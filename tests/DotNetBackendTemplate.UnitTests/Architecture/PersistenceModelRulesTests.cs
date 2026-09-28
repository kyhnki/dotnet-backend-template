using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using DotNetBackendTemplate.Api.Persistence;
using DotNetBackendTemplate.Api.Persistence.Auditing;
using DotNetBackendTemplate.Api.Persistence.SoftDelete;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>
/// PERS-03, PERS-04, PERS-05, PERS-06, PERS-08, PERS-11 through PERS-17, and the entity half of TIME-02, checked
/// against the EF Core model and the migrations.
/// </summary>
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

    [Fact]
    public void ProductionModelMatchesTheLatestMigration()
    {
        using AppDbContext context = new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused").Options);

        ArchitectureAssert.NoViolations(PersistenceModelRules.CheckMigrations(context));
    }

    [Theory]
    [InlineData("PERS-03", "AuditableWithSecret")]
    [InlineData("PERS-04", "AuditableAuditLog")]
    [InlineData("PERS-05", "AuditableWithIdentityKey")]
    [InlineData("PERS-05", "AuditableWithDatabaseKey")]
    [InlineData("PERS-05", "AuditableWithManualKey")]
    [InlineData("PERS-06", "SoftDeletableWithoutFilter")]
    [InlineData("PERS-08", "SoftDeletableWithUnfilteredUniqueIndex")]
    [InlineData("PERS-11", "EntityOutsidePersistence")]
    [InlineData("PERS-13", "EntityWithUnconvertedEnum")]
    [InlineData("PERS-14", "EntityWithUnboundedString")]
    [InlineData("PERS-15", "PlainChild")]
    [InlineData("PERS-16", "PlainChild")]
    [InlineData("PERS-17", "AuditableWithComplexType")]
    [InlineData("PERS-17", "AuditableWithOwnedType")]
    [InlineData("TIME-02", "EntityWithDateTime")]
    public void PersistenceRulesCatchDeliberateViolations(string ruleId, string subject) =>
        ArchitectureAssert.Reports(FixtureViolations(), ruleId, subject);

    [Fact]
    public void SensitivePropertyRuleNamesTheUnignoredPropertyOnly()
    {
        List<ArchitectureViolation> violations = [.. FixtureViolations().Where(violation => violation.RuleId == "PERS-03")];

        violations.Should().ContainSingle(violation => violation.Detail.Contains("PasswordHash", StringComparison.Ordinal));
        violations.Should().NotContain(
            violation => violation.Detail.Contains("ApiToken", StringComparison.Ordinal),
            "a property marked [AuditIgnore] is already excluded from the audit trail");
    }

    [Fact]
    public void MigrationRuleCatchesModelChangesWithoutAMigration()
    {
        using ViolatingDbContext context = ViolatingContext();

        ArchitectureAssert.Reports(PersistenceModelRules.CheckMigrations(context), "PERS-12", nameof(ViolatingDbContext));
    }

    private static List<ArchitectureViolation> FixtureViolations()
    {
        using ViolatingDbContext context = ViolatingContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        return [.. PersistenceModelRules.Check(model, FixtureRoot.Namespace, typeof(AuditableAuditLog))];
    }

    private static ViolatingDbContext ViolatingContext() =>
        new(new DbContextOptionsBuilder<ViolatingDbContext>().UseSqlServer("Server=unused").Options);
}

internal static partial class PersistenceModelRules
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
            bool isAuditable = typeof(IAuditableEntity).IsAssignableFrom(clrType);

            if (!Namespaces.IsWithin(clrType.Namespace, persistence))
            {
                yield return new("PERS-11", subject, $"is mapped by the DbContext but declared outside {persistence}. EF entities belong to Persistence.");
            }

            IEnumerable<ArchitectureViolation> violations = CheckProperties(entity, subject, isAuditable)
                .Concat(CheckRelationships(entity, subject));

            if (isAuditable)
            {
                violations = violations.Concat(CheckAuditableKey(entity, subject)).Concat(CheckAuditableShape(entity, subject));
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                violations = violations.Concat(CheckSoftDelete(entity, subject));
            }

            foreach (ArchitectureViolation violation in violations)
            {
                yield return violation;
            }
        }
    }

    /// <summary>PERS-12: the model matches the latest migration, so an entity change cannot ship without one.</summary>
    public static IEnumerable<ArchitectureViolation> CheckMigrations(DbContext context)
    {
        if (context.Database.HasPendingModelChanges())
        {
            yield return new("PERS-12", context.GetType().Name,
                "has model changes that no migration captures. Add one with 'dotnet ef migrations add'; migrations are never applied automatically, so the gap would first appear in production.");
        }
    }

    private static IEnumerable<ArchitectureViolation> CheckProperties(IEntityType entity, string subject, bool isAuditable)
    {
        foreach (IProperty property in entity.GetProperties())
        {
            Type clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            Type providerType = property.GetValueConverter()?.ProviderClrType ?? property.GetProviderClrType() ?? clrType;

            if (clrType == typeof(DateTime))
            {
                yield return new("TIME-02", subject,
                    $"property {property.Name} is a DateTime. Use a UTC DateTimeOffset (ADR-0003).");
            }

            if (clrType.IsEnum && providerType != typeof(string))
            {
                yield return new("PERS-13", subject,
                    $"stores enum {property.Name} as {providerType.Name}. Use HasConversion<string>(); names become persisted values, so renaming needs a data migration or compatible mapping.");
            }

            // GetColumnType() falls back to the provider's resolved store type, nvarchar(max) for an unbounded string,
            // so only an explicit HasColumnType annotation counts as a deliberate choice.
            bool hasExplicitColumnType = property.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value is not null;
            if (providerType == typeof(string) && property.GetMaxLength() is null && !hasExplicitColumnType)
            {
                yield return new("PERS-14", subject,
                    $"property {property.Name} has no maximum length and would become nvarchar(max), which cannot be indexed. Set HasMaxLength, or HasColumnType(\"nvarchar(max)\") when unbounded is intended.");
            }

            if (isAuditable
                && SensitiveName().IsMatch(property.Name)
                && property.PropertyInfo?.GetCustomAttribute<AuditIgnoreAttribute>() is null)
            {
                yield return new("PERS-03", subject,
                    $"property {property.Name} looks sensitive but is not marked [AuditIgnore], so its value would be written to the audit trail.");
            }
        }
    }

    private static IEnumerable<ArchitectureViolation> CheckRelationships(IEntityType dependent, string subject)
    {
        foreach (IForeignKey foreignKey in dependent.GetForeignKeys().Where(foreignKey => !foreignKey.IsOwnership))
        {
            IEntityType principal = foreignKey.PrincipalEntityType;
            string principalName = principal.ClrType.Name;

            if (typeof(ISoftDeletable).IsAssignableFrom(principal.ClrType)
                && !typeof(ISoftDeletable).IsAssignableFrom(dependent.ClrType)
                && foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade)
            {
                yield return new("PERS-15", subject,
                    $"is cascade-deleted with soft-deletable {principalName}. Soft-deleting the parent would hard-delete this row. Make it soft-deletable too, or use DeleteBehavior.Restrict.");
            }

            if (foreignKey.IsRequired && HasQueryFilter(principal) && !HasQueryFilter(dependent))
            {
                yield return new("PERS-16", subject,
                    $"has a required relationship to filtered {principalName} but no query filter itself, so its navigation can be null for a deleted parent. EF Core only logs this at runtime.");
            }
        }
    }

    private static IEnumerable<ArchitectureViolation> CheckAuditableShape(IEntityType entity, string subject)
    {
        foreach (IComplexProperty complex in entity.GetComplexProperties())
        {
            yield return new("PERS-17", subject,
                $"maps complex property {complex.Name}, which the audit interceptor does not read, so its values would be missing from the audit trail.");
        }

        foreach (INavigation owned in entity.GetNavigations().Where(navigation => navigation.TargetEntityType.IsOwned()))
        {
            yield return new("PERS-17", subject,
                $"owns {owned.Name}, a separate entry the audit interceptor skips, so its values would be missing from the audit trail.");
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
        if (!entity.GetDeclaredQueryFilters().Any(filter => filter.Expression is { } expression && RequiresActiveRow(expression)))
        {
            yield return new("PERS-06", subject,
                "needs a recognized mandatory !IsDeleted (or IsDeleted == false) query-filter condition. Unknown expressions require review; an unrelated filter does not protect deleted rows.");
        }

        foreach (IIndex index in entity.GetIndexes().Where(index => index.IsUnique))
        {
            if (!IsActiveRowIndexFilter(index.GetFilter()))
            {
                string columns = string.Join(", ", index.Properties.Select(property => property.Name));
                yield return new("PERS-08", subject,
                    $"has a unique index on ({columns}) that is not filtered to active rows, so a deleted row would reserve its value forever. Add HasFilter(\"[IsDeleted] = 0\").");
            }
        }
    }

    private static bool HasQueryFilter(IEntityType entity) => entity.GetDeclaredQueryFilters().Count > 0;

    internal static bool IsActiveRowIndexFilter(string? filter) => filter is not null
        && Regex.Replace(filter, @"[\s()]", string.Empty) == "[IsDeleted]=0";

    internal static bool RequiresActiveRow(LambdaExpression filter) =>
        filter.Parameters.Count == 1 && RequiresActiveRow(filter.Body, filter.Parameters[0]);

    private static bool RequiresActiveRow(Expression expression, ParameterExpression entity) => expression switch
    {
        UnaryExpression { NodeType: ExpressionType.Not } not => IsDeletedMember(not.Operand, entity),
        BinaryExpression { NodeType: ExpressionType.Equal } equal =>
            (IsDeletedMember(equal.Left, entity) && equal.Right is ConstantExpression { Value: false })
            || (IsDeletedMember(equal.Right, entity) && equal.Left is ConstantExpression { Value: false }),
        BinaryExpression { NodeType: ExpressionType.AndAlso } and =>
            RequiresActiveRow(and.Left, entity) || RequiresActiveRow(and.Right, entity),
        _ => false,
    };

    private static bool IsDeletedMember(Expression expression, ParameterExpression entity) =>
        expression is MemberExpression { Member.Name: nameof(ISoftDeletable.IsDeleted) } member
        && member.Expression == entity && member.Type == typeof(bool);

    // A heuristic: a name match is a prompt to decide, and [AuditIgnore] records the decision.
    [GeneratedRegex("password|secret|token|apikey|privatekey|connectionstring", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();
}
