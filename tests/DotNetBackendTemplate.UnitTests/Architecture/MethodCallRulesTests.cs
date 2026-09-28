using System.Reflection;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations;
using MediatR;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>
/// Rules about which methods code may call: SLICE-03, PERS-01, the code half of PERS-05, PERS-07, PERS-10, PERS-18,
/// PERS-19, TIME-01, and SEC-02. Calls are read from IL, so a call inside an async method or a lambda is attributed to
/// the type the developer wrote.
/// </summary>
public sealed class MethodCallRulesTests
{
    // Exact IL method identity (return type, declaring type, name and parameters), never a namespace wildcard.
    private static readonly IReadOnlyList<ReviewedMethodException> ReviewedExceptions = [];

    /// <summary>
    /// Reviewed read paths allowed to see soft-deleted rows (PERS-07). Add an entry as
    /// <c>[typeof(SomeHandler).FullName!] = "why it may see deleted rows, and the ADR that records it"</c>.
    /// </summary>
    private static readonly Dictionary<string, string> IgnoreQueryFiltersAllowlist = new(StringComparer.Ordinal);

    [Fact]
    public void ProductionCodeFollowsMethodCallRules()
    {
        IReadOnlyList<CallSite> sites = CallSites.Read(ProductionCode.Assembly);

        // Without call sites, or without the handlers the CQRS checks inspect, these rules would pass vacuously.
        sites.Should().Contain(site => Namespaces.IsWithin(site.CallerNamespace, ProductionCode.RootNamespace));

        foreach (ReviewedMethodException exception in ReviewedExceptions)
        {
            exception.IsComplete.Should().BeTrue("exceptions need an exact method, rationale, ADR and behavior-test reference");
            sites.Should().Contain(site => site.CallerMethod == exception.Method, "stale or wildcard exemptions must not remain");
            exception.Adr.Should().StartWith("docs/adr/");
            File.Exists(Path.Combine(ProductionCode.RepositoryRoot, exception.Adr)).Should().BeTrue();
            string[] testSources = Directory.GetFiles(Path.Combine(ProductionCode.RepositoryRoot, "tests"), "*.cs", SearchOption.AllDirectories);
            testSources.Should().Contain(path => File.ReadAllText(path).Contains(exception.BehaviorTest + "(", StringComparison.Ordinal),
                "the referenced behavior test must exist; review verifies it actually covers this exception");
        }

        ArchitectureAssert.NoViolations(MethodCallRules.Check(
            sites,
            ProductionCode.Assembly.GetTypes(),
            ProductionCode.RootNamespace,
            IgnoreQueryFiltersAllowlist.Keys.ToHashSet(StringComparer.Ordinal), ReviewedExceptions));
    }

    [Theory]
    [InlineData("SLICE-03", "GetOrder+Handler")]
    [InlineData("PERS-19", "CancelOrder+Handler")]
    [InlineData("PERS-05", "CancelOrder+Handler")]
    [InlineData("TIME-01", "CancelOrder+Handler")]
    [InlineData("SEC-02", "CancelOrder+Handler")]
    [InlineData("PERS-01", "DatabaseMaintenance")]
    [InlineData("PERS-07", "DatabaseMaintenance")]
    [InlineData("PERS-10", "DatabaseMaintenance")]
    [InlineData("PERS-18", "DatabaseMaintenance")]
    public void MethodCallRulesCatchDeliberateViolations(string ruleId, string subject) =>
        ArchitectureAssert.Reports(FixtureViolations(), ruleId, subject);

    [Fact]
    public void QueryRuleReportsBothSavingAndTracking()
    {
        List<ArchitectureViolation> query = [.. FixtureViolations().Where(violation => violation.RuleId == "SLICE-03")];

        query.Should().Contain(violation => violation.Detail.Contains("SaveChanges", StringComparison.Ordinal));
        query.Should().Contain(violation => violation.Detail.Contains("AsNoTracking", StringComparison.Ordinal));
    }

    private static List<ArchitectureViolation> FixtureViolations() =>
    [
        .. MethodCallRules.Check(
            CallSites.Read(typeof(FixtureRoot).Assembly),
            typeof(FixtureRoot).Assembly.GetTypes().Where(type => Namespaces.IsWithin(type.Namespace, FixtureRoot.Namespace)),
            FixtureRoot.Namespace,
            new HashSet<string>(StringComparer.Ordinal)),
    ];
}

/// <summary>One call instruction, attributed to the type the developer wrote rather than a compiler-generated one.</summary>
internal sealed record CallSite(string Caller, string CallerNamespace, string CalleeType, string CalleeName, string CallerMethod = "")
{
    public bool Is(string calleeType, params string[] names) => CalleeType == calleeType && names.Contains(CalleeName);

    public bool IsEntityFramework(params string[] names) =>
        CalleeType.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) && names.Contains(CalleeName);
}

internal static class CallSites
{
    public static IReadOnlyList<CallSite> Read(Assembly assembly)
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(assembly.Location);
        List<CallSite> sites = [];

        foreach (TypeDefinition type in module.GetTypes())
        {
            TypeDefinition written = WrittenType(type);
            string caller = written.FullName.Replace('/', '+');
            string callerNamespace = TopLevel(written).Namespace;

            foreach (MethodDefinition method in type.Methods.Where(method => method.HasBody))
            {
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference callee && IsCall(instruction.OpCode))
                    {
                        sites.Add(new(caller, callerNamespace, callee.DeclaringType.GetElementType().FullName, callee.Name,
                            WrittenMethod(type, method, written).FullName));
                    }
                }
            }
        }

        return sites;
    }

    private static bool IsCall(OpCode opCode) =>
        opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj
        || opCode == OpCodes.Ldftn || opCode == OpCodes.Ldvirtftn;

    private static MethodDefinition WrittenMethod(TypeDefinition type, MethodDefinition method, TypeDefinition written)
    {
        // State-machine ownership is unambiguous even for overloaded Handle methods.
        MethodDefinition? original = written.Methods.FirstOrDefault(candidate => candidate.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"
            && attribute.ConstructorArguments.Count == 1
            && attribute.ConstructorArguments[0].Value is TypeReference stateMachine
            && stateMachine.FullName == type.FullName));
        // Closures retain their exact generated method identity: they never inherit an enclosing method exemption.
        return original ?? method;
    }

    /// <summary>Async state machines and closures are nested types whose names start with '&lt;'.</summary>
    private static TypeDefinition WrittenType(TypeDefinition type)
    {
        TypeDefinition current = type;
        while (current.IsNested && current.Name.StartsWith('<'))
        {
            current = current.DeclaringType;
        }

        return current;
    }

    private static TypeDefinition TopLevel(TypeDefinition type)
    {
        TypeDefinition current = type;
        while (current.IsNested)
        {
            current = current.DeclaringType;
        }

        return current;
    }
}

internal static class MethodCallRules
{
    private const string Queryable = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";

    public static IEnumerable<ArchitectureViolation> Check(
        IReadOnlyList<CallSite> sites,
        IEnumerable<Type> types,
        string root,
        IReadOnlySet<string> ignoreQueryFiltersAllowlist,
        IReadOnlyList<ReviewedMethodException>? reviewedExceptions = null)
    {
        List<CallSite> owned = [.. sites.Where(site => Namespaces.IsWithin(site.CallerNamespace, root))];
        string features = $"{root}.Features";
        string persistence = $"{root}.Persistence";

        bool IsReviewed(string rule, string method) => reviewedExceptions?.Any(exception =>
            exception.RuleId == rule && exception.Method == method && exception.IsComplete) == true;

        foreach (CallSite site in owned.DistinctBy(site => (site.CallerMethod, site.Caller, site.CalleeType, site.CalleeName)))
        {
            string ns = site.CallerNamespace;

            if (site.Is("Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions", "Migrate", "MigrateAsync"))
            {
                yield return new("PERS-01", site.Caller, $"calls {site.CalleeName}. Migrations are applied explicitly, never from application code.");
            }

            if (site.Is("System.Guid", "NewGuid", "CreateVersion7")
                && (Namespaces.IsWithin(ns, features) || Namespaces.IsWithin(ns, persistence))
                && !IsReviewed("PERS-05", site.CallerMethod))
            {
                yield return new("PERS-05", site.Caller,
                    $"calls Guid.{site.CalleeName}. Leave entity keys unset so EF assigns SQL Server-ordered GUIDs (ADR-0006).");
            }

            if (site.Is(Queryable, "IgnoreQueryFilters") && !ignoreQueryFiltersAllowlist.Contains(site.Caller))
            {
                yield return new("PERS-07", site.Caller,
                    "calls IgnoreQueryFilters, which exposes soft-deleted rows. Review intent and authorization, then add the type to the allowlist in MethodCallRulesTests with the reason.");
            }

            if (site.IsEntityFramework("ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync")
                || (site.CalleeType.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                    && site.CalleeName.StartsWith("ExecuteSql", StringComparison.Ordinal)))
            {
                yield return new("PERS-10", site.Caller,
                    $"calls {site.CalleeName}, which bypasses change tracking and the audit and soft-delete interceptors.");
            }

            if (site.Is("System.TimeProvider", "get_System") && !Namespaces.IsWithin(ns, $"{root}.Common.Extensions"))
            {
                yield return new("TIME-01", site.Caller,
                    "reads TimeProvider.System directly. Inject TimeProvider; only composition code registers the system clock.");
            }

            if (site.Is("Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder", "RequireRole")
                && !Namespaces.IsWithin(ns, $"{root}.Infrastructure"))
            {
                yield return new("SEC-02", site.Caller,
                    "builds a role requirement outside Infrastructure. Define the policy in Infrastructure/Authorization and reference its constant.");
            }
        }

        foreach (IGrouping<string, CallSite> caller in owned.GroupBy(site => site.CallerMethod))
        {
            if (caller.Any(site => site.IsEntityFramework("BeginTransaction", "BeginTransactionAsync"))
                && !IsReviewed("PERS-18", caller.Key))
            {
                yield return new("PERS-18", caller.Key,
                    "opens an explicit transaction without a reviewed method exception. Review the execution-strategy scope and supply an ADR and rollback test; merely creating a strategy proves nothing.");
            }
        }

        ILookup<string, CallSite> callsByCaller = owned.ToLookup(site => site.Caller);
        foreach ((Type handler, Type request) in Handlers(types))
        {
            List<CallSite> calls = [.. callsByCaller[handler.FullName!]];
            int saves = calls.Count(IsSaveChanges);

            if (!request.Name.EndsWith("Query", StringComparison.Ordinal) && !request.Name.EndsWith("Command", StringComparison.Ordinal))
            {
                yield return new("SLICE-03", handler.FullName!, "handles a request whose name must end with Command or Query. The suffix declares its CQRS intent; review verifies its actual behavior.");
            }

            if (request.Name.EndsWith("Query", StringComparison.Ordinal))
            {
                if (saves > 0)
                {
                    yield return new("SLICE-03", handler.FullName!, "is a query handler but calls SaveChanges. Queries never change state.");
                }

                if (!calls.Any(site => site.Is(Queryable, "AsNoTracking", "AsNoTrackingWithIdentityResolution")))
                {
                    yield return new("SLICE-03", handler.FullName!, "is a query handler but never calls AsNoTracking.");
                }
            }
            else
            {
                foreach (IGrouping<string, CallSite> method in calls.GroupBy(site => site.CallerMethod))
                {
                    int methodSaves = method.Count(IsSaveChanges);
                    if (methodSaves > 1 && !IsReviewed("PERS-19", method.Key))
                    {
                        yield return new("PERS-19", handler.FullName!,
                            $"method {method.Key} has {methodSaves} SaveChanges call sites. Save once, or record a method-scoped exception with an ADR and atomicity test. Call-site counts are not execution counts.");
                    }
                }
            }
        }
    }

    private static bool IsSaveChanges(CallSite site) =>
        site.CalleeName is "SaveChanges" or "SaveChangesAsync"
        && site.CalleeType.EndsWith("DbContext", StringComparison.Ordinal);

    private static IEnumerable<(Type Handler, Type Request)> Handlers(IEnumerable<Type> types) =>
        types.SelectMany(type => type.GetInterfaces()
            .Where(contract => contract.IsGenericType
                && (contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                    || contract.GetGenericTypeDefinition() == typeof(IRequestHandler<>)))
            .Select(contract => (type, contract.GetGenericArguments()[0])));
}

internal sealed record ReviewedMethodException(string RuleId, string Method, string Reason, string Adr, string BehaviorTest)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(Method) && !string.IsNullOrWhiteSpace(Reason)
        && !string.IsNullOrWhiteSpace(Adr) && !string.IsNullOrWhiteSpace(BehaviorTest)
        && !Method.Contains('*', StringComparison.Ordinal);
}
