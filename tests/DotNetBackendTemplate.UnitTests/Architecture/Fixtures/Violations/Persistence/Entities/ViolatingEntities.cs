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

// PERS-13: an enum stored with EF's default integer mapping.
internal sealed class EntityWithUnconvertedEnum
{
    public Guid Id { get; set; }

    public OrderStatus Status { get; set; }
}

// PERS-14: a string column with no maximum length, which becomes nvarchar(max).
internal sealed class EntityWithUnboundedString
{
    public Guid Id { get; set; }

    public string? Notes { get; set; }
}

// TIME-02: a DateTime instead of a UTC DateTimeOffset.
internal sealed class EntityWithDateTime
{
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }
}

// PERS-15 and PERS-16: a filtered, soft-deletable parent whose plain child is required and cascade-deleted.
internal sealed class SoftDeletableParent : ISoftDeletable
{
    public Guid Id { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAtUtc { get; set; }

    public List<PlainChild> Children { get; } = [];
}

internal sealed class PlainChild
{
    public Guid Id { get; set; }

    public Guid ParentId { get; set; }

    public SoftDeletableParent Parent { get; set; } = null!;
}

// PERS-17: auditable entities whose values the audit interceptor cannot see.
internal sealed class AuditableWithComplexType : IAuditableEntity
{
    public Guid Id { get; set; }

    public FixtureAddress Address { get; set; } = new();
}

internal sealed class FixtureAddress
{
    public string Street { get; set; } = string.Empty;
}

internal sealed class AuditableWithOwnedType : IAuditableEntity
{
    public Guid Id { get; set; }

    public FixtureMoney Budget { get; set; } = new();
}

internal sealed class FixtureMoney
{
    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;
}

// PERS-03: a sensitive-looking value without [AuditIgnore], next to one that is correctly ignored.
internal sealed class AuditableWithSecret : IAuditableEntity
{
    public Guid Id { get; set; }

    public string? PasswordHash { get; set; }

    [AuditIgnore]
    public string? ApiToken { get; set; }
}
