using DotNetBackendTemplate.Api.Persistence.Auditing;
using DotNetBackendTemplate.Api.Persistence.SoftDelete;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence.Entities;

// PERS-04: passed to the model check as the audit log type.
internal sealed class AuditableAuditLog : IAuditableEntity, ISoftDeletable
{
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAtUtc { get; set; }
}

// PERS-05: an auditable key the database generates, so it is unknown during SavingChanges.
internal sealed class AuditableWithIdentityKey : IAuditableEntity
{
    public int Id { get; set; }
}

// PERS-05: a Guid key with a database default (NEWSEQUENTIALID), unknown during SavingChanges.
internal sealed class AuditableWithDatabaseKey : IAuditableEntity
{
    public Guid Id { get; set; }
}

// PERS-05: a key the application must assign itself, which invites Guid.NewGuid().
internal sealed class AuditableWithManualKey : IAuditableEntity
{
    public Guid Id { get; set; }
}

// PERS-06: soft-deletable without a query filter.
internal sealed class SoftDeletableWithoutFilter : ISoftDeletable
{
    public Guid Id { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAtUtc { get; set; }
}

// PERS-08: a unique index that a deleted row would keep reserving.
internal sealed class SoftDeletableWithUnfilteredUniqueIndex : ISoftDeletable
{
    public Guid Id { get; set; }

    public required string Code { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAtUtc { get; set; }
}
