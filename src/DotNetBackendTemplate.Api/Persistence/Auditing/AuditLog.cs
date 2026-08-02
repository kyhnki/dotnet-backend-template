namespace DotNetBackendTemplate.Api.Persistence.Auditing;

public sealed class AuditLog
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public AuditAction Action { get; set; }

    public required string EntityName { get; set; }

    public required string EntityId { get; set; }

    public string? UserId { get; set; }

    public string? TraceId { get; set; }

    public string? RequestPath { get; set; }

    public string? OldValuesJson { get; set; }

    public string? NewValuesJson { get; set; }
}

public enum AuditAction
{
    Added = 0,
    Modified = 1,
    Deleted = 2,
}
