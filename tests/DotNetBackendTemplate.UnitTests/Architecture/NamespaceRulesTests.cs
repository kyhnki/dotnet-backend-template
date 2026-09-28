using System.Text.RegularExpressions;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>DEP-07: hand-written source folders and namespaces agree. The compiler does not enforce this.</summary>
public sealed class NamespaceRulesTests
{
    [Fact]
    public void ProductionSourceFoldersMatchNamespaces()
    {
        // Program.cs uses top-level statements; migrations are generated code (section 1 excludes both).
        List<(string RelativePath, string Source)> files =
        [
            .. ProductionCode.SourceFiles(path =>
                path != "Program.cs" && !path.StartsWith("Persistence/Migrations/", StringComparison.Ordinal)),
        ];

        files.Should().NotBeEmpty("DEP-07 would pass vacuously if no source files were found");
        ArchitectureAssert.NoViolations(NamespaceRules.Check(files, ProductionCode.RootNamespace));
    }

    [Fact]
    public void NamespaceRuleCatchesMismatchedNamespace() =>
        ArchitectureAssert.Reports(
            NamespaceRules.Check([("Features/Orders/PlaceOrder.cs", "namespace App.Features.Billing;")], "App"),
            "DEP-07",
            "Features/Orders/PlaceOrder.cs");

    [Fact]
    public void NamespaceRuleCatchesMissingNamespace() =>
        ArchitectureAssert.Reports(
            NamespaceRules.Check([("Infrastructure/Helper.cs", "internal static class Helper;")], "App"),
            "DEP-07",
            "Infrastructure/Helper.cs");
}

internal static partial class NamespaceRules
{
    public static IEnumerable<ArchitectureViolation> Check(
        IEnumerable<(string RelativePath, string Source)> files,
        string root)
    {
        foreach ((string path, string source) in files)
        {
            int slash = path.LastIndexOf('/');
            string expected = slash < 0 ? root : $"{root}.{path[..slash].Replace('/', '.')}";
            MatchCollection declarations = NamespaceDeclaration().Matches(source);

            if (declarations.Count == 0)
            {
                yield return new("DEP-07", path, $"declares no namespace; its folder requires '{expected}'.");
            }

            foreach (Match declaration in declarations)
            {
                string declared = declaration.Groups["name"].Value;
                if (declared != expected)
                {
                    yield return new("DEP-07", path,
                        $"declares namespace '{declared}' but its folder requires '{expected}'. A renamed namespace must not hide a class stored in the wrong area.");
                }
            }
        }
    }

    [GeneratedRegex(@"^\s*namespace\s+(?<name>[\w.]+)\s*[;{]", RegexOptions.Multiline)]
    private static partial Regex NamespaceDeclaration();
}
