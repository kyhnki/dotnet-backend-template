using System.Globalization;
using System.Text.RegularExpressions;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>
/// DOC-01: docs/architecture-rules.md and the architecture tests describe the same rules. Without this check the
/// enforcement matrix drifts the first time a rule is added, renamed, or dropped on only one side.
/// </summary>
public sealed class ArchitectureRulesDocumentTests
{
    [Fact]
    public void EnforcementMatrixMatchesTheTests()
    {
        string document = File.ReadAllText(Path.Combine(ProductionCode.RepositoryRoot, "docs", "architecture-rules.md"));

        List<string> testSources =
        [
            .. Directory.EnumerateFiles(Path.Combine(ProductionCode.RepositoryRoot, "tests"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText),
        ];

        ArchitectureAssert.NoViolations(RulesDocument.Check(document, string.Join(Environment.NewLine, testSources)));
    }

    [Theory]
    [InlineData(9)] // used by a test but not defined
    [InlineData(2)] // defined but missing from the matrix
    [InlineData(3)] // marked Test but never checked
    [InlineData(4)] // checked but not marked Test
    public void DocumentRuleCatchesRuleDrift(int rule) =>
        ArchitectureAssert.Reports(RulesDocument.Check(DriftedDocument, DriftedTests), "DOC-01", FakeId(rule));

    [Fact]
    public void DocumentRuleCatchesMissingTestClass() =>
        ArchitectureAssert.Reports(RulesDocument.Check(DriftedDocument, DriftedTests), "DOC-01", "GhostRulesTests");

    private const string DriftedDocument = """
        - **ZZZ-01:** Enforced and listed.
        - **ZZZ-02:** Defined but missing from the matrix.
        - **ZZZ-03:** Marked Test but never checked.
        - **ZZZ-04:** Checked but not marked Test.
        ### Enforcement matrix
        | Rule | Enforcement | Where |
        | --- | --- | --- |
        | ZZZ-01, ZZZ-03 | Test | `RealRulesTests`, `GhostRulesTests` |
        | ZZZ-04 | Review | — |
        ### Next section
        """;

    // Built at runtime so no made-up rule ID appears as a quoted literal in this file, which the real check scans.
    private static string DriftedTests =>
        $$"""
        public sealed class RealRulesTests { void A() => Check("{{FakeId(1)}}"); void B() => Check("{{FakeId(4)}}"); void C() => Check("{{FakeId(9)}}"); }
        """;

    private static string FakeId(int rule) => string.Concat("ZZZ", "-", rule.ToString("00", CultureInfo.InvariantCulture));
}

internal static partial class RulesDocument
{
    public static IEnumerable<ArchitectureViolation> Check(string document, string testSources)
    {
        int matrixStart = document.IndexOf("### Enforcement matrix", StringComparison.Ordinal);
        if (matrixStart < 0)
        {
            yield return new("DOC-01", "docs/architecture-rules.md", "has no \"### Enforcement matrix\" section.");
            yield break;
        }

        int matrixEnd = document.IndexOf("\n#", matrixStart + 1, StringComparison.Ordinal);
        string definitions = document[..matrixStart] + (matrixEnd < 0 ? string.Empty : document[matrixEnd..]);
        string matrix = matrixEnd < 0 ? document[matrixStart..] : document[matrixStart..matrixEnd];

        HashSet<string> defined = [.. BoldDefinition().Matches(definitions).Select(match => match.Groups["id"].Value)
            .Concat(TableDefinition().Matches(definitions).Select(match => match.Groups["id"].Value))];
        HashSet<string> listed = [.. RuleId().Matches(matrix).Select(match => match.Value)];
        HashSet<string> claimed = [.. MatrixRow().Matches(matrix)
            .Where(row => row.Groups["enforcement"].Value.TrimStart().StartsWith("Test", StringComparison.Ordinal))
            .SelectMany(row => RuleId().Matches(row.Groups["rules"].Value).Select(match => match.Value))];
        HashSet<string> checkedByTests = [.. QuotedRuleId().Matches(testSources).Select(match => match.Groups["id"].Value)];

        foreach (string id in checkedByTests.Except(defined).Order(StringComparer.Ordinal))
        {
            yield return new("DOC-01", id, "is used by an architecture test but not defined in docs/architecture-rules.md.");
        }

        foreach (string id in defined.Except(listed).Order(StringComparer.Ordinal))
        {
            yield return new("DOC-01", id, "is defined but missing from the enforcement matrix.");
        }

        foreach (string id in claimed.Except(checkedByTests).Order(StringComparer.Ordinal))
        {
            yield return new("DOC-01", id, "is marked Test in the enforcement matrix, but no architecture test checks it.");
        }

        foreach (string id in checkedByTests.Intersect(defined).Except(claimed).Order(StringComparer.Ordinal))
        {
            yield return new("DOC-01", id, "is checked by an architecture test, but its matrix row does not start with Test.");
        }

        foreach (string name in TestClassName().Matches(matrix).Select(match => match.Groups["name"].Value).Distinct())
        {
            if (!testSources.Contains($"class {name}", StringComparison.Ordinal))
            {
                yield return new("DOC-01", name, "is named in the enforcement matrix, but no such test class exists.");
            }
        }
    }

    [GeneratedRegex(@"\*\*(?<id>[A-Z]+-\d+):\*\*")]
    private static partial Regex BoldDefinition();

    [GeneratedRegex(@"^\| (?<id>[A-Z]+-\d+) \|", RegexOptions.Multiline)]
    private static partial Regex TableDefinition();

    [GeneratedRegex(@"\b[A-Z]+-\d+\b")]
    private static partial Regex RuleId();

    [GeneratedRegex(@"""(?<id>[A-Z]+-\d+)""")]
    private static partial Regex QuotedRuleId();

    [GeneratedRegex(@"^\|(?<rules>[^|]+)\|(?<enforcement>[^|]+)\|", RegexOptions.Multiline)]
    private static partial Regex MatrixRow();

    [GeneratedRegex(@"`(?<name>\w+Tests)`")]
    private static partial Regex TestClassName();
}
