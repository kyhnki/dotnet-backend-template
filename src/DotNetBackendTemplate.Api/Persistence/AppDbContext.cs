using System.Linq.Expressions;
using DotNetBackendTemplate.Api.Persistence.Auditing;
using DotNetBackendTemplate.Api.Persistence.Entities;
using DotNetBackendTemplate.Api.Persistence.SoftDelete;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DotNetBackendTemplate.Api.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Applying the filter by convention keeps soft delete opt-in through ISoftDeletable
        // instead of something every entity configuration has to remember.
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            ParameterExpression entity = Expression.Parameter(entityType.ClrType, "entity");
            LambdaExpression filter = Expression.Lambda(
                Expression.Not(
                    Expression.Property(entity, nameof(ISoftDeletable.IsDeleted))),
                entity);

            entityType.SetQueryFilter(filter);
        }
    }
}
