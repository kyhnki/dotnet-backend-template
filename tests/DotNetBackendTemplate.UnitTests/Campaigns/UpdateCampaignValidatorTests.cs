using System.Globalization;
using DotNetBackendTemplate.Api.Features.Campaigns;
using DotNetBackendTemplate.Api.Infrastructure.Localization;
using Microsoft.Extensions.Localization;

namespace DotNetBackendTemplate.UnitTests.Campaigns;

public sealed class UpdateCampaignValidatorTests
{
    private readonly UpdateCampaign.Validator _validator = new(new StubStringLocalizer<ApiResources>());

    [Fact]
    public async Task ValidCommandPassesValidation()
    {
        UpdateCampaign.Command command = new(
            Guid.NewGuid(),
            "Renamed campaign",
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero));

        FluentValidation.Results.ValidationResult result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyNameAndInvalidRangeFailValidation()
    {
        DateTimeOffset startsAt = new(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
        UpdateCampaign.Command command = new(Guid.NewGuid(), "", startsAt, startsAt.AddMinutes(-1));

        FluentValidation.Results.ValidationResult result = await _validator.ValidateAsync(command);

        result.Errors.Should().Contain(error => error.PropertyName == nameof(command.Name));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(command.EndsAtUtc));
    }

    [Fact]
    public async Task NonUtcOffsetsFailValidation()
    {
        TimeSpan offset = TimeSpan.FromHours(3);
        UpdateCampaign.Command command = new(
            Guid.NewGuid(),
            "Istanbul campaign",
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, offset),
            new DateTimeOffset(2026, 8, 2, 10, 0, 0, offset));

        FluentValidation.Results.ValidationResult result = await _validator.ValidateAsync(command);

        result.Errors.Should().Contain(error => error.PropertyName == nameof(command.StartsAtUtc));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(command.EndsAtUtc));
    }

    private sealed class StubStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.InvariantCulture, name, arguments), resourceNotFound: false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
