using System.Xml.Linq;
using Ake.Core;

namespace Ake.Architecture.Tests;

/// <summary>ADR-0001: Ake.Core arayüze, dosya sistemine, FreeCAD'e ve diğer projelere bağımlı olamaz.</summary>
public class CoreDependencyTests
{
    [Fact]
    public void Core_project_file_declares_no_project_or_package_references()
    {
        var projectFile = Path.Combine(FindRepositoryRoot(), "src", "Ake.Core", "Ake.Core.csproj");
        var project = XDocument.Load(projectFile);

        var references = project.Descendants()
            .Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference" or "Reference")
            .Select(e => $"{e.Name.LocalName}: {e.Attribute("Include")?.Value}")
            .ToList();

        Assert.Empty(references);
    }

    [Fact]
    public void Core_assembly_references_only_the_base_class_library()
    {
        var foreignReferences = typeof(Result).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !IsBaseClassLibrary(name))
            .ToList();

        Assert.Empty(foreignReferences);
    }

    private static bool IsBaseClassLibrary(string assemblyName) =>
        assemblyName is "netstandard" or "mscorlib" or "System"
        || assemblyName.StartsWith("System.", StringComparison.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Depo kökü bulunamadı (Directory.Build.props yok).");
    }
}
