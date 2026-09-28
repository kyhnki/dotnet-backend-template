using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence;

internal static class DatabaseMaintenance
{
    // PERS-01: migrating from application code.
    public static void Migrate(ViolatingDbContext database) => database.Database.Migrate();

    // PERS-07: a read path that sees soft-deleted rows without being reviewed.
    public static IQueryable<SoftDeletableWithoutFilter> Everything(ViolatingDbContext database) =>
        database.Set<SoftDeletableWithoutFilter>().IgnoreQueryFilters();

    // PERS-10: a bulk delete, only inside a lambda, that bypasses the audit and soft-delete interceptors.
    public static Func<Task<int>> Purge(ViolatingDbContext database) =>
        () => database.Set<SoftDeletableWithoutFilter>().ExecuteDeleteAsync();

    // PERS-18: an explicit transaction outside the execution strategy, which EnableRetryOnFailure rejects.
    public static int SaveInTransaction(ViolatingDbContext database)
    {
        using IDbContextTransaction transaction = database.Database.BeginTransaction();
        int saved = database.SaveChanges();
        transaction.Commit();
        return saved;
    }
}
