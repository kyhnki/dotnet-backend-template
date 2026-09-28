using DotNetBackendTemplate.Api.Persistence;
using DotNetBackendTemplate.Api.Persistence.Auditing;
using DotNetBackendTemplate.Api.Persistence.Entities;
using DotNetBackendTemplate.Api.Persistence.SoftDelete;
using DotNetBackendTemplate.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetBackendTemplate.IntegrationTests.Persistence;

public sealed class PersistenceSafetyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task SqlFailureRollsBackBusinessAndAuditRows()
    {
        Guid id;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Campaign campaign = NewCampaign(new string('x', 201)); // Violates the real nvarchar(200) column.
            context.Campaigns.Add(campaign);
            id = campaign.Id;
            Func<Task> save = () => context.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();
            context.ChangeTracker.Entries<AuditLog>().Should().ContainSingle();
        }

        using IServiceScope verification = factory.Services.CreateScope();
        AppDbContext fresh = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        (await fresh.Campaigns.IgnoreQueryFilters().AnyAsync(row => row.Id == id)).Should().BeFalse();
        (await fresh.AuditLogs.AnyAsync(row => row.EntityId == id.ToString())).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReenteredSaveWritesExactlyOneAuditRecord(bool delete)
    {
        Campaign campaign = NewCampaign("Retry " + Guid.NewGuid());
        if (delete)
        {
            using IServiceScope seed = factory.Services.CreateScope();
            AppDbContext seedContext = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            seedContext.Campaigns.Add(campaign);
            await seedContext.SaveChangesAsync();
        }

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            FailAfterAuditOnce fault = new();
            DbContextOptions<AppDbContext> registered = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
            await using AppDbContext context = new(new DbContextOptionsBuilder<AppDbContext>(registered).AddInterceptors(fault).Options);
            if (delete)
            {
                Campaign tracked = await context.Campaigns.SingleAsync(row => row.Id == campaign.Id);
                context.Campaigns.Remove(tracked);
            }
            else
            {
                context.Campaigns.Add(campaign);
            }

            int attempts = 0;
            RetryInjectedFailure strategy = new(context);
            await strategy.ExecuteAsync(async () =>
            {
                attempts++;
                await context.SaveChangesAsync();
            });
            attempts.Should().Be(2);
            fault.StagedAuditRows.Should().Be(1, "the failure must happen after the real audit interceptor staged its row");
        }

        using IServiceScope verification = factory.Services.CreateScope();
        AppDbContext fresh = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Campaign stored = await fresh.Campaigns.IgnoreQueryFilters().SingleAsync(row => row.Id == campaign.Id);
        stored.IsDeleted.Should().Be(delete);
        (await fresh.Campaigns.AnyAsync(row => row.Id == campaign.Id)).Should().Be(!delete);
        AuditAction expected = delete ? AuditAction.Deleted : AuditAction.Added;
        List<AuditLog> audits = await fresh.AuditLogs.Where(row => row.EntityId == campaign.Id.ToString() && row.Action == expected).ToListAsync();
        audits.Should().ContainSingle();
        audits[0].OccurredAtUtc.Should().Be(factory.Clock.GetUtcNow());
    }

    [Fact]
    public async Task SoftDeletedUniqueValueCanBeReusedButActiveDuplicateCannot()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContextOptions<AppDbContext> registered = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        AppDbContext application = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IInterceptor[] interceptors = [.. registered.Extensions.OfType<CoreOptionsExtension>().SelectMany(extension => extension.Interceptors ?? [])];
        DbContextOptions<UniqueProbeContext> options = new DbContextOptionsBuilder<UniqueProbeContext>()
            .UseSqlServer(application.Database.GetConnectionString()).AddInterceptors(interceptors).Options;
        await using UniqueProbeContext context = new(options);
        // Test-only table in the disposable container, not a production schema/migration change.
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE [SoftDeleteUniqueProbe] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                [Code] nvarchar(100) NOT NULL,
                [IsDeleted] bit NOT NULL,
                [DeletedAtUtc] datetimeoffset NULL);
            CREATE UNIQUE INDEX [IX_SoftDeleteUniqueProbe_Code] ON [SoftDeleteUniqueProbe] ([Code]) WHERE [IsDeleted] = 0;
            """);
        UniqueProbe original = new() { Code = "reusable" };
        context.Add(original);
        await context.SaveChangesAsync();

        await using (UniqueProbeContext duplicate = new(options))
        {
            duplicate.Add(new UniqueProbe { Code = "reusable" });
            Func<Task> save = () => duplicate.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();
        }

        context.Remove(original);
        await context.SaveChangesAsync();
        (await context.Set<UniqueProbe>().CountAsync()).Should().Be(0);
        context.Add(new UniqueProbe { Code = "reusable" });
        await context.SaveChangesAsync();

        await using UniqueProbeContext fresh = new(options);
        (await fresh.Set<UniqueProbe>().CountAsync()).Should().Be(1);
        (await fresh.Set<UniqueProbe>().IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await fresh.Set<UniqueProbe>().IgnoreQueryFilters().SingleAsync(row => row.Id == original.Id))
            .DeletedAtUtc.Should().Be(factory.Clock.GetUtcNow());
    }

    private Campaign NewCampaign(string name) => new()
    {
        Name = name,
        StartsAtUtc = factory.Clock.GetUtcNow(),
        EndsAtUtc = factory.Clock.GetUtcNow().AddDays(1),
        CreatedAtUtc = factory.Clock.GetUtcNow(),
        Status = CampaignStatus.Draft,
    };

    private sealed class InjectedSaveException : Exception;

    private sealed class FailAfterAuditOnce : SaveChangesInterceptor
    {
        private bool _failed;

        public int StagedAuditRows { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                StagedAuditRows = eventData.Context!.ChangeTracker.Entries<AuditLog>().Count(entry => entry.State == EntityState.Added);
                throw new InjectedSaveException();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class RetryInjectedFailure(DbContext context) : ExecutionStrategy(context, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is InjectedSaveException;
    }

    private sealed class UniqueProbe : ISoftDeletable
    {
        public Guid Id { get; set; }

        public required string Code { get; set; }

        public bool IsDeleted { get; set; }

        public DateTimeOffset? DeletedAtUtc { get; set; }
    }

    private sealed class UniqueProbeContext(DbContextOptions<UniqueProbeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
            modelBuilder.Entity<UniqueProbe>(builder =>
            {
                builder.ToTable("SoftDeleteUniqueProbe");
                builder.HasKey(row => row.Id);
                builder.Property(row => row.Code).HasMaxLength(100);
                builder.HasQueryFilter(row => !row.IsDeleted);
                builder.HasIndex(row => row.Code).IsUnique().HasFilter("[IsDeleted] = 0");
            });
        }
    }
}
