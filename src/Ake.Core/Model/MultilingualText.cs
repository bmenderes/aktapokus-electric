namespace Ake.Core.Model;

/// <summary>
/// Çok dilli metinleri (<c>tr=Ana güç;en=Main power;de=Hauptstrom</c>) projenin etkin dil ayarına
/// (<c>project.Properties["language"]</c>) göre çözer (ADR-0058). Biçim çok dilli değilse metni
/// olduğu gibi döndürür.
/// </summary>
public static class MultilingualText
{
    public static string Resolve(string? text, string? language)
    {
        if (string.IsNullOrEmpty(text) || !TryParseEntries(text, out var entries))
        {
            return text ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(language))
        {
            return entries[0].Value;
        }

        var wanted = language.Split(['/', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var resolved = new List<string>(wanted.Length);
        foreach (var lang in wanted)
        {
            var match = entries.FirstOrDefault(entry => string.Equals(entry.Lang, lang, StringComparison.OrdinalIgnoreCase));
            var dash = lang.IndexOf('-');
            if (string.IsNullOrEmpty(match.Value) && dash > 0)
            {
                var baseLang = lang[..dash];
                match = entries.FirstOrDefault(entry => string.Equals(entry.Lang, baseLang, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(match.Value) && !resolved.Contains(match.Value, StringComparer.Ordinal))
            {
                resolved.Add(match.Value);
            }
        }

        return resolved.Count > 0 ? string.Join(" / ", resolved) : entries[0].Value;
    }

    private static bool TryParseEntries(string text, out List<(string Lang, string Value)> entries)
    {
        entries = [];
        var parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        foreach (var part in parts)
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                return false;
            }

            var lang = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (!IsLanguageCode(lang) || value.Length == 0)
            {
                return false;
            }

            entries.Add((lang, value));
        }

        return entries.Count >= 2;
    }

    private static bool IsLanguageCode(string code) =>
        code.Length is >= 2 and <= 5 && code.All(ch => (ch is >= 'a' and <= 'z') || (ch is >= 'A' and <= 'Z') || ch == '-');
}
