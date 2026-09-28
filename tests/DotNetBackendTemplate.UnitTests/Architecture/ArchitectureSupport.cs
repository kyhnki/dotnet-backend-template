namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>One broken architecture rule, phrased for the developer who has to fix it.</summary>
internal sealed record ArchitectureViolation(string RuleId, string Subject, string Detail)
{
    public override string ToString() => $"[{RuleId}] {Subject}: {Detail}";
}

internal static class ArchitectureAssert
{
    private const string Guidance =
        "Each rule is described under its ID in docs/architecture-rules.md. Fix the code, or record a justified "
        + "exception in an ADR as described in section 8, \"Changing a rule\".";

    public static void NoViolations(IEnumerable<ArchitectureViolation> violations)
    {
        List<ArchitectureViolation> found =
        [
            .. violations
                .Distinct()
                .OrderBy(violation => violation.RuleId, StringComparer.Ordinal)
                .ThenBy(violation => violation.Subject, StringComparer.Ordinal),
        ];

        if (found.Count > 0)
        {
            string report = string.Join(Environment.NewLine, found.Select(violation => $"  {violation}"));
            Assert.Fail(
                $"{found.Count} architecture rule violation(s):{Environment.NewLine}{report}{Environment.NewLine}{Guidance}");
        }
    }

    /// <summary>
    /// Asserts that a rule reports its deliberate violation. A rule that stops reporting it has silently stopped
    /// working, and every production check that depends on it would keep passing.
    /// </summary>
    public static void Reports(IEnumerable<ArchitectureViolation> violations, string ruleId, string subject)
    {
        List<ArchitectureViolation> found = [.. violations];
        string reported = found.Count == 0 ? "nothing" : string.Join("; ", found);

        found.Should().Contain(
            violation => violation.RuleId == ruleId && violation.Subject.Contains(subject, StringComparison.Ordinal),
            "{0} must catch its deliberate violation in {1}, otherwise it passes vacuously. Reported: {2}",
            ruleId,
            subject,
            reported);
    }

    /// <summary>Asserts that a heuristic rule does not flag a case it must accept.</summary>
    public static void DoesNotReport(IEnumerable<ArchitectureViolation> violations, string ruleId, string subject) =>
        violations.Should().NotContain(
            violation => violation.RuleId == ruleId && violation.Subject.Contains(subject, StringComparison.Ordinal),
            "{0} must not flag {1}",
            ruleId,
            subject);
}

internal static class Namespaces
{
    public static bool IsWithin(string? candidate, string root) =>
        candidate is not null
        && (candidate == root || candidate.StartsWith(root + ".", StringComparison.Ordinal));

    /// <summary>The first namespace segment below <paramref name="root"/>, such as <c>Campaigns</c> in <c>…Features.Campaigns.Sub</c>.</summary>
    public static string? SegmentBelow(string? candidate, string root)
    {
        if (candidate is null || !candidate.StartsWith(root + ".", StringComparison.Ordinal))
        {
            return null;
        }

        string rest = candidate[(root.Length + 1)..];
        int dot = rest.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? rest : rest[..dot];
    }
}
