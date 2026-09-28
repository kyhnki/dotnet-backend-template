using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations;
using FluentValidation;
using MediatR;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>SLICE-01: handlers and feature validators live in the feature area that owns their request.</summary>
public sealed class SliceRulesTests
{
    [Fact]
    public void ProductionHandlersAndValidatorsLiveInTheirFeature() =>
        ArchitectureAssert.NoViolations(SliceRules.Check(ProductionCode.Assembly.GetTypes(), ProductionCode.RootNamespace));

    [Theory]
    [InlineData("MisplacedHandler")]
    [InlineData("CrossAreaHandler")]
    [InlineData("MisplacedValidator")]
    public void SliceRuleCatchesDeliberateViolations(string subject)
    {
        IEnumerable<Type> fixtureTypes = typeof(FixtureRoot).Assembly
            .GetTypes()
            .Where(type => Namespaces.IsWithin(type.Namespace, FixtureRoot.Namespace));

        ArchitectureAssert.Reports(SliceRules.Check(fixtureTypes, FixtureRoot.Namespace), "SLICE-01", subject);
    }
}

internal static class SliceRules
{
    public static IEnumerable<ArchitectureViolation> Check(IEnumerable<Type> types, string root)
    {
        string features = $"{root}.Features";

        foreach (Type type in types)
        {
            foreach (Type request in HandledRequests(type))
            {
                foreach (ArchitectureViolation violation in CheckOwnership(type, "handles", request, features))
                {
                    yield return violation;
                }
            }

            foreach (Type request in ValidatedRequests(type))
            {
                foreach (ArchitectureViolation violation in CheckOwnership(type, "validates", request, features))
                {
                    yield return violation;
                }
            }
        }
    }

    private static IEnumerable<ArchitectureViolation> CheckOwnership(Type type, string verb, Type request, string features)
    {
        string? area = Namespaces.SegmentBelow(type.Namespace, features);
        string? requestArea = Namespaces.SegmentBelow(request.Namespace, features);

        if (area is null)
        {
            yield return new("SLICE-01", Display(type),
                $"{verb} {Display(request)} but lives outside {features}. Handlers and feature validators belong to the feature that owns the use case.");
        }
        else if (area != requestArea)
        {
            yield return new("SLICE-01", Display(type),
                $"lives in feature area '{area}' but {verb} {Display(request)} from '{requestArea ?? "outside Features"}'.");
        }
    }

    private static IEnumerable<Type> HandledRequests(Type type) =>
        type.GetInterfaces()
            .Where(contract => contract.IsGenericType
                && (contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                    || contract.GetGenericTypeDefinition() == typeof(IRequestHandler<>)))
            .Select(contract => contract.GetGenericArguments()[0]);

    private static IEnumerable<Type> ValidatedRequests(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(AbstractValidator<>)
                && typeof(IBaseRequest).IsAssignableFrom(current.GetGenericArguments()[0]))
            {
                yield return current.GetGenericArguments()[0];
            }
        }
    }

    private static string Display(Type type) => type.FullName ?? type.Name;
}
