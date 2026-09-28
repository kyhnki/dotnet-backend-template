using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Common.Exceptions;

// DEP-03: exception handling that depends on the DbContext.
internal static class ExceptionHandlerDbContextLeak
{
    public static bool IsDatabase(object candidate) => candidate is ViolatingDbContext;
}
