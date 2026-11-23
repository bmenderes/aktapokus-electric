using System.Text;
using System.Text.Json;
using Ake.Core;
using Ake.Core.Model;

namespace Ake.Serialization;

/// <summary>
/// Sembol tanımının tek başına dosyası (<c>*.sembol.json</c>). Biçimi, proje dosyasındaki
/// <c>symbols</c> öğesiyle aynıdır; böylece bir sembol projeler arasında dosya olarak taşınır.
/// </summary>
public static class SymbolJson
{
    private static readonly AkeJsonContext Context = new(JsonDefaults.CreateOptions());

    public static string Serialize(SymbolDefinition symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return JsonSerializer.Serialize(ProjectMapper.ToDto(symbol), Context.SymbolDto) + "\n";
    }

    public static Result<SymbolDefinition> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        SymbolDefinition symbol;
        try
        {
            var dto = JsonSerializer.Deserialize(json, Context.SymbolDto);
            if (dto is null)
            {
                return Invalid("dosya boş.");
            }

            symbol = ProjectMapper.ToModel(dto);
        }
        catch (JsonException ex)
        {
            return Invalid(ex.Message);
        }

        return symbol.Check() is { } error ? Result.Failure<SymbolDefinition>(error) : Result.Success(symbol);
    }

    public static Result<SymbolDefinition> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return Deserialize(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<SymbolDefinition>(
                new Error("sembol.dosya_okunamadi", $"'{path}' okunamadı: {ex.Message}"));
        }
    }

    public static Result Save(SymbolDefinition symbol, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            File.WriteAllText(path, Serialize(symbol), new UTF8Encoding(false));
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new Error("sembol.dosya_yazilamadi", $"'{path}' yazılamadı: {ex.Message}"));
        }
    }

    private static Result<SymbolDefinition> Invalid(string detail) =>
        Result.Failure<SymbolDefinition>(new Error("sembol.json_gecersiz", $"Sembol dosyası okunamadı: {detail}"));
}
