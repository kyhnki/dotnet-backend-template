using DotNetBackendTemplate.Api.Persistence.Auditing;

namespace DotNetBackendTemplate.Api.Persistence.Entities;

public sealed class Campaign : IAuditableEntity
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset StartsAtUtc { get; set; }

    public DateTimeOffset EndsAtUtc { get; set; }

    public CampaignStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public enum CampaignStatus
{
    Draft = 0,
}
