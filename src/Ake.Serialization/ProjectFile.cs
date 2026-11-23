using System.Text;
using Ake.Core;
using Ake.Core.Model;

namespace Ake.Serialization;

/// <summary>Proje dosyasını diskten okur ve diske yazar (UTF-8, BOM'suz).</summary>
public static class ProjectFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static Result<Project> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string json;
        try
        {
            json = File.ReadAllText(path, Utf8NoBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<Project>(new Error("proje.dosya_okunamadi", $"'{path}' okunamadı: {ex.Message}"));
        }

        return ProjectJson.Deserialize(json);
    }

    public static Result Save(Project project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            File.WriteAllText(path, ProjectJson.Serialize(project), Utf8NoBom);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new Error("proje.dosya_yazilamadi", $"'{path}' yazılamadı: {ex.Message}"));
        }
    }
}
