using System.Text.Json;
using System.Text.Json.Nodes;
using Ake.Core;
using Avalonia.Input;

namespace Ake.Editor;

/// <summary>
/// Kısayol profili: her komutun kısayolu. Varsayılan set <see cref="EditorCommands"/>'tadır; kullanıcı bunun üstüne yalnızca
/// değiştirdiklerini yazar (<c>kisayollar.json</c>, biçim <c>docs/kisayollar.md</c>'de). Bir kısayol en çok bir komuta
/// bağlıdır; çakışan atama reddedilir. Kısayollar Avalonia biçimindedir ("Ctrl+Shift+S", "Delete", "F11") ve kayıtta
/// tek biçime (<see cref="Normalize"/>) getirilir.
/// </summary>
public sealed class KeyMap
{
    /// <summary>Dosya biçiminin sürümü.</summary>
    public const int Version = 1;

    private readonly Dictionary<string, string?> _gestures;

    private KeyMap(Dictionary<string, string?> gestures) => _gestures = gestures;

    /// <summary>Varsayılan set: komut tanımlarındaki kısayollar.</summary>
    public static KeyMap Default() => new(EditorCommands.All.ToDictionary(
        command => command.Id,
        command => command.Gesture is null ? null : Normalize(command.Gesture).Value));

    /// <summary>Kopya; pencere bunun üzerinde düzenler, Tamam'a basılana kadar asıl profil değişmez.</summary>
    public KeyMap Clone() => new(new Dictionary<string, string?>(_gestures));

    /// <summary>Komutun kısayolu; yoksa <c>null</c>.</summary>
    public string? GestureOf(string commandId) => _gestures.GetValueOrDefault(commandId);

    /// <summary>Kısayolu olan komutlar ve kısayolları (pencerenin tuş yakalaması için).</summary>
    public IReadOnlyList<(string Command, KeyGesture Gesture)> Bindings =>
    [
        .. _gestures.Where(entry => entry.Value is not null).Select(entry => (entry.Key, KeyGesture.Parse(entry.Value!))),
    ];

    /// <summary>Komutun kısayolu varsayılandan farklı mı.</summary>
    public bool IsCustom(string commandId) => GestureOf(commandId) != Default().GestureOf(commandId);

    /// <summary>
    /// Komuta kısayol verir; <c>null</c> kısayolu kaldırır. Geçersiz kısayol ya da başka bir komutta zaten bulunan kısayol
    /// reddedilir ve profil değişmez.
    /// </summary>
    public Result Set(string commandId, string? gesture)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        if (!_gestures.ContainsKey(commandId))
        {
            return Result.Failure(new Error("kisayol.komut_yok", $"Tanımsız komut: {commandId}"));
        }

        if (gesture is null)
        {
            _gestures[commandId] = null;
            return Result.Success();
        }

        var normalized = Normalize(gesture);
        if (normalized.IsFailure)
        {
            return Result.Failure(normalized.Error!);
        }

        if (_gestures.FirstOrDefault(entry => entry.Key != commandId && entry.Value == normalized.Value) is { Value: not null } taken)
        {
            return Result.Failure(new Error(
                "kisayol.cakisma", $"{normalized.Value} zaten '{EditorCommands.Get(taken.Key).Label}' komutunda."));
        }

        _gestures[commandId] = normalized.Value;
        return Result.Success();
    }

    /// <summary>
    /// Komutu varsayılan kısayoluna döndürür. Varsayılanı başka bir komut almışsa o komutun kısayolu kaldırılır; böylece
    /// sıfırlama her zaman çalışır ve çakışma bırakmaz.
    /// </summary>
    public void Reset(string commandId)
    {
        var fallback = Default().GestureOf(commandId);
        foreach (var other in _gestures.Where(entry => entry.Key != commandId && entry.Value is not null && entry.Value == fallback).Select(entry => entry.Key).ToList())
        {
            _gestures[other] = null;
        }

        _gestures[commandId] = fallback;
    }

    /// <summary>Bütün komutları varsayılana döndürür.</summary>
    public void ResetAll()
    {
        foreach (var entry in Default()._gestures)
        {
            _gestures[entry.Key] = entry.Value;
        }
    }

    /// <summary>Kısayolu tek biçime getirir ("ctrl+s" → "Ctrl+S"); okunamıyorsa hata.</summary>
    public static Result<string> Normalize(string gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        try
        {
            // Avalonia Enter tuşunu "Return" yazar; kullanıcıya ve dosyaya "Enter" gösterilir (ikisi de okunur).
            var text = KeyGesture.Parse(gesture.Trim()).ToString();
            return Result.Success(text == "Return" || text.EndsWith("+Return", StringComparison.Ordinal) ? text[..^"Return".Length] + "Enter" : text);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            return Result.Failure<string>(new Error("kisayol.gecersiz", $"Kısayol okunamadı: '{gesture}'"));
        }
    }

    /// <summary>
    /// Profili JSON'a çevirir. Varsayılan olarak yalnızca varsayılandan farklı olanlar yazılır (kaldırılan kısayol
    /// <c>null</c>); <paramref name="full"/> doğruysa bütün komutlar yazılır (belge için).
    /// </summary>
    public string ToJson(bool full = false)
    {
        var defaults = Default()._gestures;
        var entries = new JsonObject();
        foreach (var (id, gesture) in _gestures.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (full || defaults[id] != gesture)
            {
                entries[id] = gesture;
            }
        }

        var root = new JsonObject { ["surum"] = Version, ["kisayollar"] = entries };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }

    /// <summary>
    /// JSON'dan profil kurar: yazılmayan komutlar varsayılanda kalır. Bilinmeyen komut, geçersiz kısayol, çakışma ya da
    /// bozuk dosya hata verir (profil kısmen uygulanmaz).
    /// </summary>
    public static Result<KeyMap> FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return Result.Failure<KeyMap>(new Error("kisayol.json_gecersiz", $"Kısayol dosyası geçerli JSON değil: {ex.Message}"));
        }

        if (root is not JsonObject obj || obj["kisayollar"] is not JsonObject entries)
        {
            return Result.Failure<KeyMap>(new Error("kisayol.json_gecersiz", "Kısayol dosyasında 'kisayollar' nesnesi yok."));
        }

        if (obj["surum"]?.GetValue<int>() is not Version)
        {
            return Result.Failure<KeyMap>(new Error("kisayol.surum", $"Kısayol dosyasının 'surum' alanı {Version} olmalı."));
        }

        // Önce hepsi boşaltılıp sonra atanır: dosyadaki iki komutun kısayol takası ("A↔B") varsayılanla çakışmaz.
        var map = Default();
        foreach (var (id, _) in entries)
        {
            if (!map._gestures.ContainsKey(id))
            {
                return Result.Failure<KeyMap>(new Error("kisayol.komut_yok", $"Kısayol dosyasında tanımsız komut: {id}"));
            }

            map._gestures[id] = null;
        }

        foreach (var (id, value) in entries)
        {
            string? gesture;
            try
            {
                gesture = value?.GetValue<string>();
            }
            catch (InvalidOperationException)
            {
                return Result.Failure<KeyMap>(new Error("kisayol.gecersiz", $"'{id}' kısayolu metin ya da null olmalı."));
            }

            if (gesture is null)
            {
                continue;
            }

            var set = map.Set(id, gesture);
            if (set.IsFailure)
            {
                return Result.Failure<KeyMap>(set.Error!);
            }
        }

        return Result.Success(map);
    }

    /// <summary>Dosyadan okur; dosya yoksa varsayılan profil.</summary>
    public static Result<KeyMap> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            return Result.Success(Default());
        }

        try
        {
            return FromJson(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<KeyMap>(new Error("kisayol.okunamadi", $"Kısayol dosyası okunamadı: {ex.Message}"));
        }
    }

    /// <summary>Profili dosyaya yazar (yalnızca değişenler).</summary>
    public Result Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, ToJson());
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new Error("kisayol.yazilamadi", $"Kısayol dosyası yazılamadı: {ex.Message}"));
        }
    }
}
