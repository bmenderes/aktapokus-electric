using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ake.Ai;

/// <summary>Patch'in kullanıcıya gösterilecek okunur özeti: komut başına bir satır (onay ekranı için).</summary>
public static class PatchSummary
{
    public static IReadOnlyList<string> Describe(string patchJson)
    {
        ArgumentNullException.ThrowIfNull(patchJson);

        JsonArray? commands;
        try
        {
            commands = (JsonNode.Parse(patchJson) as JsonObject)?["commands"] as JsonArray;
        }
        catch (JsonException)
        {
            return [];
        }

        return commands is null ? [] : [.. commands.OfType<JsonObject>().Select(Line)];
    }

    private static string Line(JsonObject command)
    {
        string Text(string name) => command[name]?.ToString() ?? "?";
        var detail = command["description"]?.ToString();

        return command["op"]?.ToString() switch
        {
            "addDevice" => $"Cihaz ekle: {Text("tag")}" + (detail is null ? string.Empty : $" ({detail})"),
            "removeDevice" => $"Cihazı sil: {Text("device")}",
            "connect" => $"Bağla: {Text("from")} → {Text("to")}" + WireText(command),
            "disconnect" => $"Bağlantıyı kaldır: {Text("from")} → {Text("to")}",
            "assignWire" => $"Tel/damar ata: {Text("from")} → {Text("to")}" + WireText(command),
            "retag" => $"Etiketi değiştir: {Text("device")} → {Text("tag")}",
            "assignIo" => $"G/Ç ata: {Text("channel")}",
            "setProperty" => $"Özellik: {Text("key")} = {command["value"]?.ToString() ?? "(sil)"}",
            "addPage" => $"Sayfa ekle: {Text("number")} {command["title"]}",
            "assignSymbol" => $"Sembol ata: {Text("function")}",
            var other => $"{other}",
        };
    }

    private static string WireText(JsonObject command) =>
        command["wire"]?["number"]?.ToString() is { } number ? $" (tel {number})" : string.Empty;
}
