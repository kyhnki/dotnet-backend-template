using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Invoices;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Orders;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence;

internal sealed class ViolatingDbContext(DbContextOptions<ViolatingDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditableWithIdentityKey>();

        modelBuilder.Entity<AuditableWithDatabaseKey>()
            .Property(entity => entity.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        modelBuilder.Entity<AuditableWithManualKey>()
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<SoftDeletableWithoutFilter>();

        modelBuilder.Entity<SoftDeletableWithUnfilteredUniqueIndex>(builder =>
        {
            builder.HasQueryFilter(entity => !entity.IsDeleted);
            builder.HasIndex(entity => entity.Code).IsUnique();
        });

        modelBuilder.Entity<EntityOutsidePersistence>();

        modelBuilder.Entity<EntityWithUnconvertedEnum>();
        modelBuilder.Entity<EntityWithUnboundedString>();
        modelBuilder.Entity<EntityWithDateTime>();

        modelBuilder.Entity<SoftDeletableParent>(builder =>
        {
            builder.HasQueryFilter(entity => !entity.IsDeleted);
            builder.HasMany(parent => parent.Children).WithOne(child => child.Parent).IsRequired();
        });

        modelBuilder.Entity<AuditableWithComplexType>().ComplexProperty(entity => entity.Address);
        modelBuilder.Entity<AuditableWithOwnedType>().OwnsOne(entity => entity.Budget);
        modelBuilder.Entity<AuditableWithSecret>();
    }
}

// DEP-02: persistence code that depends on a feature type.
internal static class PersistenceFeatureLeak
{
    public static InvoiceSummary? Latest() => null;
}
