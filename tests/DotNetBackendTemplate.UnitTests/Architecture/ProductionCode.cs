using System.Reflection;
using DotNetBackendTemplate.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;
using ArchLoader = ArchUnitNET.Loader.ArchLoader;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>
/// Entry points into the production code. Names are derived rather than hard-coded so the rules keep working
/// after the clone-and-rename checklist renames the solution, projects, and namespaces.
/// </summary>
internal static class ProductionCode
{
    private static readonly Lazy<ArchitectureModel> LazyArchitecture =
        new(() => new ArchLoader().LoadAssemblies(Assembly).Build());

    public static Assembly Assembly { get; } = typeof(Program).Assembly;

    /// <summary>The root namespace, which equals the assembly name in this template.</summary>
    public static string RootNamespace { get; } = Assembly.GetName().Name!;

    public static ArchitectureModel Architecture => LazyArchitecture.Value;

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string SourceDirectory { get; } = Path.Combine(RepositoryRoot, "src", RootNamespace);

    /// <summary>
    /// The design-time model keeps relational annotations such as index filters, which the runtime model may drop.
    /// Building it never opens a connection; the connection string only has to parse.
    /// </summary>
    public static IModel DesignTimeModel()
    {
        using AppDbContext context = new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused").Options);

        return context.GetService<IDesignTimeModel>().Model;
    }

    /// <summary>Hand-written source files as repository-relative paths with forward slashes.</summary>
    public static IEnumerable<(string RelativePath, string Source)> SourceFiles(Func<string, bool> include)
    {
        foreach (string path in Directory.EnumerateFiles(SourceDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(SourceDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
            bool isBuildOutput = relative.StartsWith("bin/", StringComparison.Ordinal)
                || relative.StartsWith("obj/", StringComparison.Ordinal);

            if (!isBuildOutput && include(relative))
            {
                yield return (relative, File.ReadAllText(path));
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.EnumerateFiles("*.slnx").Any())
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"No directory containing a .slnx file was found above {AppContext.BaseDirectory}.");
    }
}
