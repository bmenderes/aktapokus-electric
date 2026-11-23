using System.Text.Json;
using System.Text.Json.Nodes;
using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Ai.Tests;

/// <summary>Değerlendirme vakası (examples/ai/degerlendirme.json): istem, beklenen sonuç ve referans çözüm.</summary>
public sealed record EvaluationCase(string Id, string Request, JsonObject Expect, string SolutionJson)
{
    public override string ToString() => Id;

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    public static Project RefMachine() =>
        ProjectFile.Load(Path.Combine(RepositoryRoot(), "examples", "ref-machine", "ref-machine.json")).Value;

    public static IReadOnlyList<EvaluationCase> Load() =>
        [.. JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "examples", "ai", "degerlendirme.json")))!["cases"]!.AsArray()
            .Select(node => new EvaluationCase(
                node!["id"]!.GetValue<string>(),
                node["request"]!.GetValue<string>(),
                node["expect"]!.AsObject(),
                node["solution"]!.ToJsonString()))];

    public static TheoryData<string> Ids()
    {
        var data = new TheoryData<string>();
        foreach (var item in Load())
        {
            data.Add(item.Id);
        }

        return data;
    }

    /// <summary>Patch'i <paramref name="project"/> üzerinde uygular (yalnızca kopya verilmelidir).</summary>
    public static Ake.Core.Result Apply(Project project, string patchJson)
    {
        var json = JsonNode.Parse(patchJson)!.AsObject();
        json["patchVersion"] = PatchJson.CurrentPatchVersion;
        var patch = PatchJson.Deserialize(json.ToJsonString());
        return patch.IsFailure
            ? Ake.Core.Result.Failure(patch.Error!)
            : new CommandStack(project).Execute(patch.Value.ToCommand(), ChangeSource.Ai, "ai");
    }

    /// <summary>Beklentiler sağlanmıyorsa nedenlerin listesi; boşsa vaka geçti.</summary>
    public IReadOnlyList<string> Check(Project before, Project after)
    {
        var problems = new List<string>();
        var tags = after.Devices.Select(device => after.DesignationOf(device).ToString()).ToHashSet();

        void Delta(string name, int actual, int expected, bool atLeast)
        {
            if (atLeast ? actual < expected : actual != expected)
            {
                problems.Add($"{name}: beklenen {(atLeast ? "en az " : string.Empty)}{expected:+0;-0;0}, olan {actual:+0;-0;0}");
            }
        }

        if (Expect["devices"] is { } devices)
        {
            Delta("cihaz", after.Devices.Count - before.Devices.Count, devices.GetValue<int>(), atLeast: false);
        }

        if (Expect["connections"] is { } connections)
        {
            Delta("bağlantı", after.Connections.Count - before.Connections.Count, connections.GetValue<int>(), atLeast: false);
        }

        if (Expect["connectionsMin"] is { } minimum)
        {
            Delta("bağlantı", after.Connections.Count - before.Connections.Count, minimum.GetValue<int>(), atLeast: true);
        }

        if (Expect["pages"] is { } pages)
        {
            Delta("sayfa", after.Pages.Count - before.Pages.Count, pages.GetValue<int>(), atLeast: false);
        }

        problems.AddRange((Expect["has"]?.AsArray() ?? []).Select(tag => tag!.GetValue<string>()).Where(tag => !tags.Contains(tag)).Select(tag => $"cihaz yok: {tag}"));
        problems.AddRange((Expect["lacks"]?.AsArray() ?? []).Select(tag => tag!.GetValue<string>()).Where(tags.Contains).Select(tag => $"cihaz olmamalıydı: {tag}"));

        if (Expect["wire"] is JsonObject wire)
        {
            var (from, to, number) = (wire["from"]!.GetValue<string>(), wire["to"]!.GetValue<string>(), wire["number"]!.GetValue<string>());
            var found = after.Connections.FirstOrDefault(connection =>
                (after.DesignationOf(connection.From)?.ToString() == from && after.DesignationOf(connection.To)?.ToString() == to)
                || (after.DesignationOf(connection.From)?.ToString() == to && after.DesignationOf(connection.To)?.ToString() == from));
            if (found?.Wire?.Number != number)
            {
                problems.Add($"{from} → {to} tel numarası {number} olmalıydı, olan {found?.Wire?.Number ?? "(yok)"}");
            }
        }

        var newErrors = ErcEngine.Default.Run(after).Findings.Where(finding => finding.Severity == Severity.Error)
            .Select(finding => $"{finding.RuleId}|{finding.Message}")
            .Except(ErcEngine.Default.Run(before).Findings.Where(finding => finding.Severity == Severity.Error).Select(finding => $"{finding.RuleId}|{finding.Message}"))
            .ToList();
        problems.AddRange(newErrors.Select(error => $"yeni ERC hatası: {error}"));
        return problems;
    }
}
