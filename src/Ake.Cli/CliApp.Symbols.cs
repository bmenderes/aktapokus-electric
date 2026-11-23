using System.CommandLine;
using System.Globalization;
using Ake.Core;
using Ake.Library;

namespace Ake.Cli;

/// <summary>Sembol içe aktarma komutlarının ortak parçaları: QElectroTech ve DXF/DWG (ADR-0016, ADR-0027).</summary>
internal static partial class CliApp
{
    private const string SymbolSourceDescription =
        "QElectroTech elemanı (.elmt), DXF/DWG çizimi ya da bunları içeren klasör";

    private static Option<double?> ScaleOption() => new("--scale")
    {
        Description = "Ölçek. QElectroTech: piksel başına mm (varsayılan 0,5, ızgara 5 mm olur). DXF/DWG: çizim mm'ye çevrildikten sonraki çarpan (varsayılan 1)",
    };

    private static Option<bool> BlocksOption() => new("--blocks")
    {
        Description = "DXF/DWG: model alanı yerine adlandırılmış her bloğu ayrı sembol olarak al",
    };

    private static Option<string?> AreaOption() => new("--alan")
    {
        Description = "DXF/DWG: yalnızca bu dikdörtgenin içindeki öğeler (x,y,genişlik,yükseklik; dosyanın çizim biriminde). Bir görünümü seçmek için; sınırlar 'symbol inspect' ile görülür",
    };

    /// <summary>"x,y,genişlik,yükseklik" metnini okur; boşsa <c>null</c>, bozuksa hata.</summary>
    private static Result<(double X, double Y, double Width, double Height)?> ParseArea(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Success<(double, double, double, double)?>(null);
        }

        var parts = text.Split(',').Select(part => double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN).ToArray();
        return parts.Length != 4 || parts.Any(double.IsNaN) || parts[2] <= 0 || parts[3] <= 0
            ? Result.Failure<(double, double, double, double)?>(new Error("sembol.alan_gecersiz", $"Alan x,y,genişlik,yükseklik biçiminde olmalı (genişlik ve yükseklik pozitif): '{text}'"))
            : Result.Success<(double, double, double, double)?>((parts[0], parts[1], parts[2], parts[3]));
    }

    private static Command SymbolInspectCommand(TextWriter output, TextWriter error)
    {
        var file = new Argument<string>("dosya") { Description = "DXF ya da DWG dosyası" };
        var command = new Command("inspect", "DXF/DWG dosyasının içine bakar (içe aktarmadan): sürüm, birim, sınırlar, öğeler, katmanlar, bloklar");
        command.Arguments.Add(file);
        command.SetAction(parse =>
        {
            var info = DxfImport.Inspect(parse.GetValue(file)!);
            if (info.IsFailure)
            {
                return Fail(error, info.Error!);
            }

            var value = info.Value;
            string Format(double number) => number.ToString("0.###", CultureInfo.InvariantCulture);
            output.WriteLine($"{value.File}  ·  {value.Version}  ·  birim: {value.Units}");
            output.WriteLine(value.Extents is { } e
                ? $"Sınırlar (çizim biriminde): x={Format(e.X)} y={Format(e.Y)} genişlik={Format(e.Width)} yükseklik={Format(e.Height)}   →  --alan \"{Format(e.X)},{Format(e.Y)},{Format(e.Width)},{Format(e.Height)}\""
                : "Alınabilecek geometri yok.");
            output.WriteLine($"Uç noktaları (POINT): {value.Points}");
            output.WriteLine("Öğeler: " + (value.Entities.Count == 0 ? "yok" : string.Join(", ", value.Entities.Select(item => $"{item.Type} {item.Count}"))));
            if (value.Ignored.Count > 0)
            {
                output.WriteLine("ALINMAYAN: " + string.Join(", ", value.Ignored.Select(item => $"{item.Type} {item.Count}")));
            }

            output.WriteLine("Katmanlar: " + (value.Layers.Count == 0 ? "yok" : string.Join(", ", value.Layers.Select(layer => $"{layer.Name} ({layer.Entities}){(layer.Visible ? string.Empty : " gizli")}"))));
            output.WriteLine("Bloklar: " + (value.Blocks.Count == 0 ? "yok" : string.Join(", ", value.Blocks.Select(block => $"{block.Name} ({block.Entities})"))));
            return ExitOk;
        });
        return command;
    }

    /// <summary>Yoldaki QElectroTech elemanlarını ve DXF/DWG çizimlerini birlikte okur.</summary>
    private static List<ImportedSymbol> ImportSymbolFiles(
        string source, string? license, double? scale, bool blocks, (double X, double Y, double Width, double Height)? area = null)
    {
        var folder = Directory.Exists(source);
        var cad = folder
            ? Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Any(DxfImport.IsCadFile)
            : DxfImport.IsCadFile(source);

        var results = new List<ImportedSymbol>();
        if (scale is not null && !(scale > 0 && double.IsFinite(scale.Value)))
        {
            results.Add(new ImportedSymbol(source, null, new Ake.Core.Error("sembol.olcek", $"Ölçek sıfırdan büyük olmalı: {scale}")));
            return results;
        }

        if (folder || !cad)
        {
            results.AddRange(QElectroTechImport.FromPath(source, license, scale ?? QElectroTechImport.DefaultScaleMmPerPixel));
        }

        if (cad)
        {
            results.AddRange(DxfImport.FromPath(source, license, scale ?? 1, blocks, area));
        }

        return results;
    }
}
