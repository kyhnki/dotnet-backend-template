namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations;

/// <summary>
/// Anchor for a miniature application whose folders mirror the real API and which breaks every enforced
/// architecture rule on purpose. Each rule's negative test runs against it: a rule that stops reporting its
/// deliberate violation has silently stopped working, and its production check would keep passing.
/// </summary>
internal static class FixtureRoot
{
    public static string Namespace { get; } = typeof(FixtureRoot).Namespace!;
}
