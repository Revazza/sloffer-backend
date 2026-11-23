using System.Net;
using System.Net.Http.Json;
using Sloffer.Api.Contracts;

namespace Sloffer.Tests;

[Collection(ApiCollection.Name)]
public sealed class HealthEndpointTests(SlofferApiFactory factory)
{
    [Fact]
    public async Task Get_health_returns_healthy_including_database()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
        Assert.Equal("Healthy", body.Checks["database"]);
    }
}
