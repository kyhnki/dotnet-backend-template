using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetBackendTemplate.Api.Features.Campaigns;
using DotNetBackendTemplate.Api.Infrastructure.Authorization;
using DotNetBackendTemplate.Api.Persistence;
using DotNetBackendTemplate.Api.Persistence.Auditing;
using DotNetBackendTemplate.Api.Persistence.Entities;
using DotNetBackendTemplate.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetBackendTemplate.IntegrationTests.Campaigns;

public sealed class CampaignEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task CampaignEndpointWithoutAuthenticationReturnsUnauthorized()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/campaigns");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null, "Authentication is required.")]
    [InlineData("tr-TR", "Kimlik doğrulaması gerekiyor.")]
    public async Task UnauthorizedRequestReturnsLocalizedProblemDetails(string? language, string title)
    {
        using HttpClient client = factory.CreateClient();
        if (language is not null)
        {
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        }

        HttpResponseMessage response = await client.GetAsync("/api/v1/campaigns");

        await AssertProblemDetailsAsync(response, HttpStatusCode.Unauthorized, title);
    }

    [Fact]
    public async Task ForbiddenRequestReturnsProblemDetails()
    {
        using HttpClient client = factory.CreateAuthenticatedClient(role: null);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/campaigns",
            ValidRequest($"Forbidden problem {Guid.NewGuid():N}"));

        await AssertProblemDetailsAsync(response, HttpStatusCode.Forbidden, "You do not have permission to perform this action.");
    }

    [Fact]
    public async Task UnknownCampaignReturnsProblemDetails()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/api/v1/campaigns/{Guid.NewGuid()}");

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound, "The requested resource was not found.");
    }

    [Fact]
    public async Task CreateCampaignWithoutWriteRoleReturnsForbidden()
    {
        using HttpClient client = factory.CreateAuthenticatedClient(role: null);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/campaigns",
            ValidRequest($"Forbidden {Guid.NewGuid():N}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(ApiRoles.Administrator)]
    [InlineData(ApiRoles.CampaignManager)]
    public async Task CampaignWriteRolesCanCreateCampaign(string role)
    {
        using HttpClient client = factory.CreateAuthenticatedClient(role);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/campaigns",
            ValidRequest($"Authorized {Guid.NewGuid():N}"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateThenGetCampaignReturnsCreatedCampaign()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        object request = ValidRequest($"Campaign {Guid.NewGuid():N}");

        HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/v1/campaigns", request);
        CreateCampaign.Response? created = await createResponse.Content
            .ReadFromJsonAsync<CreateCampaign.Response>(JsonOptions);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Should().NotBeNull();
        CreateCampaign.Response createdCampaign = created!;
        createResponse.Headers.Location?.OriginalString
            .Should().Be($"/api/v1/campaigns/{createdCampaign.Id}");

        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/v1/campaigns/{createdCampaign.Id}");
        GetCampaign.Response? campaign = await getResponse.Content
            .ReadFromJsonAsync<GetCampaign.Response>(JsonOptions);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        campaign.Should().NotBeNull();
        GetCampaign.Response returnedCampaign = campaign!;
        returnedCampaign.Id.Should().Be(createdCampaign.Id);
        returnedCampaign.Status.ToString().Should().Be("Draft");
    }

    [Fact]
    public async Task UpdateCampaignPersistsChangesAndWritesAuditLog()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/v1/campaigns",
            ValidRequest($"Before update {Guid.NewGuid():N}"));
        CreateCampaign.Response? created = await createResponse.Content
            .ReadFromJsonAsync<CreateCampaign.Response>(JsonOptions);

        created.Should().NotBeNull();
        CreateCampaign.Response createdCampaign = created!;
        string updatedName = $"After update {Guid.NewGuid():N}";

        HttpResponseMessage updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/campaigns/{createdCampaign.Id}",
            new
            {
                name = updatedName,
                startsAtUtc = "2026-09-01T08:00:00+00:00",
                endsAtUtc = "2026-09-05T08:00:00+00:00",
            });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/v1/campaigns/{createdCampaign.Id}");
        GetCampaign.Response? campaign = await getResponse.Content
            .ReadFromJsonAsync<GetCampaign.Response>(JsonOptions);

        campaign.Should().NotBeNull();
        GetCampaign.Response updatedCampaign = campaign!;
        updatedCampaign.Name.Should().Be(updatedName);
        updatedCampaign.StartsAtUtc.Should().Be(
            new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        updatedCampaign.EndsAtUtc.Should().Be(
            new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero));

        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        AuditLog modified = await dbContext.AuditLogs
            .AsNoTracking()
            .SingleAsync(log => log.EntityId == createdCampaign.Id.ToString()
                && log.Action == AuditAction.Modified);

        modified.UserId.Should().Be("integration-test-user");
        modified.NewValuesJson.Should().Contain(updatedName);
    }

    [Fact]
    public async Task UpdateCampaignWithUnknownIdReturnsNotFound()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/campaigns/{Guid.NewGuid()}",
            ValidRequest($"Missing {Guid.NewGuid():N}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateCampaignWithoutWriteRoleReturnsForbidden()
    {
        using HttpClient client = factory.CreateAuthenticatedClient(role: null);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/campaigns/{Guid.NewGuid()}",
            ValidRequest($"Forbidden update {Guid.NewGuid():N}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateCampaignWithInvalidInputReturnsValidationProblem()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        object request = new
        {
            name = "",
            startsAtUtc = "2026-08-02T10:00:00+00:00",
            endsAtUtc = "2026-08-01T10:00:00+00:00",
        };

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/campaigns/{Guid.NewGuid()}",
            request);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        body.RootElement.GetProperty("errors").TryGetProperty("name", out _)
            .Should().BeTrue();
        body.RootElement.GetProperty("errors").TryGetProperty("endsAtUtc", out _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task GeneratedKeysSortInCreationOrderInSqlServer()
    {
        // Entity keys are left unset so EF assigns sequential GUIDs ordered for SQL Server's
        // uniqueidentifier comparison, which keeps the clustered primary key from fragmenting on
        // insert. Ordering is evaluated by SQL Server, so this asserts the property directly
        // rather than an implementation detail.
        using HttpClient client = factory.CreateAuthenticatedClient();
        string prefix = $"Ordered {Guid.NewGuid():N}";
        List<Guid> createdInOrder = [];

        for (int index = 0; index < 5; index++)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/v1/campaigns",
                ValidRequest($"{prefix} {index}"));
            CreateCampaign.Response? created = await response.Content
                .ReadFromJsonAsync<CreateCampaign.Response>(JsonOptions);

            created.Should().NotBeNull();
            createdInOrder.Add(created!.Id);
        }

        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        List<Guid> sortedByKey = await dbContext.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Name.StartsWith(prefix))
            .OrderBy(campaign => campaign.Id)
            .Select(campaign => campaign.Id)
            .ToListAsync();

        sortedByKey.Should().Equal(createdInOrder);
    }

    [Fact]
    public async Task DeleteCampaignSoftDeletesAndHidesItFromQueries()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        string campaignName = $"Deleted {Guid.NewGuid():N}";
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/v1/campaigns",
            ValidRequest(campaignName));
        CreateCampaign.Response? created = await createResponse.Content
            .ReadFromJsonAsync<CreateCampaign.Response>(JsonOptions);

        created.Should().NotBeNull();
        CreateCampaign.Response createdCampaign = created!;

        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/v1/campaigns/{createdCampaign.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/v1/campaigns/{createdCampaign.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        HttpResponseMessage listResponse = await client.GetAsync("/api/v1/campaigns?page=1&pageSize=100");
        string listJson = await listResponse.Content.ReadAsStringAsync();
        listJson.Should().NotContain(campaignName);

        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The row is retained; only the global query filter hides it.
        Campaign campaign = await dbContext.Campaigns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == createdCampaign.Id);

        campaign.IsDeleted.Should().BeTrue();
        campaign.DeletedAtUtc.Should().Be(factory.Clock.GetUtcNow());
        campaign.Name.Should().Be(campaignName);
    }

    [Fact]
    public async Task DeleteCampaignRecordsDeletedAuditLog()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        string campaignName = $"Audited delete {Guid.NewGuid():N}";
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/v1/campaigns",
            ValidRequest(campaignName));
        CreateCampaign.Response? created = await createResponse.Content
            .ReadFromJsonAsync<CreateCampaign.Response>(JsonOptions);

        created.Should().NotBeNull();
        CreateCampaign.Response createdCampaign = created!;

        await client.DeleteAsync($"/api/v1/campaigns/{createdCampaign.Id}");

        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        AuditLog deleted = await dbContext.AuditLogs
            .AsNoTracking()
            .SingleAsync(log => log.EntityId == createdCampaign.Id.ToString()
                && log.Action == AuditAction.Deleted);

        deleted.UserId.Should().Be("integration-test-user");
        deleted.OldValuesJson.Should().Contain(campaignName);
        deleted.NewValuesJson.Should().BeNull();
    }

    [Fact]
    public async Task SoftDeleteIsAuditedAsDeletedWhenTheEntryIsAlreadyModified()
    {
        // A retried SaveChanges re-enters the pipeline with the entry already converted to
        // Modified, and interceptor order is not guaranteed. The audit action is derived from
        // IsDeleted changing rather than from EntityState, so both cases still record Deleted.
        using HttpClient client = factory.CreateAuthenticatedClient();
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/v1/campaigns",
            ValidRequest($"Already modified {Guid.NewGuid():N}"));
        CreateCampaign.Response? created = await createResponse.Content
            .ReadFromJsonAsync<CreateCampaign.Response>(JsonOptions);

        created.Should().NotBeNull();
        CreateCampaign.Response createdCampaign = created!;

        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Campaign campaign = await dbContext.Campaigns
            .SingleAsync(item => item.Id == createdCampaign.Id);

        campaign.IsDeleted = true;
        campaign.DeletedAtUtc = factory.Clock.GetUtcNow();
        await dbContext.SaveChangesAsync();

        AuditLog audited = await dbContext.AuditLogs
            .AsNoTracking()
            .SingleAsync(log => log.EntityId == createdCampaign.Id.ToString()
                && log.Action == AuditAction.Deleted);

        audited.NewValuesJson.Should().BeNull();
    }

    [Fact]
    public async Task DeletingCampaignTwiceReturnsNotFound()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/v1/campaigns",
            ValidRequest($"Twice {Guid.NewGuid():N}"));
        CreateCampaign.Response? created = await createResponse.Content
            .ReadFromJsonAsync<CreateCampaign.Response>(JsonOptions);

        created.Should().NotBeNull();
        string route = $"/api/v1/campaigns/{created!.Id}";

        HttpResponseMessage first = await client.DeleteAsync(route);
        HttpResponseMessage second = await client.DeleteAsync(route);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteCampaignWithUnknownIdReturnsNotFound()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.DeleteAsync(
            $"/api/v1/campaigns/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteCampaignWithoutWriteRoleReturnsForbidden()
    {
        using HttpClient client = factory.CreateAuthenticatedClient(role: null);

        HttpResponseMessage response = await client.DeleteAsync(
            $"/api/v1/campaigns/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateCampaignWritesTransactionalAuditLog()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        string campaignName = $"Audited {Guid.NewGuid():N}";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/campaigns",
            ValidRequest(campaignName));
        CreateCampaign.Response? created = await response.Content
            .ReadFromJsonAsync<CreateCampaign.Response>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Should().NotBeNull();
        CreateCampaign.Response createdCampaign = created!;

        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Campaign campaign = await dbContext.Campaigns
            .AsNoTracking()
            .SingleAsync(item => item.Id == createdCampaign.Id);
        AuditLog auditLog = await dbContext.AuditLogs
            .AsNoTracking()
            .SingleAsync(log => log.EntityId == createdCampaign.Id.ToString());

        auditLog.Action.Should().Be(AuditAction.Added);
        campaign.CreatedAtUtc.Should().Be(factory.Clock.GetUtcNow());
        auditLog.OccurredAtUtc.Should().Be(factory.Clock.GetUtcNow());
        auditLog.EntityName.Should().Be("Campaign");
        auditLog.UserId.Should().Be("integration-test-user");
        auditLog.RequestPath.Should().Be("/api/v1/campaigns");
        auditLog.TraceId.Should().NotBeNullOrWhiteSpace();
        auditLog.OldValuesJson.Should().BeNull();
        auditLog.NewValuesJson.Should().NotBeNull();
        auditLog.NewValuesJson.Should().Contain(campaignName);
    }

    [Fact]
    public async Task UpdatingAndDeletingCampaignWritesOldAndNewAuditValues()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        string originalName = $"Original {Guid.NewGuid():N}";
        string updatedName = $"Updated {Guid.NewGuid():N}";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/campaigns",
            ValidRequest(originalName));
        CreateCampaign.Response? created = await response.Content
            .ReadFromJsonAsync<CreateCampaign.Response>(JsonOptions);

        created.Should().NotBeNull();
        CreateCampaign.Response createdCampaign = created!;

        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Campaign campaign = await dbContext.Campaigns.SingleAsync(
            item => item.Id == createdCampaign.Id);

        campaign.Name = updatedName;
        await dbContext.SaveChangesAsync();

        dbContext.Campaigns.Remove(campaign);
        await dbContext.SaveChangesAsync();

        List<AuditLog> auditLogs = await dbContext.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityId == createdCampaign.Id.ToString())
            .OrderBy(log => log.OccurredAtUtc)
            .ToListAsync();

        AuditLog modified = auditLogs.Should()
            .ContainSingle(log => log.Action == AuditAction.Modified)
            .Which;
        modified.OldValuesJson.Should().NotBeNull();
        modified.NewValuesJson.Should().NotBeNull();
        modified.OldValuesJson.Should().Contain(originalName);
        modified.NewValuesJson.Should().Contain(updatedName);

        AuditLog deleted = auditLogs.Should()
            .ContainSingle(log => log.Action == AuditAction.Deleted)
            .Which;
        deleted.OldValuesJson.Should().NotBeNull();
        deleted.NewValuesJson.Should().BeNull();
        deleted.OldValuesJson.Should().Contain(updatedName);
    }

    [Fact]
    public async Task GetCampaignWithUnknownIdReturnsNotFound()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/api/v1/campaigns/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateCampaignWithInvalidInputReturnsValidationProblem()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        object request = new
        {
            name = "",
            startsAtUtc = "2026-08-02T10:00:00+00:00",
            endsAtUtc = "2026-08-01T10:00:00+00:00",
        };

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/campaigns", request);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        GetContentLanguage(response).Should().Be("en-US");
        body.RootElement.GetProperty("title").GetString()
            .Should().Be("One or more validation errors occurred.");
        body.RootElement.GetProperty("errors").TryGetProperty("name", out _)
            .Should().BeTrue();
        body.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("tr-TR", "tr-TR")]
    [InlineData("tr", "tr")]
    public async Task TurkishAcceptLanguageLocalizesValidationProblem(
        string language,
        string expectedContentLanguage)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        object request = new
        {
            name = "",
            startsAtUtc = "2026-08-01T10:00:00+03:00",
            endsAtUtc = "2026-08-02T10:00:00+03:00",
        };

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/campaigns", request);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement errors = body.RootElement.GetProperty("errors");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        GetContentLanguage(response).Should().Be(expectedContentLanguage);
        body.RootElement.GetProperty("title").GetString()
            .Should().Be("Bir veya daha fazla doğrulama hatası oluştu.");
        errors.GetProperty("name")[0].GetString()
            .Should().Be("Name alanı zorunludur.");
        errors.GetProperty("startsAtUtc")[0].GetString()
            .Should().Be("StartsAtUtc, UTC saat dilimi farkını (+00:00) kullanmalıdır.");
        body.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    public async Task UnsupportedAcceptLanguageFallsBackToEnglish(string language)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        object request = new
        {
            name = "",
            startsAtUtc = "2026-08-01T10:00:00+00:00",
            endsAtUtc = "2026-08-02T10:00:00+00:00",
        };

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/campaigns", request);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        GetContentLanguage(response).Should().Be("en-US");
        body.RootElement.GetProperty("title").GetString()
            .Should().Be("One or more validation errors occurred.");
        body.RootElement.GetProperty("errors").GetProperty("name")[0].GetString()
            .Should().Be("Name is required.");
    }

    [Fact]
    public async Task ListCampaignsUsesPaginationAndDoesNotExposeEntityFields()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        await client.PostAsJsonAsync("/api/v1/campaigns", ValidRequest($"First {Guid.NewGuid():N}"));
        await client.PostAsJsonAsync("/api/v1/campaigns", ValidRequest($"Second {Guid.NewGuid():N}"));

        HttpResponseMessage response = await client.GetAsync("/api/v1/campaigns?page=1&pageSize=1000");
        string json = await response.Content.ReadAsStringAsync();
        GetCampaigns.Response? page = JsonSerializer.Deserialize<GetCampaigns.Response>(json, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        page.Should().NotBeNull();
        GetCampaigns.Response campaignPage = page!;
        campaignPage.PageSize.Should().Be(100);
        campaignPage.TotalCount.Should().BeGreaterThanOrEqualTo(2);
        json.Should().NotContainEquivalentOf("createdAtUtc");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointIsAnonymousAndHealthy(string path)
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApiDocumentDescribesPublicEndpointsAndJwtSecurity()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        root.GetProperty("openapi").GetString().Should().StartWith("3.");
        root.GetProperty("info").GetProperty("title").GetString()
            .Should().Be(".NET Backend Template API");
        root.GetProperty("info").GetProperty("version").GetString().Should().Be("v1");

        JsonElement bearer = root
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        bearer.GetProperty("type").GetString().Should().Be("http");
        bearer.GetProperty("scheme").GetString().Should().Be("bearer");
        bearer.GetProperty("bearerFormat").GetString().Should().Be("JWT");

        JsonElement paths = root.GetProperty("paths");
        JsonElement campaignCollection = paths.GetProperty("/api/v1/campaigns");
        JsonElement createCampaign = campaignCollection.GetProperty("post");
        JsonElement getCampaigns = campaignCollection.GetProperty("get");
        JsonElement getCampaign = paths.GetProperty("/api/v1/campaigns/{id}").GetProperty("get");

        AssertJwtSecurity(createCampaign);
        AssertJwtSecurity(getCampaigns);
        AssertJwtSecurity(getCampaign);
        createCampaign.GetProperty("responses").TryGetProperty("201", out _).Should().BeTrue();
        createCampaign.GetProperty("responses").TryGetProperty("400", out _).Should().BeTrue();
        createCampaign.GetProperty("responses").TryGetProperty("401", out _).Should().BeTrue();
        createCampaign.GetProperty("responses").TryGetProperty("403", out _).Should().BeTrue();
        createCampaign.TryGetProperty("requestBody", out _).Should().BeTrue();

        AssertAnonymous(paths.GetProperty("/health/live").GetProperty("get"));
        AssertAnonymous(paths.GetProperty("/health/ready").GetProperty("get"));
    }

    private static async Task AssertProblemDetailsAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
        body.RootElement.GetProperty("title").GetString().Should().Be(title);
        body.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    private static object ValidRequest(string name) => new
    {
        name,
        startsAtUtc = "2026-08-01T10:00:00+00:00",
        endsAtUtc = "2026-08-02T10:00:00+00:00",
    };

    private static string GetContentLanguage(HttpResponseMessage response)
    {
        IEnumerable<string> values = response.Headers.TryGetValues(
            "Content-Language",
            out IEnumerable<string>? responseValues)
            ? responseValues
            : response.Content.Headers.GetValues("Content-Language");

        return values.Should().ContainSingle().Which;
    }

    private static void AssertJwtSecurity(JsonElement operation)
    {
        JsonElement security = operation.GetProperty("security");
        bool hasBearerRequirement = security.EnumerateArray().Any(
            requirement => requirement.TryGetProperty("Bearer", out JsonElement scopes)
                && scopes.ValueKind == JsonValueKind.Array
                && scopes.GetArrayLength() == 0);

        hasBearerRequirement.Should().BeTrue();
    }

    private static void AssertAnonymous(JsonElement operation)
    {
        (operation.TryGetProperty("security", out JsonElement security)
            && security.GetArrayLength() > 0).Should().BeFalse();
    }
}
