using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>
/// OPS-02: packages whose later major versions require a commercial license stay below that version. An automated
/// dependency bump would otherwise merge a licensing change nobody reviewed.
/// </summary>
public sealed class PackageRulesTests
{
    [Fact]
    public void PackagesStayBelowCommercialLicenseVersions()
    {
        List<(string Path, XDocument Project)> projects = Directory
            .EnumerateFiles(ProductionCode.RepositoryRoot, "Directory.Packages.props")
            .Concat(Directory.EnumerateFiles(ProductionCode.RepositoryRoot, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Path.GetRelativePath(ProductionCode.RepositoryRoot, path), XDocument.Load(path)))
            .ToList();

        projects.Should().Contain(project => project.Path == "Directory.Packages.props", "central package versions live there");
        ArchitectureAssert.NoViolations(PackageRules.Check(projects));
    }

    [Theory]
    [InlineData("""<PackageVersion Include="MediatR" Version="13.0.0" />""", "MediatR")]
    [InlineData("""<PackageVersion Include="FluentAssertions" Version="8.1.0" />""", "FluentAssertions")]
    [InlineData("""<PackageVersion Include="AutoMapper" Version="[15.0.0]" />""", "AutoMapper")]
    [InlineData("""<PackageReference Include="MediatR" VersionOverride="13.1.0" />""", "MediatR")]
    public void PackageRuleCatchesCommercialVersions(string item, string package) =>
        ArchitectureAssert.Reports(
            PackageRules.Check([("Fixture.props", XDocument.Parse($"<Project><ItemGroup>{item}</ItemGroup></Project>"))]),
            "OPS-02",
            package);

    [Fact]
    public void PackageRuleAcceptsTheLastPermissiveVersions() =>
        ArchitectureAssert.DoesNotReport(
            PackageRules.Check([("Fixture.props", XDocument.Parse(
                """<Project><ItemGroup><PackageVersion Include="MediatR" Version="12.5.0" /><PackageVersion Include="FluentAssertions" Version="7.2.2" /></ItemGroup></Project>"""))]),
            "OPS-02",
            "Fixture.props");
}

internal static partial class PackageRules
{
    // Package, first commercially licensed major version, and the last permissive release to stay on.
    private static readonly (string Package, int CommercialMajor, string LastPermissive)[] Limits =
    [
        ("MediatR", 13, "12.5.0"),
        ("FluentAssertions", 8, "7.2.2"),
        ("AutoMapper", 15, "14.x"),
    ];

    public static IEnumerable<ArchitectureViolation> Check(IEnumerable<(string Path, XDocument Project)> projects)
    {
        foreach ((string path, XDocument project) in projects)
        {
            foreach (XElement item in project.Descendants().Where(element => element.Name.LocalName is "PackageVersion" or "PackageReference"))
            {
                string? package = (string?)item.Attribute("Include");
                string? version = (string?)item.Attribute("Version") ?? (string?)item.Attribute("VersionOverride");
                (string Package, int CommercialMajor, string LastPermissive) limit =
                    Limits.FirstOrDefault(candidate => string.Equals(candidate.Package, package, StringComparison.OrdinalIgnoreCase));

                if (limit.Package is null || version is null || MajorVersion().Match(version) is not { Success: true } match)
                {
                    continue;
                }

                if (int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture) >= limit.CommercialMajor)
                {
                    yield return new("OPS-02", $"{path}: {package} {version}",
                        $"is at or above version {limit.CommercialMajor}, which requires a commercial license. Stay on {limit.LastPermissive}, or review the license, record the decision in an ADR, and update this limit.");
                }
            }
        }
    }

    [GeneratedRegex(@"^\[?\s*(?<major>\d+)")]
    private static partial Regex MajorVersion();
}
