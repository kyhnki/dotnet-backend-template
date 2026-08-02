using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DotNetBackendTemplate.Api.Persistence.Auditing;

internal sealed class AuditSaveChangesInterceptor(
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AddAuditLogs(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditLogs(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditLogs(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        // EnableRetryOnFailure re-runs SavingChanges on transient failures; audit rows staged by
        // the failed attempt are still Added and would duplicate. Audit rows are created only here.
        foreach (EntityEntry<AuditLog> staged in dbContext.ChangeTracker.Entries<AuditLog>())
        {
            if (staged.State == EntityState.Added)
            {
                staged.State = EntityState.Detached;
            }
        }

        dbContext.ChangeTracker.DetectChanges();
        DateTimeOffset occurredAtUtc = timeProvider.GetUtcNow();
        HttpContext? httpContext = httpContextAccessor.HttpContext;

        AuditLog[] auditLogs = dbContext.ChangeTracker
            .Entries<IAuditableEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => CreateAuditLog(entry, occurredAtUtc, httpContext))
            .ToArray();

        if (auditLogs.Length > 0)
        {
            dbContext.Set<AuditLog>().AddRange(auditLogs);
        }
    }

    private static AuditLog CreateAuditLog(
        EntityEntry<IAuditableEntity> entry,
        DateTimeOffset occurredAtUtc,
        HttpContext? httpContext)
    {
        AuditAction action = entry.State switch
        {
            EntityState.Added => AuditAction.Added,
            EntityState.Modified => AuditAction.Modified,
            EntityState.Deleted => AuditAction.Deleted,
            _ => throw new UnreachableException(),
        };

        Dictionary<string, object?>? oldValues = action switch
        {
            AuditAction.Modified => GetValues(entry, useOriginalValues: true, modifiedOnly: true),
            AuditAction.Deleted => GetValues(entry, useOriginalValues: true, modifiedOnly: false),
            _ => null,
        };

        Dictionary<string, object?>? newValues = action switch
        {
            AuditAction.Added => GetValues(entry, useOriginalValues: false, modifiedOnly: false),
            AuditAction.Modified => GetValues(entry, useOriginalValues: false, modifiedOnly: true),
            _ => null,
        };

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = occurredAtUtc,
            Action = action,
            EntityName = entry.Metadata.ClrType.Name,
            EntityId = GetEntityId(entry),
            UserId = GetUserId(httpContext?.User),
            TraceId = Activity.Current?.Id ?? httpContext?.TraceIdentifier,
            RequestPath = httpContext?.Request.Path.Value,
            OldValuesJson = Serialize(oldValues),
            NewValuesJson = Serialize(newValues),
        };
    }

    private static Dictionary<string, object?> GetValues(
        EntityEntry entry,
        bool useOriginalValues,
        bool modifiedOnly) => entry.Properties
        .Where(property => !modifiedOnly || property.IsModified)
        .Where(property => !IsIgnored(property.Metadata.PropertyInfo))
        .ToDictionary(
            property => property.Metadata.Name,
            property => useOriginalValues ? property.OriginalValue : property.CurrentValue);

    private static bool IsIgnored(PropertyInfo? propertyInfo) =>
        propertyInfo?.GetCustomAttribute<AuditIgnoreAttribute>() is not null;

    private static string GetEntityId(EntityEntry entry)
    {
        IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.IProperty>? keyProperties =
            entry.Metadata.FindPrimaryKey()?.Properties;

        if (keyProperties is null or { Count: 0 })
        {
            return "<no-key>";
        }

        if (keyProperties.Count == 1)
        {
            object? value = entry.Property(keyProperties[0].Name).CurrentValue;
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>";
        }

        Dictionary<string, object?> values = keyProperties.ToDictionary(
            property => property.Name,
            property => entry.Property(property.Name).CurrentValue);

        return JsonSerializer.Serialize(values, SerializerOptions);
    }

    private static string? GetUserId(ClaimsPrincipal? user) =>
        user?.FindFirst("sub")?.Value
        ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private static string? Serialize(Dictionary<string, object?>? values) =>
        values is null ? null : JsonSerializer.Serialize(values, SerializerOptions);
}
