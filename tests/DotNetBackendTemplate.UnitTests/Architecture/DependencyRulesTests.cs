using System.Reflection;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations;
using Microsoft.EntityFrameworkCore;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;
using ArchLoader = ArchUnitNET.Loader.ArchLoader;
using GenericArgument = ArchUnitNET.Domain.GenericArgument;
using IType = ArchUnitNET.Domain.IType;
using ITypeDependency = ArchUnitNET.Domain.Dependencies.ITypeDependency;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>DEP-01, DEP-02, DEP-03, DEP-05 and DEP-06 in docs/architecture-rules.md, section 2.</summary>
public sealed class DependencyRulesTests
{
    private static readonly Lazy<ArchitectureModel> FixtureArchitecture =
        new(() => new ArchLoader().LoadAssemblies(typeof(FixtureRoot).Assembly).Build());

    [Fact]
    public void ProductionCodeFollowsDependencyRules()
    {
        ArchitectureModel architecture = ProductionCode.Architecture;
        string root = ProductionCode.RootNamespace;

        // If the root namespace were derived wrongly, every rule below would pass vacuously.
        architecture.Types.Should().Contain(type => Namespaces.IsWithin(type.Namespace.FullName, $"{root}.Infrastructure"));
        architecture.Types.Should().Contain(type => Namespaces.IsWithin(type.Namespace.FullName, $"{root}.Persistence"));

        ArchitectureAssert.NoViolations(DependencyRules.Check(architecture, root));
    }

    [Fact]
    public void ProductionAssemblyDoesNotReferenceTestAssemblies() =>
        ArchitectureAssert.NoViolations(DependencyRules.CheckReferences(ProductionCode.Assembly));

    [Theory]
    [InlineData("DEP-01", "InfrastructureSignatureLeak")]
    [InlineData("DEP-01", "InfrastructureAsyncBodyLeak")]
    [InlineData("DEP-01", "InfrastructureLambdaLeak")]
    [InlineData("DEP-01", "InfrastructureGenericArgumentLeak")]
    [InlineData("DEP-02", "PersistenceFeatureLeak")]
    [InlineData("DEP-02", "Entities.Order")]
    [InlineData("DEP-03", "BehaviorFeatureLeak")]
    [InlineData("DEP-03", "ExceptionHandlerDbContextLeak")]
    [InlineData("DEP-05", "Orders.PlaceOrder")]
    public void DependencyRulesCatchDeliberateViolations(string ruleId, string subject) =>
        ArchitectureAssert.Reports(DependencyRules.Check(FixtureArchitecture.Value, FixtureRoot.Namespace), ruleId, subject);

    [Fact]
    public void ReferenceRuleCatchesTestAssemblyReferences() =>
        ArchitectureAssert.Reports(
            DependencyRules.CheckReferences("Fixture.Api", [new AssemblyName("xunit.core"), new AssemblyName("Serilog")]),
            "DEP-06",
            "Fixture.Api");
}

internal static class DependencyRules
{
    private static readonly string[] TestAssemblyPrefixes =
    [
        "xunit",
        "FluentAssertions",
        "Microsoft.NET.Test.Sdk",
        "Microsoft.TestPlatform",
        "Microsoft.VisualStudio.TestPlatform",
        "Microsoft.AspNetCore.Mvc.Testing",
        "Testcontainers",
        "ArchUnitNET",
    ];

    public static IEnumerable<ArchitectureViolation> Check(ArchitectureModel architecture, string root)
    {
        string features = $"{root}.Features";
        string infrastructure = $"{root}.Infrastructure";
        string persistence = $"{root}.Persistence";
        string entities = $"{persistence}.Entities";
        string[] genericHandling = [$"{root}.Common.Behaviors", $"{root}.Common.Exceptions"];

        foreach ((IType origin, IType target) in Dependencies(architecture))
        {
            string from = origin.Namespace.FullName;
            string to = target.Namespace.FullName;
            string subject = DisplayName(origin);

            if (Namespaces.IsWithin(from, infrastructure) && Namespaces.IsWithin(to, features))
            {
                yield return new("DEP-01", subject,
                    $"depends on feature type {target.FullName}. Infrastructure implements technical mechanisms and must not depend on Features.");
            }

            if (Namespaces.IsWithin(from, persistence) && Namespaces.IsWithin(to, features))
            {
                yield return new("DEP-02", subject,
                    $"depends on feature type {target.FullName}. Persistence must not depend on Features.");
            }

            if (Namespaces.IsWithin(from, entities)
                && (Namespaces.IsWithin(to, "Microsoft.AspNetCore") || Namespaces.IsWithin(to, "MediatR")))
            {
                yield return new("DEP-02", subject,
                    $"depends on {target.FullName}. Entities must not refer to HTTP requests, responses, or MediatR.");
            }

            if (genericHandling.Any(area => Namespaces.IsWithin(from, area)))
            {
                if (Namespaces.IsWithin(to, features))
                {
                    yield return new("DEP-03", subject,
                        $"depends on feature type {target.FullName}. Generic behaviors and exception handling must not know concrete features.");
                }
                else if (IsDbContext(target))
                {
                    yield return new("DEP-03", subject,
                        $"depends on {target.FullName}. Generic behaviors and exception handling must not use the DbContext.");
                }
            }

            string? fromArea = Namespaces.SegmentBelow(from, features);
            string? toArea = Namespaces.SegmentBelow(to, features);
            if (fromArea is not null && toArea is not null && fromArea != toArea)
            {
                yield return new("DEP-05", subject,
                    $"in feature area '{fromArea}' depends on {target.FullName} from area '{toArea}'. Cross-feature coordination needs explicit design review.");
            }
        }
    }

    public static IEnumerable<ArchitectureViolation> CheckReferences(Assembly assembly) =>
        CheckReferences(assembly.GetName().Name!, assembly.GetReferencedAssemblies());

    public static IEnumerable<ArchitectureViolation> CheckReferences(string assemblyName, IEnumerable<AssemblyName> references) =>
        references
            .Where(reference => IsTestAssembly(reference.Name))
            .Select(reference => new ArchitectureViolation("DEP-06", assemblyName,
                $"references test assembly '{reference.Name}'. Production code must never depend on test assemblies."));

    /// <summary>
    /// Every type a type depends on, including generic arguments. ArchUnitNET reads IL, so dependencies inside
    /// method bodies, lambdas, and async state machines are included; searching using directives would miss them.
    /// </summary>
    private static IEnumerable<(IType Origin, IType Target)> Dependencies(ArchitectureModel architecture) =>
        architecture.Types
            .Where(HasNamespace)
            .SelectMany(origin => origin.Dependencies
                .SelectMany(Targets)
                .Where(HasNamespace)
                .Select(target => (origin, target)))
            .Distinct();

    // Generic parameters such as TRequest have no namespace and can never be a feature, entity, or DbContext type.
    private static bool HasNamespace(IType type) => !type.IsGenericParameter && type.Namespace is not null;

    private static IEnumerable<IType> Targets(ITypeDependency dependency) =>
        Expand(dependency.Target, dependency.TargetGenericArguments);

    private static IEnumerable<IType> Expand(IType type, IEnumerable<GenericArgument> arguments) =>
        arguments.SelectMany(argument => Expand(argument.Type, argument.GenericArguments)).Prepend(type);

    /// <summary>Reports compiler-generated state machines and closures as the type the developer wrote.</summary>
    private static string DisplayName(IType type)
    {
        int generated = type.FullName.IndexOfAny(['<']);
        string name = generated < 0 ? type.FullName : type.FullName[..generated];
        return name.TrimEnd('+', '/');
    }

    private static bool IsDbContext(IType type) =>
        Type.GetType(type.AssemblyQualifiedName, throwOnError: false) is { } clrType
        && typeof(DbContext).IsAssignableFrom(clrType);

    private static bool IsTestAssembly(string? name) =>
        name is not null
        && (TestAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))
            || name.EndsWith("Tests", StringComparison.Ordinal));
}
