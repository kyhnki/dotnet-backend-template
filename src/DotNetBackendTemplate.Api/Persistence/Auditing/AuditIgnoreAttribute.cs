namespace DotNetBackendTemplate.Api.Persistence.Auditing;

[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute;
