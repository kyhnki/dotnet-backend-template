using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotNetBackendTemplate.Api.Features.Campaigns;
using DotNetBackendTemplate.IntegrationTests.Infrastructure;

namespace DotNetBackendTemplate.IntegrationTests.Authentication;

public sealed class JwtAuthenticationTests(JwtApiFactory factory) : IClassFixture<JwtApiFactory>
{
    [Theory]
    [InlineData("missing")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expired")]
    public async Task RealBearerHandlerRejectsInvalidTokens(string scenario)
    {
        using HttpClient client = factory.CreateClient();
        if (scenario != "missing")
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(scenario));
        }

        using HttpResponseMessage response = await client.GetAsync("/api/v1/campaigns");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("valid", HttpStatusCode.Created)]
    [InlineData("role", HttpStatusCode.Forbidden)]
    public async Task RealBearerClaimsDriveCampaignWritePolicy(string scenario, HttpStatusCode expected)
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(scenario));
        DateTimeOffset starts = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/campaigns",
            new CreateCampaign.Request("JWT " + Guid.NewGuid(), starts, starts.AddDays(1)));
        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.Created)
        {
            response.Headers.Location.Should().NotBeNull();
            using HttpResponseMessage detail = await client.GetAsync(response.Headers.Location);
            detail.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
