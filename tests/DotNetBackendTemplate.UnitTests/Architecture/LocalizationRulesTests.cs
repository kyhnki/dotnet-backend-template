using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotNetBackendTemplate.Api.Infrastructure.Localization;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>
/// LOC-01, LOC-02 and LOC-03: every client-facing message exists in every supported language, every resource key the
/// code uses exists, and validator messages come from the resources. Keys are read from the resource files, so a new message is checked without editing this test.
/// </summary>
public sealed class LocalizationRulesTests
{
    [Fact]
    public void ProductionResourcesAreCompleteInEveryLanguage()
    {
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> cultures =
            LocalizationRules.ReadResources(ProductionResources.Directory, ProductionResources.BaseName);

        cultures.Should().ContainKey(LocalizationRules.Neutral);
        cultures.Should().HaveCountGreaterThan(1, "LOC-01 would pass vacuously without at least one translation");
        ArchitectureAssert.NoViolations(LocalizationRules.CheckTranslations(cultures));
    }

    [Fact]
    public void EveryResourceKeyUsedInProductionCodeExists()
    {
        IReadOnlyDictionary<string, string> neutral =
            LocalizationRules.ReadResources(ProductionResources.Directory, ProductionResources.BaseName)[LocalizationRules.Neutral];

        ArchitectureAssert.NoViolations(
            LocalizationRules.CheckUsedKeys(ProductionCode.SourceFiles(_ => true), neutral.Keys.ToHashSet(StringComparer.Ordinal)));
    }

    [Fact]
    public void ProductionValidatorMessagesComeFromResources() =>
        ArchitectureAssert.NoViolations(LocalizationRules.CheckValidatorMessages(ProductionCode.SourceFiles(_ => true)));

    [Theory]
    [InlineData("""RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");""")]
    [InlineData("""RuleFor(x => x.Name).NotEmpty().WithMessage($"{nameof(Name)} is required");""")]
    public void ValidatorMessageRuleCatchesStringLiterals(string source) =>
        ArchitectureAssert.Reports(
            LocalizationRules.CheckValidatorMessages([("Features/Orders/PlaceOrder.cs", source)]),
            "LOC-03",
            "Features/Orders/PlaceOrder.cs");

    [Theory]
    [InlineData("tr:Missing")]
    [InlineData("tr:Orphan")]
    [InlineData("tr:Untranslated")]
    [InlineData("tr:Empty")]
    public void TranslationRuleCatchesDeliberateViolations(string subject)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> cultures = new(StringComparer.Ordinal)
        {
            [LocalizationRules.Neutral] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Missing"] = "Only in English",
                ["Untranslated"] = "Same text",
                ["Empty"] = "Has English text",
            },
            ["tr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Orphan"] = "Yalnızca Türkçe",
                ["Untranslated"] = "Same text",
                ["Empty"] = " ",
            },
        };

        ArchitectureAssert.Reports(LocalizationRules.CheckTranslations(cultures), "LOC-01", subject);
    }

    [Fact]
    public void KeyRuleCatchesUnknownKey() =>
        ArchitectureAssert.Reports(
            LocalizationRules.CheckUsedKeys(
                [("Features/Orders/PlaceOrder.cs", """.WithMessage(localizer["OrderNameRequird"])""")],
                new HashSet<string>(StringComparer.Ordinal) { "OrderNameRequired" }),
            "LOC-02",
            "Features/Orders/PlaceOrder.cs");

    private static class ProductionResources
    {
        // Resource files mirror the marker type's namespace under Resources/.
        public static string BaseName { get; } = nameof(ApiResources);

        public static string Directory { get; } = Path.Combine(
            ProductionCode.SourceDirectory,
            "Resources",
            Path.Combine(typeof(ApiResources).Namespace![(ProductionCode.RootNamespace.Length + 1)..].Split('.')));
    }
}

internal static partial class LocalizationRules
{
    public const string Neutral = "neutral";

    /// <summary>Reads Name.resx as the neutral culture and every Name.culture.resx beside it.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ReadResources(string directory, string baseName)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> cultures = new(StringComparer.Ordinal);

        foreach (string path in System.IO.Directory.EnumerateFiles(directory, $"{baseName}*.resx"))
        {
            string culture = Path.GetFileNameWithoutExtension(path)[baseName.Length..].TrimStart('.');
            cultures[culture.Length == 0 ? Neutral : culture] = XDocument.Load(path)
                .Descendants("data")
                .ToDictionary(
                    data => (string)data.Attribute("name")!,
                    data => (string?)data.Element("value") ?? string.Empty,
                    StringComparer.Ordinal);
        }

        return cultures;
    }

    public static IEnumerable<ArchitectureViolation> CheckTranslations(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> cultures)
    {
        IReadOnlyDictionary<string, string> neutral = cultures[Neutral];

        foreach ((string culture, IReadOnlyDictionary<string, string> translation) in cultures.Where(pair => pair.Key != Neutral))
        {
            foreach ((string key, string text) in neutral)
            {
                if (!translation.TryGetValue(key, out string? translated))
                {
                    yield return new("LOC-01", $"{culture}:{key}", "has no translation. Every client-facing message needs an entry in every supported language.");
                }
                else if (string.IsNullOrWhiteSpace(translated))
                {
                    yield return new("LOC-01", $"{culture}:{key}", "has an empty translation.");
                }
                else if (translated == text)
                {
                    yield return new("LOC-01", $"{culture}:{key}", "is identical to the default text and is probably untranslated.");
                }
            }

            foreach (string key in translation.Keys.Where(key => !neutral.ContainsKey(key)))
            {
                yield return new("LOC-01", $"{culture}:{key}", "exists only in this translation; add it to the default resource file or remove it.");
            }
        }

        foreach ((string key, string text) in neutral.Where(pair => string.IsNullOrWhiteSpace(pair.Value)))
        {
            yield return new("LOC-01", $"{Neutral}:{key}", $"has empty default text '{text}'.");
        }
    }

    /// <summary>
    /// IStringLocalizer does not fail on an unknown key: it returns the key itself, so a typo reaches the client
    /// as text such as "CampaignNameRequird". Only literal keys can be checked.
    /// </summary>
    public static IEnumerable<ArchitectureViolation> CheckUsedKeys(
        IEnumerable<(string RelativePath, string Source)> files,
        IReadOnlySet<string> knownKeys)
    {
        foreach ((string path, string source) in files)
        {
            foreach (Match usage in LocalizerKey().Matches(source))
            {
                string key = usage.Groups["key"].Value;
                if (!knownKeys.Contains(key))
                {
                    yield return new("LOC-02", path,
                        $"uses resource key '{key}', which does not exist. The localizer would silently return the key name to the client.");
                }
            }
        }
    }

    /// <summary>LOC-03: a string literal passed to WithMessage bypasses localization and reaches every client in English.</summary>
    public static IEnumerable<ArchitectureViolation> CheckValidatorMessages(IEnumerable<(string RelativePath, string Source)> files)
    {
        foreach ((string path, string source) in files)
        {
            foreach (Match literal in LiteralValidatorMessage().Matches(source))
            {
                yield return new("LOC-03", path,
                    $"passes a string literal to WithMessage ({literal.Value.Trim()}…). Use localizer[\"Key\"] so the message follows Accept-Language.");
            }
        }
    }

    [GeneratedRegex(@"\.WithMessage\(\s*[@$]*""")]
    private static partial Regex LiteralValidatorMessage();

    [GeneratedRegex("""[Ll]ocalizer\s*\[\s*"(?<key>[^"]+)"\s*[,\]]""")]
    private static partial Regex LocalizerKey();
}
