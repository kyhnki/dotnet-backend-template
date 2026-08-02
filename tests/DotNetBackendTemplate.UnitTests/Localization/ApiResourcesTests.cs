using System.Globalization;
using System.Resources;
using DotNetBackendTemplate.Api.Infrastructure.Localization;

namespace DotNetBackendTemplate.UnitTests.Localization;

public sealed class ApiResourcesTests
{
    private static readonly ResourceManager ResourceManager = new(
        "DotNetBackendTemplate.Api.Resources.Infrastructure.Localization.ApiResources",
        typeof(ApiResources).Assembly);

    public static TheoryData<string> ResourceKeys => new()
    {
        "ValidationErrorTitle",
        "UnexpectedErrorTitle",
        "CampaignNameRequired",
        "CampaignNameMaximumLength",
        "CampaignStartsAtUtcMustUseUtcOffset",
        "CampaignEndsAtUtcMustUseUtcOffset",
        "CampaignEndsAtUtcMustBeAfterStartsAtUtc",
    };

    [Theory]
    [MemberData(nameof(ResourceKeys))]
    public void ClientFacingResourceHasEnglishAndTurkishValues(string resourceKey)
    {
        string? english = ResourceManager.GetString(
            resourceKey,
            CultureInfo.GetCultureInfo("en-US"));
        string? turkish = ResourceManager.GetString(
            resourceKey,
            CultureInfo.GetCultureInfo("tr-TR"));

        english.Should().NotBeNullOrWhiteSpace();
        turkish.Should().NotBeNullOrWhiteSpace();
        english.Should().NotBe(turkish);
    }
}
