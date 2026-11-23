using System.CommandLine;
using System.Text.Json;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Core.Numbering;
using Ake.Layout;
using Ake.Library;
using Ake.Render;
using Ake.Reports;
using Ake.Serialization;

namespace Ake.Cli;

/// <summary>
/// <c>ake</c> komutlarının tamamı. Çıktı verilen yazıcılara gider ve log kurulumu burada değildir;
/// böylece testler kullanıcının dosyalarına dokunmadan çalıştırabilir.
/// </summary>
internal static partial class CliApp
{
    public const int ExitOk = 0;
    public const int ExitFailure = 1;

    /// <param name="defaultLibraryPath">
    /// --library verilmediğinde kullanılan kütüphane dosyası. Testler vermez; böylece kullanıcının
    /// gerçek kütüphanesine dokunulmaz.
    /// </param>
    public static int Run(string[] args, TextWriter output, TextWriter error, string? defaultLibraryPath = null)
    {
        var root = new RootCommand("aktapokus-electric komut satırı aracı");
        root.Subcommands.Add(ValidateCommand(output, error));
        root.Subcommands.Add(ApplyCommand(output, error, defaultLibraryPath));
        root.Subcommands.Add(AiCommand(output, error, defaultLibraryPath));
        root.Subcommands.Add(RenumberCommand(output, error));
        root.Subcommands.Add(TagSchemeCommand(output, error));
        root.Subcommands.Add(MacroCommand(output, error));
        root.Subcommands.Add(ImportCommand(output, error));
        root.Subcommands.Add(ReportCommand(output, error));
        root.Subcommands.Add(LibCommand(output, error, defaultLibraryPath));
        root.Subcommands.Add(TemplateCommand(output, error));
        root.Subcommands.Add(SymbolCommand(output, error));
        root.Subcommands.Add(LayoutCommand(output, error));
        root.Subcommands.Add(RenderCommand(output, error));
        root.Subcommands.Add(CommunicationCommand(output, error));
        root.Subcommands.Add(SingleLineCommand(output, error));

        return root.Parse(args).Invoke(new InvocationConfiguration { Output = output, Error = error });
    }

    private static Command ValidateCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var json = new Option<bool>("--json") { Description = "Bulguları JSON olarak yaz" };

        var command = new Command("validate", "Projeyi yükler ve ERC kurallarını çalıştırır");
        command.Arguments.Add(project);
        command.Options.Add(json);
        command.SetAction(parse =>
        {
            var loaded = ProjectFile.Load(parse.GetValue(project)!);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var report = ErcEngine.Default.Run(loaded.Value);
            if (parse.GetValue(json))
            {
                WriteJson(output, report);
            }
            else
            {
                WriteText(output, loaded.Value, report);
            }

            return report.HasErrors ? ExitFailure : ExitOk;
        });
        return command;
    }

    private static Command ApplyCommand(TextWriter output, TextWriter error, string? defaultLibraryPath)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var patch = new Argument<string>("patch") { Description = "Patch dosyası (.json)" };
        var outPath = new Option<string?>("--out") { Description = "Sonucu bu dosyaya yaz (varsayılan: proje dosyası)" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Uygula ve raporla, dosya yazma" };
        var strict = new Option<bool>("--strict") { Description = "ERC hatası varsa dosya yazma" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };
        var library = new Option<string?>("--library") { Description = "Komponent kütüphanesi dosyası (patch kütüphane ürünü kullanıyorsa)" };

        var command = new Command("apply", "Patch'i projeye tek adımda uygular ve ERC raporu verir");
        command.Arguments.Add(project);
        command.Arguments.Add(patch);
        command.Options.Add(outPath);
        command.Options.Add(dryRun);
        command.Options.Add(strict);
        command.Options.Add(actor);
        command.Options.Add(library);
        command.SetAction(parse =>
        {
            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var loadedPatch = PatchJson.Load(parse.GetValue(patch)!);
            if (loadedPatch.IsFailure)
            {
                return Fail(error, loadedPatch.Error!);
            }

            // Açıkça verilen kütüphane açılır (yoksa oluşturulur); varsayılan yalnızca dosya varsa kullanılır.
            var libraryPath = parse.GetValue(library)
                ?? (defaultLibraryPath is not null && File.Exists(defaultLibraryPath) ? defaultLibraryPath : null);
            var opened = libraryPath is null ? null : ComponentLibrary.Open(libraryPath);
            if (opened is { IsFailure: true })
            {
                return Fail(error, opened.Error!);
            }

            using var catalog = opened?.Value;
            var stack = new CommandStack(loaded.Value);
            var who = loadedPatch.Value.Actor ?? parse.GetValue(actor) ?? Environment.UserName;
            var applied = stack.Execute(loadedPatch.Value.ToCommand(catalog, catalog), loadedPatch.Value.Source, who);
            if (applied.IsFailure)
            {
                return Fail(error, applied.Error!);
            }

            output.WriteLine($"Patch uygulandı: {loadedPatch.Value.CommandCount} komut.");
            var report = ErcEngine.Default.Run(loaded.Value);
            WriteText(output, loaded.Value, report);

            if (report.HasErrors && parse.GetValue(strict))
            {
                error.WriteLine("ERC hatası var ve --strict verildi: dosya yazılmadı.");
                return ExitFailure;
            }

            if (parse.GetValue(dryRun))
            {
                output.WriteLine("Deneme çalıştırması: dosya yazılmadı.");
                return report.HasErrors ? ExitFailure : ExitOk;
            }

            var saved = Save(loaded.Value, parse.GetValue(outPath) ?? projectPath, stack, output);
            return saved.IsFailure ? Fail(error, saved.Error!) : report.HasErrors ? ExitFailure : ExitOk;
        });
        return command;
    }

    private static Command RenumberCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var tags = new Option<bool>("--tags") { Description = "Device tag'lerini yeniden numarala" };
        var wires = new Option<bool>("--wires") { Description = "Tel numaralarını yeniden ver" };
        var scheme = new Option<WireNumberingScheme?>("--scheme")
        {
            Description = "Tel şeması: PerPotential ya da Sequential (verilmezse projenin ayarı; yalnızca bu çalıştırma için)",
        };
        var onlyNew = new Option<bool>("--new")
        {
            Description = "Tel numaralarını baştan vermek yerine yalnızca numarasız telleri, kaldığı yerden numarala (mevcutlara dokunulmaz)",
        };
        var prefix = new Option<string?>("--prefix") { Description = "Tel numarası kodu (önek), örn. W; projenin ayarı olarak kaydedilir" };
        var start = new Option<int?>("--start") { Description = "İlk numara; projenin ayarı olarak kaydedilir" };
        var step = new Option<int?>("--step") { Description = "Numaralar arası adım; projenin ayarı olarak kaydedilir" };
        var width = new Option<int?>("--width") { Description = "En az basamak sayısı (sıfırla doldurulur); projenin ayarı olarak kaydedilir" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Değişiklikleri göster, dosya yazma" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };

        var command = new Command("renumber", "Device tag'lerini ya da tel numaralarını yeniden verir");
        command.Arguments.Add(project);
        command.Options.Add(tags);
        command.Options.Add(wires);
        command.Options.Add(scheme);
        command.Options.Add(onlyNew);
        command.Options.Add(prefix);
        command.Options.Add(start);
        command.Options.Add(step);
        command.Options.Add(width);
        command.Options.Add(dryRun);
        command.Options.Add(actor);
        command.SetAction(parse =>
        {
            if (parse.GetValue(tags) == parse.GetValue(wires))
            {
                error.WriteLine("--tags ya da --wires seçeneklerinden tam olarak biri verilmeli.");
                return ExitFailure;
            }

            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            Transaction transaction;
            if (parse.GetValue(tags))
            {
                transaction = Renumbering.Tags(loaded.Value);
            }
            else
            {
                // Verilen kod, başlangıç, adım ve basamak projenin ayarı olarak kaydedilir ve aynı adımda uygulanır (ADR-0041).
                var current = loaded.Value.WireNumbering;
                var settings = new WireNumberSettings(
                    parse.GetValue(prefix) ?? current.Prefix,
                    parse.GetValue(start) ?? current.Start,
                    parse.GetValue(step) ?? current.Step,
                    parse.GetValue(width) ?? current.Width,
                    current.Scheme);
                if (settings.Check() is { } invalid)
                {
                    return Fail(error, invalid);
                }

                // --scheme yalnızca bu çalıştırma içindir; projenin kayıtlı şeması değişmez.
                var run = settings with { Scheme = parse.GetValue(scheme) ?? current.Scheme };
                var inner = parse.GetValue(onlyNew) ? Renumbering.NewWires(loaded.Value, run) : Renumbering.Wires(loaded.Value, run);
                transaction = settings == current
                    ? inner
                    : new Transaction(inner.Description, [new SetWireNumbering(settings), .. inner.Commands]);
            }

            return Commit(loaded.Value, projectPath, transaction, parse.GetValue(dryRun), parse.GetValue(actor), output, error);
        });
        return command;
    }

    private static Command TagSchemeCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var scheme = new Argument<string>("sema") { Description = "sayfa (3K1: sayfa + harf + sıra) ya da harf-sayi (K1)" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Değişiklikleri göster, dosya yazma" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };

        var command = new Command("tag-scheme", "Projenin tag şemasını değiştirir ve tag'leri yeni şemaya göre numaralar");
        command.Arguments.Add(project);
        command.Arguments.Add(scheme);
        command.Options.Add(dryRun);
        command.Options.Add(actor);
        command.SetAction(parse =>
        {
            TagScheme? chosen = parse.GetValue(scheme)!.Trim().ToLowerInvariant() switch
            {
                "sayfa" => TagScheme.PageLetterNumber,
                "harf-sayi" => TagScheme.LetterNumber,
                _ => null,
            };
            if (chosen is not { } value)
            {
                return Fail(error, new Error("tag.sema_gecersiz", $"Bilinmeyen tag şeması: '{parse.GetValue(scheme)}'. sayfa ya da harf-sayi olmalı."));
            }

            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var transaction = Renumbering.ChangeTagScheme(loaded.Value, value);
            return Commit(loaded.Value, projectPath, transaction, parse.GetValue(dryRun), parse.GetValue(actor), output, error);
        });
        return command;
    }

    // İşlemi çalıştırır, değişiklikleri yazar; deneme değilse projeyi audit kaydıyla kaydeder.
    private static int Commit(
        Project project, string projectPath, Transaction transaction, bool dryRun, string? actor, TextWriter output, TextWriter error)
    {
        if (transaction.Commands.Count == 0)
        {
            output.WriteLine("Değişiklik yok.");
            return ExitOk;
        }

        var stack = new CommandStack(project);
        var applied = stack.Execute(transaction, ChangeSource.User, actor ?? Environment.UserName);
        if (applied.IsFailure)
        {
            return Fail(error, applied.Error!);
        }

        foreach (var change in transaction.Commands)
        {
            output.WriteLine(change.Description);
        }

        output.WriteLine($"{transaction.Commands.Count} değişiklik.");
        if (dryRun)
        {
            output.WriteLine("Deneme çalıştırması: dosya yazılmadı.");
            return ExitOk;
        }

        var saved = Save(project, projectPath, stack, output);
        return saved.IsFailure ? Fail(error, saved.Error!) : ExitOk;
    }

    private static Command ReportCommand(TextWriter output, TextWriter error)
    {
        var report = new Argument<string>("rapor")
        {
            Description = $"Rapor adı: {string.Join(", ", ReportCatalog.Names)} ya da all (hepsi, tek XLSX)",
        };
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var format = new Option<string>("--format")
        {
            Description = "csv (varsayılan) ya da xlsx",
            DefaultValueFactory = _ => "csv",
        };
        var outPath = new Option<string?>("--out") { Description = "Çıktı dosyası (csv için verilmezse ekrana yazılır)" };
        var template = new Option<string?>("--template") { Description = "Sütun seçimi için şablon dosyası (.json)" };
        var separator = new Option<string>("--separator")
        {
            Description = "CSV ayracı (varsayılan virgül)",
            DefaultValueFactory = _ => ",",
        };

        var command = new Command("report", "Projeden rapor üretir (CSV ya da XLSX)");
        command.Arguments.Add(report);
        command.Arguments.Add(project);
        command.Options.Add(format);
        command.Options.Add(outPath);
        command.Options.Add(template);
        command.Options.Add(separator);
        command.SetAction(parse =>
        {
            var name = parse.GetValue(report)!;
            var kind = parse.GetValue(format)!.ToLowerInvariant();
            var target = parse.GetValue(outPath);
            var templatePath = parse.GetValue(template);
            var separatorText = parse.GetValue(separator)!;

            if (kind is not ("csv" or "xlsx"))
            {
                return Fail(error, new Error("rapor.bicim_yok", $"Bilinmeyen biçim '{kind}'. Biçimler: csv, xlsx"));
            }

            if (separatorText.Length != 1)
            {
                return Fail(error, new Error("rapor.ayrac_gecersiz", "CSV ayracı tek karakter olmalı."));
            }

            if (kind == "xlsx" && target is null)
            {
                return Fail(error, new Error("rapor.cikti_yok", "xlsx biçimi için --out gerekli."));
            }

            if (name == "all" && (kind != "xlsx" || templatePath is not null))
            {
                return Fail(error, new Error(
                    "rapor.all_gecersiz", "'all' yalnızca --format xlsx ile ve şablonsuz kullanılır."));
            }

            var loaded = ProjectFile.Load(parse.GetValue(project)!);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var tables = new List<ReportTable>();
            foreach (var reportName in name == "all" ? ReportCatalog.Names : [name])
            {
                var built = ReportCatalog.Build(reportName, loaded.Value);
                if (built.IsFailure)
                {
                    return Fail(error, built.Error!);
                }

                tables.Add(built.Value);
            }

            if (templatePath is not null)
            {
                var shaped = ApplyTemplate(tables[0], templatePath);
                if (shaped.IsFailure)
                {
                    return Fail(error, shaped.Error!);
                }

                tables[0] = shaped.Value;
            }

            if (kind == "csv" && target is null)
            {
                output.Write(CsvReport.Write(tables[0], separatorText[0]));
                return ExitOk;
            }

            var saved = kind == "csv"
                ? CsvReport.Save(tables[0], target!, separatorText[0])
                : XlsxReport.Save(tables, target!);
            if (saved.IsFailure)
            {
                return Fail(error, saved.Error!);
            }

            output.WriteLine($"Yazıldı: {target} ({string.Join(", ", tables.Select(t => $"{t.Name}: {t.Rows.Count} satır"))})");
            return ExitOk;
        });
        return command;
    }

    private static Command TemplateCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var template = new Argument<string>("sablon") { Description = "Sayfa şablonu dosyası (.json)" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };

        var set = new Command("set", "Sayfa şablonunu projeye ekler; aynı kimlikli şablon varsa yerine koyar");
        set.Arguments.Add(project);
        set.Arguments.Add(template);
        set.Options.Add(actor);
        set.SetAction(parse =>
        {
            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var sheet = SheetTemplateJson.Load(parse.GetValue(template)!);
            if (sheet.IsFailure)
            {
                return Fail(error, sheet.Error!);
            }

            var stack = new CommandStack(loaded.Value);
            var applied = stack.Execute(
                new PutSheetTemplate(sheet.Value), ChangeSource.User, parse.GetValue(actor) ?? Environment.UserName);
            if (applied.IsFailure)
            {
                return Fail(error, applied.Error!);
            }

            output.WriteLine($"Sayfa şablonu: {sheet.Value.Name} ({loaded.Value.SheetTemplates.Count} şablon).");
            var saved = Save(loaded.Value, projectPath, stack, output);
            return saved.IsFailure ? Fail(error, saved.Error!) : ExitOk;
        });

        var list = new Command("list", "Hazır sayfa şablonlarını listeler (A4/A3/A2, yatay ve dikey)");
        list.SetAction(_ =>
        {
            foreach (var standard in StandardSheetTemplates.All)
            {
                output.WriteLine($"{standard.Id}  {standard.Name}  ({standard.WidthMm}x{standard.HeightMm} mm)");
            }

            return ExitOk;
        });

        var wanted = new Argument<string>("sablon") { Description = "Hazır şablonun kimliği ya da adının bir kısmı (örn. 'A4 dikey')" };
        var add = new Command("add", "Hazır bir sayfa şablonunu projeye ekler (zaten varsa yerine koyar)");
        add.Arguments.Add(project);
        add.Arguments.Add(wanted);
        add.Options.Add(actor);
        add.SetAction(parse =>
        {
            var text = parse.GetValue(wanted)!.Trim();
            var matches = StandardSheetTemplates.All
                .Where(candidate => candidate.Id.ToString() == text || candidate.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1)
            {
                return Fail(error, new Error(
                    "sablon.hazir_yok",
                    matches.Count == 0
                        ? $"'{text}' adında hazır şablon yok. Şablonlar: ake template list"
                        : $"'{text}' birden çok şablona uyuyor: {string.Join(", ", matches.Select(match => match.Name))}"));
            }

            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var stack = new CommandStack(loaded.Value);
            var applied = stack.Execute(new PutSheetTemplate(matches[0]), ChangeSource.User, parse.GetValue(actor) ?? Environment.UserName);
            if (applied.IsFailure)
            {
                return Fail(error, applied.Error!);
            }

            output.WriteLine($"Sayfa şablonu: {matches[0].Name} ({loaded.Value.SheetTemplates.Count} şablon).");
            var saved = Save(loaded.Value, projectPath, stack, output);
            return saved.IsFailure ? Fail(error, saved.Error!) : ExitOk;
        });

        var command = new Command("template", "Sayfa şablonu ve antet");
        command.Subcommands.Add(set);
        command.Subcommands.Add(list);
        command.Subcommands.Add(add);
        return command;
    }

    private static Command LayoutCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Kaç sembol yerleşeceğini göster, dosya yazma" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };

        var command = new Command("layout", "Henüz yerleştirilmemiş function'ları sayfalara otomatik yerleştirir");
        command.Arguments.Add(project);
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

            var transaction = AutoLayout.Build(loaded.Value);
            if (transaction.Commands.Count == 0)
            {
                output.WriteLine("Yerleştirilecek function yok.");
                return ExitOk;
            }

            var stack = new CommandStack(loaded.Value);
            var applied = stack.Execute(transaction, ChangeSource.User, parse.GetValue(actor) ?? Environment.UserName);
            if (applied.IsFailure)
            {
                return Fail(error, applied.Error!);
            }

            var total = loaded.Value.Devices.Sum(device => device.Functions.Count);
            var unplaced = total - loaded.Value.Placements.Select(placement => placement.FunctionId).Distinct().Count();
            output.WriteLine($"{transaction.Commands.Count} sembol yerleştirildi; {unplaced} function hiçbir sayfada değil.");
            if (parse.GetValue(dryRun))
            {
                output.WriteLine("Deneme çalıştırması: dosya yazılmadı.");
                return ExitOk;
            }

            var saved = Save(loaded.Value, projectPath, stack, output);
            return saved.IsFailure ? Fail(error, saved.Error!) : ExitOk;
        });
        return command;
    }

    private static Command SymbolCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var symbol = new Argument<string>("sembol") { Description = "Sembol dosyası (.sembol.json)" };
        var actor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };

        var set = new Command("set", "Sembolü projeye ekler; aynı kimlikli sembol varsa yerine koyar");
        set.Arguments.Add(project);
        set.Arguments.Add(symbol);
        set.Options.Add(actor);
        set.SetAction(parse =>
        {
            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var definition = SymbolJson.Load(parse.GetValue(symbol)!);
            if (definition.IsFailure)
            {
                return Fail(error, definition.Error!);
            }

            var stack = new CommandStack(loaded.Value);
            var applied = stack.Execute(
                new PutSymbol(definition.Value), ChangeSource.User, parse.GetValue(actor) ?? Environment.UserName);
            if (applied.IsFailure)
            {
                return Fail(error, applied.Error!);
            }

            output.WriteLine($"Sembol: {definition.Value.Name} ({loaded.Value.Symbols.Count} sembol).");
            var saved = Save(loaded.Value, projectPath, stack, output);
            return saved.IsFailure ? Fail(error, saved.Error!) : ExitOk;
        });

        var importProject = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var importPath = new Argument<string>("yol") { Description = SymbolSourceDescription };
        var license = new Option<string?>("--license") { Description = "Lisans ya da kullanım koşulu (DXF/DWG'de zorunlu; QElectroTech'te elemanda yoksa)" };
        var scale = ScaleOption();
        var importBlocks = BlocksOption();
        var importArea = AreaOption();
        var importActor = new Option<string?>("--actor") { Description = "Audit kaydındaki kişi (varsayılan: oturum kullanıcısı)" };
        var import = new Command("import", "QElectroTech elemanlarını ve DXF/DWG çizimlerini projeye sembol olarak alır");
        import.Arguments.Add(importProject);
        import.Arguments.Add(importPath);
        import.Options.Add(license);
        import.Options.Add(scale);
        import.Options.Add(importBlocks);
        import.Options.Add(importArea);
        import.Options.Add(importActor);
        import.SetAction(parse =>
        {
            var projectPath = parse.GetValue(importProject)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var source = parse.GetValue(importPath)!;
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                return Fail(error, new Error("qet.yol_yok", $"'{source}' bulunamadı."));
            }

            var area = ParseArea(parse.GetValue(importArea));
            if (area.IsFailure)
            {
                return Fail(error, area.Error!);
            }

            var results = ImportSymbolFiles(source, parse.GetValue(license), parse.GetValue(scale), parse.GetValue(importBlocks), area.Value);
            foreach (var skipped in results.Where(result => result.Error is not null))
            {
                error.WriteLine($"ATLANDI {skipped.Error!.Code}: {skipped.Error.Message}");
            }

            foreach (var warned in results.Where(result => result.Warning is not null))
            {
                error.WriteLine($"UYARI {warned.File}: {warned.Warning}");
            }

            var symbols = results.Where(result => result.Symbol is not null).Select(result => result.Symbol!).ToList();
            if (symbols.Count == 0)
            {
                return Fail(error, new Error("qet.eleman_yok", "Alınabilecek eleman bulunamadı."));
            }

            var stack = new CommandStack(loaded.Value);
            var applied = stack.Execute(
                new Transaction($"Semboller içe aktarıldı: {symbols.Count}", [.. symbols.Select(symbol => new PutSymbol(symbol))]),
                ChangeSource.Import,
                parse.GetValue(importActor) ?? Environment.UserName);
            if (applied.IsFailure)
            {
                return Fail(error, applied.Error!);
            }

            foreach (var symbol in symbols)
            {
                output.WriteLine($"{symbol.Name}  ·  {symbol.Pins.Count} uç  ·  {symbol.License}");
            }

            output.WriteLine($"{symbols.Count} sembol alındı, {results.Count - symbols.Count} eleman atlandı.");
            var saved = Save(loaded.Value, projectPath, stack, output);
            return saved.IsFailure ? Fail(error, saved.Error!) : ExitOk;
        });

        var command = new Command("symbol", "Kullanıcı sembolleri");
        command.Subcommands.Add(set);
        command.Subcommands.Add(import);
        command.Subcommands.Add(SymbolInspectCommand(output, error));
        return command;
    }

    private static Command RenderCommand(TextWriter output, TextWriter error)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var pdf = new Option<string?>("--pdf") { Description = "Bütün sayfaları bu PDF dosyasına yaz" };
        var svg = new Option<string?>("--svg") { Description = "Her sayfayı bu klasöre ayrı SVG olarak yaz" };
        var png = new Option<string?>("--png") { Description = "Her sayfayı bu klasöre ayrı PNG olarak yaz (önizleme)" };
        var dxf = new Option<string?>("--dxf") { Description = "Her sayfayı bu klasöre ayrı DXF olarak yaz (AutoCAD 2013)" };
        var dwg = new Option<string?>("--dwg") { Description = "Her sayfayı bu klasöre ayrı DWG olarak yaz (AutoCAD 2013)" };
        var contents = new Option<bool>("--icindekiler") { Description = "Başa içindekiler sayfası ekle (PDF'te satırlar sayfalara bağlantıdır)" };

        var command = new Command("render", "Proje sayfalarını çerçeve ve antetle çıktıya yazar");
        command.Arguments.Add(project);
        command.Options.Add(pdf);
        command.Options.Add(svg);
        command.Options.Add(png);
        command.Options.Add(dxf);
        command.Options.Add(dwg);
        command.Options.Add(contents);
        command.SetAction(parse =>
        {
            var pdfPath = parse.GetValue(pdf);
            var svgDirectory = parse.GetValue(svg);
            var pngDirectory = parse.GetValue(png);
            if (pdfPath is null && svgDirectory is null && pngDirectory is null && parse.GetValue(dxf) is null && parse.GetValue(dwg) is null)
            {
                return Fail(error, new Error("render.cikti_yok", "--pdf, --svg, --png, --dxf ya da --dwg seçeneklerinden en az biri gerekli."));
            }

            var projectPath = parse.GetValue(project)!;
            var loaded = ProjectFile.Load(projectPath);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            if (loaded.Value.Pages.Count == 0)
            {
                return Fail(error, new Error("render.sayfa_yok", "Projede sayfa yok."));
            }

            var today = DateOnly.FromDateTime(DateTime.Now);
            var scenes = new List<Scene>();
            foreach (var page in loaded.Value.Pages)
            {
                var scene = SheetRenderer.Render(loaded.Value, page, today);
                if (scene.IsFailure)
                {
                    return Fail(error, scene.Error!);
                }

                scenes.Add(scene.Value);
                foreach (var warning in scene.Value.Warnings)
                {
                    error.WriteLine($"UYARI sayfa {page.Number}: {warning}");
                }
            }

            // Çıktı sırası ve dosya adları: içindekiler (istenirse) önce, sonra sayfalar.
            var named = new List<(string Name, Scene Scene)>();
            if (parse.GetValue(contents))
            {
                var table = SheetRenderer.Contents(loaded.Value, today);
                if (table.IsFailure)
                {
                    return Fail(error, table.Error!);
                }

                named.AddRange(table.Value.Select((scene, index) => ($"icindekiler-{index + 1}", scene)));
            }

            named.AddRange(scenes.Select((scene, index) => ($"sayfa-{loaded.Value.Pages[index].Number}", scene)));

            if (pdfPath is not null)
            {
                var saved = PdfExporter.Save(
                    [.. named.Select(entry => entry.Scene)], pdfPath, Path.GetDirectoryName(Path.GetFullPath(projectPath)), loaded.Value.Name);
                if (saved.IsFailure)
                {
                    return Fail(error, saved.Error!);
                }

                output.WriteLine($"Yazıldı: {pdfPath} ({named.Count} sayfa)");
            }

            if (svgDirectory is not null)
            {
                try
                {
                    Directory.CreateDirectory(svgDirectory);
                    foreach (var (name, scene) in named)
                    {
                        File.WriteAllText(Path.Combine(svgDirectory, $"{name}.svg"), SvgExporter.Write(scene));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Fail(error, new Error("render.dosya_yazilamadi", $"'{svgDirectory}' klasörüne yazılamadı: {ex.Message}"));
                }

                output.WriteLine($"Yazıldı: {svgDirectory} ({named.Count} SVG)");
            }

            if (pngDirectory is not null)
            {
                try
                {
                    Directory.CreateDirectory(pngDirectory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Fail(error, new Error("render.dosya_yazilamadi", $"'{pngDirectory}' klasörü oluşturulamadı: {ex.Message}"));
                }

                foreach (var (name, scene) in named)
                {
                    var saved = PngExporter.Save(
                        scene,
                        Path.Combine(pngDirectory, $"{name}.png"),
                        Path.GetDirectoryName(Path.GetFullPath(projectPath)));
                    if (saved.IsFailure)
                    {
                        return Fail(error, saved.Error!);
                    }
                }

                output.WriteLine($"Yazıldı: {pngDirectory} ({named.Count} PNG)");
            }

            foreach (var (directory, format) in new[] { (parse.GetValue(dxf), CadFormat.Dxf), (parse.GetValue(dwg), CadFormat.Dwg) })
            {
                if (directory is null)
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Fail(error, new Error("render.dosya_yazilamadi", $"'{directory}' klasörü oluşturulamadı: {ex.Message}"));
                }

                var extension = format == CadFormat.Dwg ? "dwg" : "dxf";
                foreach (var (name, scene) in named)
                {
                    var saved = CadExporter.Save(scene, Path.Combine(directory, $"{name}.{extension}"), format);
                    if (saved.IsFailure)
                    {
                        return Fail(error, saved.Error!);
                    }
                }

                output.WriteLine($"Yazıldı: {directory} ({named.Count} {extension.ToUpperInvariant()})");
            }

            return ExitOk;
        });
        return command;
    }

    private static Command LibCommand(TextWriter output, TextWriter error, string? defaultLibraryPath)
    {
        Option<string?> LibraryOption() => new("--library")
        {
            Description = "Komponent kütüphanesi dosyası (varsayılan: kullanıcı kütüphanesi)",
        };

        Result<ComponentLibrary> Open(ParseResult parse, Option<string?> option)
        {
            var path = parse.GetValue(option) ?? defaultLibraryPath;
            return path is null
                ? Result.Failure<ComponentLibrary>(
                    new Error("kutuphane.yol_yok", "Kütüphane dosyası belirtilmedi (--library)."))
                : ComponentLibrary.Open(path);
        }

        var importFile = new Argument<string>("dosya") { Description = "Ürün veya üretici dosyası (.json, .csv, .bmecat, .gsdml, .eds, .iodd, .aasx, .edz, .skycad, .xml)" };
        var importLibrary = LibraryOption();
        var source = new Option<string?>("--source") { Description = "Verinin kaynağı (dosyada yoksa zorunlu)" };
        var license = new Option<string?>("--license") { Description = "Verinin lisansı (dosyada yoksa zorunlu)" };
        var import = new Command("import", "Dosyadaki ürünleri kütüphaneye ekler ya da günceller");
        import.Arguments.Add(importFile);
        import.Options.Add(importLibrary);
        import.Options.Add(source);
        import.Options.Add(license);
        import.SetAction(parse =>
        {
            var products = LibraryImport.FromFile(
                parse.GetValue(importFile)!, parse.GetValue(source), parse.GetValue(license));
            if (products.IsFailure)
            {
                return Fail(error, products.Error!);
            }

            var opened = Open(parse, importLibrary);
            if (opened.IsFailure)
            {
                return Fail(error, opened.Error!);
            }

            using var library = opened.Value;
            var imported = library.Import(products.Value);
            if (imported.IsFailure)
            {
                return Fail(error, imported.Error!);
            }

            output.WriteLine(
                $"{imported.Value.Added} ürün eklendi, {imported.Value.Updated} ürün güncellendi. Kütüphanede {library.Count} ürün var.");
            return ExitOk;
        });

        var searchText = new Argument<string>("metin") { Description = "Üretici, sipariş kodu, tip, açıklama ya da kategoride aranır" };
        var searchLibrary = LibraryOption();
        var search = new Command("search", "Kütüphanede ürün arar");
        search.Arguments.Add(searchText);
        search.Options.Add(searchLibrary);
        search.SetAction(parse =>
        {
            var opened = Open(parse, searchLibrary);
            if (opened.IsFailure)
            {
                return Fail(error, opened.Error!);
            }

            using var library = opened.Value;
            var found = library.Search(parse.GetValue(searchText)!);
            foreach (var product in found)
            {
                output.WriteLine(
                    $"{product.Manufacturer}  {product.OrderCode}  {product.TypeDesignation ?? product.Description ?? string.Empty}".TrimEnd());
            }

            output.WriteLine($"{found.Count} ürün bulundu.");
            return ExitOk;
        });

        var showCode = new Argument<string>("siparis-kodu") { Description = "Ürünün sipariş kodu" };
        var showLibrary = LibraryOption();
        var manufacturer = new Option<string?>("--manufacturer") { Description = "Üretici (kod birden çok üreticide varsa)" };
        var show = new Command("show", "Ürünün ayrıntılarını ve function şablonunu gösterir");
        show.Arguments.Add(showCode);
        show.Options.Add(showLibrary);
        show.Options.Add(manufacturer);
        show.SetAction(parse =>
        {
            var opened = Open(parse, showLibrary);
            if (opened.IsFailure)
            {
                return Fail(error, opened.Error!);
            }

            using var library = opened.Value;
            var found = library.FindProduct(parse.GetValue(showCode)!, parse.GetValue(manufacturer));
            if (found.IsFailure)
            {
                return Fail(error, found.Error!);
            }

            WriteProduct(output, found.Value);
            return ExitOk;
        });

        var symbolSource = new Argument<string>("yol") { Description = SymbolSourceDescription };
        var symbolLibrary = LibraryOption();
        var symbolLicense = new Option<string?>("--license") { Description = "Lisans ya da kullanım koşulu (DXF/DWG'de zorunlu; QElectroTech'te elemanda ve koleksiyonda yoksa)" };
        var symbolScale = ScaleOption();
        var symbolBlocks = BlocksOption();
        var symbolArea = AreaOption();
        var symbolImport = new Command("symbol-import", "QElectroTech elemanlarını ve DXF/DWG çizimlerini paylaşılan sembol kütüphanesine alır");
        symbolImport.Arguments.Add(symbolSource);
        symbolImport.Options.Add(symbolLibrary);
        symbolImport.Options.Add(symbolLicense);
        symbolImport.Options.Add(symbolScale);
        symbolImport.Options.Add(symbolBlocks);
        symbolImport.Options.Add(symbolArea);
        symbolImport.SetAction(parse =>
        {
            var source = parse.GetValue(symbolSource)!;
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                return Fail(error, new Error("qet.yol_yok", $"'{source}' bulunamadı."));
            }

            var libraryArea = ParseArea(parse.GetValue(symbolArea));
            if (libraryArea.IsFailure)
            {
                return Fail(error, libraryArea.Error!);
            }

            var results = ImportSymbolFiles(source, parse.GetValue(symbolLicense), parse.GetValue(symbolScale), parse.GetValue(symbolBlocks), libraryArea.Value);
            var skipped = results.Where(result => result.Error is not null).ToList();
            foreach (var group in skipped.GroupBy(result => result.Error!.Code).OrderByDescending(group => group.Count()))
            {
                error.WriteLine($"ATLANDI {group.Key}: {group.Count()} eleman (ör. {group.First().File})");
            }

            foreach (var group in results.Where(result => result.Warning is not null).GroupBy(result => result.Warning))
            {
                error.WriteLine($"UYARI {group.Count()} sembol: {group.Key} (ör. {group.First().File})");
            }

            var symbols = results
                .Where(result => result.Symbol is not null)
                .Select(result => new LibrarySymbol(
                    result.Symbol!, Path.GetDirectoryName(Path.Combine(Path.GetFileName(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar)), result.File))))
                .ToList();
            if (symbols.Count == 0)
            {
                return Fail(error, new Error("qet.eleman_yok", "Alınabilecek eleman bulunamadı."));
            }

            var opened = Open(parse, symbolLibrary);
            if (opened.IsFailure)
            {
                return Fail(error, opened.Error!);
            }

            using var library = opened.Value;
            var imported = library.ImportSymbols(symbols);
            if (imported.IsFailure)
            {
                return Fail(error, imported.Error!);
            }

            output.WriteLine(
                $"{imported.Value.Added} sembol eklendi, {imported.Value.Updated} güncellendi, {skipped.Count} eleman atlandı. Kütüphanede {library.SymbolCount} sembol var.");
            return ExitOk;
        });

        var symbolText = new Argument<string>("metin") { Description = "Sembol adında ya da kategorisinde aranır" };
        var symbolSearchLibrary = LibraryOption();
        var symbolPins = new Option<int?>("--pins") { Description = "Yalnızca bu sayıda ucu olan semboller" };
        var symbolSearch = new Command("symbols", "Paylaşılan sembol kütüphanesinde arar");
        symbolSearch.Arguments.Add(symbolText);
        symbolSearch.Options.Add(symbolSearchLibrary);
        symbolSearch.Options.Add(symbolPins);
        symbolSearch.SetAction(parse =>
        {
            var opened = Open(parse, symbolSearchLibrary);
            if (opened.IsFailure)
            {
                return Fail(error, opened.Error!);
            }

            using var library = opened.Value;
            var found = library.SearchSymbols(parse.GetValue(symbolText)!, parse.GetValue(symbolPins));
            foreach (var symbol in found)
            {
                output.WriteLine($"{symbol.Id}  {symbol.Category}/{symbol.Name}  ·  {symbol.Pins} uç");
            }

            output.WriteLine($"{found.Count} sembol bulundu.");
            return ExitOk;
        });

        var command = new Command("lib", "Komponent kütüphanesi");
        command.Subcommands.Add(symbolImport);
        command.Subcommands.Add(symbolSearch);
        command.Subcommands.Add(import);
        command.Subcommands.Add(search);
        command.Subcommands.Add(show);
        return command;
    }

    private static void WriteProduct(TextWriter output, LibraryProduct product)
    {
        var definition = product.Definition;
        output.WriteLine($"{definition.Manufacturer} {definition.OrderCode}");
        WriteField(output, "Tip", definition.TypeDesignation);
        WriteField(output, "Açıklama", definition.Description);
        WriteField(output, "Kategori", product.Category);
        if (definition.Mounting is { } mounting)
        {
            WriteField(
                output,
                "Montaj",
                FormattableString.Invariant(
                    $"{mounting.Type}, {mounting.WidthMm} × {mounting.HeightMm} × {mounting.DepthMm} mm, ısı kaybı {mounting.HeatLossW} W"));
        }

        foreach (var function in definition.Functions)
        {
            var rated = function.RatedCurrentA is { } amps ? FormattableString.Invariant($", {amps} A") : string.Empty;
            output.WriteLine(
                $"  {function.Kind}: {string.Join(", ", function.ConnectionPoints)}{rated}{(function.Description is null ? string.Empty : $"  ({function.Description})")}");
        }

        foreach (var property in product.Properties ?? [])
        {
            output.WriteLine($"  {property.Key} = {property.Value}{(property.Unit is null ? string.Empty : $" {property.Unit}")}");
        }

        WriteField(output, "Kaynak", product.Source);
        WriteField(output, "Lisans", product.License);
    }

    private static void WriteField(TextWriter output, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            output.WriteLine($"{label}: {value}");
        }
    }

    private static Result<ReportTable> ApplyTemplate(ReportTable table, string templatePath)
    {
        string json;
        try
        {
            json = File.ReadAllText(templatePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<ReportTable>(
                new Error("rapor.sablon_okunamadi", $"'{templatePath}' okunamadı: {ex.Message}"));
        }

        var parsed = ReportTemplate.Parse(json);
        return parsed.IsFailure ? Result.Failure<ReportTable>(parsed.Error!) : table.Apply(parsed.Value);
    }

    private static Result Save(Project project, string path, CommandStack stack, TextWriter output)
    {
        var saved = ProjectFile.Save(project, path);
        if (saved.IsFailure)
        {
            return saved;
        }

        output.WriteLine($"Yazıldı: {path}");
        return AuditLog.Append(AuditLog.PathFor(path), stack.Audit);
    }

    private static int Fail(TextWriter error, Error failure)
    {
        error.WriteLine($"HATA {failure.Code}: {failure.Message}");
        return ExitFailure;
    }

    private static void WriteText(TextWriter output, Project project, ErcReport report)
    {
        foreach (var finding in report.Findings)
        {
            var label = finding.Severity switch
            {
                Severity.Error => "HATA ",
                Severity.Warning => "UYARI",
                _ => "BİLGİ",
            };
            output.WriteLine($"{label} {finding.RuleId}: {finding.Message}");
        }

        output.WriteLine(
            $"ERC: {project.Devices.Count} device, {project.Connections.Count} bağlantı; "
            + $"{report.Count(Severity.Error)} hata, {report.Count(Severity.Warning)} uyarı.");
    }

    private static void WriteJson(TextWriter output, ErcReport report)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("errors", report.Count(Severity.Error));
            writer.WriteNumber("warnings", report.Count(Severity.Warning));
            writer.WriteStartArray("findings");
            foreach (var finding in report.Findings)
            {
                writer.WriteStartObject();
                writer.WriteString("ruleId", finding.RuleId);
                writer.WriteString("severity", finding.Severity.ToString());
                writer.WriteString("message", finding.Message);
                writer.WriteStartArray("targets");
                foreach (var target in finding.Targets)
                {
                    writer.WriteStringValue(target);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        output.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
