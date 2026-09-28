using DotNetBackendTemplate.Api.Persistence.Auditing;
using DotNetBackendTemplate.Api.Persistence.SoftDelete;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>PERS-04, PERS-05, PERS-06, PERS-08 and PERS-11, checked against the EF Core model.</summary>
public sealed class PersistenceModelRulesTests
{
    [Fact]
    public void ProductionModelFollowsPersistenceRules()
    {
        IModel model = ProductionCode.DesignTimeModel();

        // AuditLog is part of the template rather than the removable example, so it is always mapped.
        model.FindEntityType(typeof(AuditLog)).Should().NotBeNull("an empty model would make these rules pass vacuously");
        ArchitectureAssert.NoViolations(PersistenceModelRules.Check(model, ProductionCode.RootNamespace, typeof(AuditLog)));
    }

    [Theory]
    [InlineData("PERS-04", "AuditableAuditLog")]
    [InlineData("PERS-05", "AuditableWithIdentityKey")]
    [InlineData("PERS-05", "AuditableWithDatabaseKey")]
    [InlineData("PERS-05", "AuditableWithManualKey")]
    [InlineData("PERS-06", "SoftDeletableWithoutFilter")]
    [InlineData("PERS-08", "SoftDeletableWithUnfilteredUniqueIndex")]
    [InlineData("PERS-11", "EntityOutsidePersistence")]
    public void PersistenceRulesCatchDeliberateViolations(string ruleId, string subject)
    {
        using ViolatingDbContext context = new(
            new DbContextOptionsBuilder<ViolatingDbContext>().UseSqlServer("Server=unused").Options);
        IModel model = context.GetService<IDesignTimeModel>().Model;

        ArchitectureAssert.Reports(
            PersistenceModelRules.Check(model, FixtureRoot.Namespace, typeof(AuditableAuditLog)),
            ruleId,
            subject);
    }
}

internal static class PersistenceModelRules
{
    public static IEnumerable<ArchitectureViolation> Check(IModel model, string root, Type auditLogType)
    {
        string persistence = $"{root}.Persistence";

        if (typeof(IAuditableEntity).IsAssignableFrom(auditLogType))
        {
            yield return new("PERS-04", auditLogType.FullName!, "implements IAuditableEntity. Audit rows must not audit themselves.");
        }

        if (typeof(ISoftDeletable).IsAssignableFrom(auditLogType))
        {
            yield return new("PERS-04", auditLogType.FullName!, "implements ISoftDeletable. Audit rows are immutable records, not business data.");
        }

        foreach (IEntityType entity in model.GetEntityTypes())
        {
            Type clrType = entity.ClrType;
            string subject = clrType.FullName ?? entity.Name;

            if (!Namespaces.IsWithin(clrType.Namespace, persistence))
            {
                yield return new("PERS-11", subject, $"is mapped by the DbContext but declared outside {persistence}. EF entities belong to Persistence.");
            }

            if (typeof(IAuditableEntity).IsAssignableFrom(clrType))
            {
                foreach (ArchitectureViolation violation in CheckAuditableKey(entity, subject))
                {
                    yield return violation;
                }
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                foreach (ArchitectureViolation violation in CheckSoftDelete(entity, subject))
                {
                    yield return violation;
                }
            }
        }
    }

    private static IEnumerable<ArchitectureViolation> CheckAuditableKey(IEntityType entity, string subject)
    {
        IKey? key = entity.FindPrimaryKey();
        if (key is not { Properties.Count: 1 } || key.Properties[0].ClrType != typeof(Guid))
        {
            yield return new("PERS-05", subject,
                "is auditable but has no single Guid primary key. Audit rows record the key during SavingChanges, so it must be generated client-side (ADR-0006).");
            yield break;
        }

        IProperty property = key.Properties[0];
        if (property.ValueGenerated != ValueGenerated.OnAdd)
        {
            yield return new("PERS-05", subject,
                $"has key generation '{property.ValueGenerated}'. Leave the key unset and let EF's sequential GUID generator assign it instead of assigning it in code (ADR-0006).");
        }

        if (property.GetDefaultValueSql() is { } sql)
        {
            yield return new("PERS-05", subject,
                $"has the database default '{sql}' on its key. A database-generated key is unknown during SavingChanges, so audit rows could not record it (ADR-0006).");
        }
    }

    private static IEnumerable<ArchitectureViolation> CheckSoftDelete(IEntityType entity, string subject)
    {
        if (entity.GetDeclaredQueryFilters().Count == 0)
        {
            yield return new("PERS-06", subject,
                "implements ISoftDeletable but has no global query filter, so deleted rows would appear in every query.");
        }

        foreach (IIndex index in entity.GetIndexes().Where(index => index.IsUnique))
        {
            if (index.GetFilter()?.Contains(nameof(ISoftDeletable.IsDeleted), StringComparison.Ordinal) != true)
            {
                string columns = string.Join(", ", index.Properties.Select(property => property.Name));
                yield return new("PERS-08", subject,
                    $"has a unique index on ({columns}) that is not filtered to active rows, so a deleted row would reserve its value forever. Add HasFilter(\"[IsDeleted] = 0\").");
            }
        }
    }
}
