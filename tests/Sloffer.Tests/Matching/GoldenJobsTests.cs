using System.Text.Json;
using Microsoft.Extensions.Options;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Matching;

namespace Sloffer.Tests.Matching;

/// <summary>
/// Golden examples (<c>GoldenJobs/golden-jobs.json</c>): real-looking jobs with the score range they must land in.
/// Every scoring mistake found in real data becomes a golden example before it is fixed. Cases with a <c>pending</c>
/// note describe a rule that a later PR adds; they are listed by the skipped theory until that PR removes the note.
/// </summary>
public sealed class GoldenJobsTests
{
    private static readonly JobScorer Scorer = new(Options.Create(new JobScoringOptions()));

    public static TheoryData<string> ActiveCases() => Names(c => c.Pending is null);

    public static TheoryData<string> PendingCases() => Names(c => c.Pending is not null);

    [Theory]
    [MemberData(nameof(ActiveCases))]
    public void Golden_job_scores_in_its_expected_range(string name)
    {
        var golden = Load().Single(c => c.Name == name);

        var result = Scorer.Score(new FetchedJob(
            "golden", name, "https://example.test/golden", golden.Title, "Example Co", golden.Location, "Full-Time",
            null, golden.Description, new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero)));

        var because = $"{golden.Name}: score {result.Score}, reasons [{string.Join("; ", result.Reasons)}]";
        Assert.True(result.Score >= golden.MinScore && result.Score <= golden.MaxScore,
            $"{because}; expected {golden.MinScore}-{golden.MaxScore}");
        if (golden.ReasonContains is { } expectedReason)
            Assert.True(result.Reasons.Any(r => r.Contains(expectedReason, StringComparison.Ordinal)),
                $"{because}; expected a reason containing '{expectedReason}'");
    }

    [Theory(Skip = "Rule lands in a later scoring-v2 PR (see the case's 'pending' note).")]
    [MemberData(nameof(PendingCases))]
    public void Golden_job_pending_a_later_rule(string name) => Golden_job_scores_in_its_expected_range(name);

    [Fact]
    public void Golden_job_names_are_unique()
    {
        var names = Load().Select(c => c.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    private static TheoryData<string> Names(Func<GoldenJob, bool> filter)
    {
        var data = new TheoryData<string>();
        foreach (var golden in Load().Where(filter))
            data.Add(golden.Name);
        return data;
    }

    private static List<GoldenJob> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Matching", "GoldenJobs", "golden-jobs.json");
        return JsonSerializer.Deserialize<List<GoldenJob>>(File.ReadAllText(path), JsonOptions) ?? [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record GoldenJob(
        string Name,
        string Title,
        string? Location,
        string? Description,
        int MinScore,
        int MaxScore,
        string? ReasonContains,
        string? Pending);
}
