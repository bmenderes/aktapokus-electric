using System.CommandLine;
using System.Globalization;
using Ake.Core;
using Ake.Core.Macros;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Cli;

/// <summary><c>ake macro</c>: projeden makro oluşturma ve makroyu projeye yerleştirme (ADR-0025).</summary>
internal static partial class CliApp
{
    private static Command MacroCommand(TextWriter output, TextWriter error)
    {
        var command = new Command("macro", "Makro oluşturur ve projeye yerleştirir");
        command.Subcommands.Add(MacroCreateCommand(output, error));
        command.Subcommands.Add(MacroInsertCommand(output, error));
        return command;
    }

    private static Command MacroCreateCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var page = new Argument<string>("sayfa") { Description = "Makronun alınacağı sayfanın numarası" };
        var outPath = new Option<string>("--out") { Description = "Makro dosyası (.makro.json)", Required = true };
        var area = new Option<string?>("--alan") { Description = "Yalnızca bu alandaki semboller: x,y,genişlik,yükseklik (mm)" };
        var name = new Option<string?>("--ad") { Description = "Makronun adı (varsayılan: sayfa başlığı)" };

        var command = new Command("create", "Bir sayfadan ya da sayfadaki bir alandan makro oluşturur");
        command.Arguments.Add(project);
        command.Arguments.Add(page);
        command.Options.Add(outPath);
        command.Options.Add(area);
        command.Options.Add(name);
        command.SetAction(parse =>
        {
            var loaded = ProjectFile.Load(parse.GetValue(project)!);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            if (FindPage(loaded.Value, parse.GetValue(page)!) is not { } source)
            {
                return Fail(error, new Error("sayfa.yok", $"Sayfa bulunamadı: {parse.GetValue(page)}"));
            }

            (double, double, double, double)? box = null;
            if (parse.GetValue(area) is { } text)
            {
                var parts = text.Split(',').Select(part => double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray();
                if (parts.Length != 4 || parts.Any(double.IsNaN) || parts[2] <= 0 || parts[3] <= 0)
                {
                    return Fail(error, new Error("makro.alan_gecersiz", $"Alan x,y,genişlik,yükseklik biçiminde olmalı: '{text}'"));
                }

                box = (parts[0], parts[1], parts[2], parts[3]);
            }

            var macro = MacroService.Capture(loaded.Value, source.Id, parse.GetValue(name) ?? source.Title, box);
            if (macro.IsFailure)
            {
                return Fail(error, macro.Error!);
            }

            var path = parse.GetValue(outPath)!;
            var saved = MacroJson.Save(macro.Value, path);
            if (saved.IsFailure)
            {
                return Fail(error, saved.Error!);
            }

            output.WriteLine(
                $"Yazıldı: {path} ({macro.Value.Devices.Count} cihaz, {macro.Value.Connections.Count} bağlantı, {macro.Value.Placements.Count} sembol)");
            return ExitOk;
        });
        return command;
    }

    private static Command MacroInsertCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var macroPath = new Argument<string>("makro") { Description = "Makro dosyası (.makro.json)" };
        var page = new Argument<string>("sayfa") { Description = "Yerleştirilecek sayfanın numarası" };
        var x = new Option<double?>("--x") { Description = "Makronun sol üst köşesi, mm (varsayılan: sayfadaki sembollerin altı)" };
        var y = new Option<double?>("--y") { Description = "Makronun sol üst köşesi, mm" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Değişiklikleri göster, dosya yazma" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };

        var command = new Command("insert", "Makroyu sayfaya yeni tag'lerle yerleştirir");
        command.Arguments.Add(project);
        command.Arguments.Add(macroPath);
        command.Arguments.Add(page);
        command.Options.Add(x);
        command.Options.Add(y);
        command.Options.Add(dryRun);
        command.Options.Add(actor);
        command.SetAction(parse =>
        {
            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var macro = MacroJson.Load(parse.GetValue(macroPath)!);
            if (macro.IsFailure)
            {
                return Fail(error, macro.Error!);
            }

            if (FindPage(loaded.Value, parse.GetValue(page)!) is not { } target)
            {
                return Fail(error, new Error("sayfa.yok", $"Sayfa bulunamadı: {parse.GetValue(page)}"));
            }

            var spot = MacroService.DefaultSpot(loaded.Value, target.Id);
            var insert = MacroService.Insert(
                loaded.Value, macro.Value, target.Id, parse.GetValue(x) ?? spot.X, parse.GetValue(y) ?? spot.Y);
            return insert.IsFailure
                ? Fail(error, insert.Error!)
                : Commit(loaded.Value, projectPath, insert.Value, parse.GetValue(dryRun), parse.GetValue(actor), output, error);
        });
        return command;
    }

    private static Page? FindPage(Project project, string number) =>
        project.Pages.FirstOrDefault(page => string.Equals(page.Number, number.Trim(), StringComparison.Ordinal));
}
