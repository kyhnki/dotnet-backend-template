using DotNetBackendTemplate.Api.Persistence.Auditing;
using DotNetBackendTemplate.Api.Persistence.SoftDelete;

namespace DotNetBackendTemplate.Api.Persistence.Entities;

public sealed class Campaign : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset StartsAtUtc { get; set; }

    public DateTimeOffset EndsAtUtc { get; set; }

    public CampaignStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAtUtc { get; set; }
}

public enum CampaignStatus
{
    Draft = 0,
}
