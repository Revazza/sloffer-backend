using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sloffer.Jobs.Fetching;
using static Sloffer.Tests.Jobs.JobFetchTestSetup;

namespace Sloffer.Tests.Jobs;

public sealed class JobFetchServiceTests
{
    [Fact]
    public void AddJobFetching_without_config_uses_code_defaults()
    {
        using var services = CreateServices(TimeProvider.System, []);

        var options = services.GetRequiredService<IOptions<JobFetchOptions>>().Value;

        Assert.Equal(TimeSpan.FromMinutes(60), options.MinInterval);
        Assert.Equal(TimeSpan.FromMinutes(60), options.GetMinInterval("any-source"));
        Assert.Equal(TimeSpan.FromMinutes(5), options.CycleDelay);
    }

    [Fact]
    public void AddJobFetching_binds_overrides_from_JobFetch_section()
    {
        using var services = CreateServices(TimeProvider.System, [], new Dictionary<string, string?>
        {
            ["JobFetch:CycleDelay"] = "00:01:00",
            ["JobFetch:MinInterval"] = "02:00:00",
            ["JobFetch:Sources:Example:MinInterval"] = "00:15:00",
        });

        var options = services.GetRequiredService<IOptions<JobFetchOptions>>().Value;

        Assert.Equal(TimeSpan.FromMinutes(1), options.CycleDelay);
        Assert.Equal(TimeSpan.FromHours(2), options.GetMinInterval("other"));
        Assert.Equal(TimeSpan.FromMinutes(15), options.GetMinInterval("example"));
    }

    [Fact]
    public void AddJobFetching_with_zero_CycleDelay_fails_validation()
    {
        using var services = CreateServices(TimeProvider.System, [], new Dictionary<string, string?>
        {
            ["JobFetch:CycleDelay"] = "00:00:00",
        });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JobFetchOptions>>().Value);
    }

    [Fact]
    public void AddJobFetching_registers_JobFetchService_as_hosted_service()
    {
        using var services = CreateServices(TimeProvider.System, []);

        Assert.Contains(services.GetServices<IHostedService>(), s => s is JobFetchService);
    }

    [Fact]
    public async Task JobFetchService_keeps_running_cycles_until_stopped()
    {
        var calls = 0;
        var twoCalls = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeJobSource(UniqueSourceName(), _ =>
        {
            if (Interlocked.Increment(ref calls) == 2)
                twoCalls.TrySetResult();
            throw new InvalidOperationException("Failing every time must not stop the loop.");
        });
        await using var services = CreateServices(TimeProvider.System, [source], new Dictionary<string, string?>
        {
            ["JobFetch:CycleDelay"] = "00:00:00.010",
            ["JobFetch:MinInterval"] = "00:00:00",
        });
        var service = services.GetServices<IHostedService>().OfType<JobFetchService>().Single();

        await service.StartAsync(CancellationToken.None);
        await twoCalls.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.NotNull(service.ExecuteTask);
        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
    }
}
