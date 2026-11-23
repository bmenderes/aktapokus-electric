using System.Diagnostics;
using System.Globalization;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Macros;
using Ake.Core.Model;
using Ake.Editor;
using Ake.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

// Performans ölçümü (Faz 6.3, madde 15): referans makineden makrolarla büyük bir proje kurar, oturum ve pencere
// düzeyinde ölçer. Hedefler (ROADMAP Bölüm 0): soğuk açılış < 3 sn, RAM < 300 MB, açık sayfa dışında render yok.
// Kullanım: dotnet run -c Release --project tools/Ake.Bench -- [sayfa-sayısı=300] [referans-proje.json]

var startedAt = Process.GetCurrentProcess().StartTime;

// Soğuk açılış: temiz süreçte pencereyi açıp süreç başlangıcından ilk kareye kadar geçen süre.
if (args is ["--ac", var coldPath, ..])
{
    AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    var phase = Stopwatch.StartNew();
    void Mark(string name)
    {
        Console.WriteLine($"  . {name,-34} {phase.Elapsed.TotalMilliseconds,6:0} ms");
        phase.Restart();
    }

    Mark("Avalonia kurulumu");
    var cold = new MainWindow();
    Mark("MainWindow oluştur");
    cold.Show();
    Mark("pencereyi göster");
    if (args.Length > 2 && args[2] == "agac-yok")
    {
        // Deney: sayfa ağacı boş kalsın (süzgeç hiçbir sayfayla eşleşmez); ağacın payını ayırmak için.
        cold.FindControl<TextBox>("PageFilter")!.Text = "eslesmeyen-metin";
    }

    cold.OpenProject(coldPath);
    Mark("proje aç (OpenProject)");
    for (var i = 0; i < 3; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    Mark("üç kare");
    var seconds = (DateTime.Now - startedAt).TotalSeconds;
    Console.WriteLine(seconds.ToString("0.00", CultureInfo.InvariantCulture));
    return 0;
}

var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
var verifyOnly = args.Contains("--verify", StringComparer.OrdinalIgnoreCase);
var pageCount = positional.Length > 0 && int.TryParse(positional[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 300;
var reference = positional.Length > 1 ? positional[1] : FindReference();
var report = new List<(string Name, string Value, bool? Ok)>();

void Add(string name, string value, bool? ok = null)
{
    report.Add((name, value, ok));
    Console.WriteLine($"  {name,-46} {value}{(ok is null ? string.Empty : ok.Value ? "   TAMAM" : "   HEDEF AŞILDI")}");
}

string Ms(double ms) => ms < 10 ? $"{ms:0.0} ms" : $"{ms:0} ms";

static double Time(Action action)
{
    var watch = Stopwatch.StartNew();
    action();
    return watch.Elapsed.TotalMilliseconds;
}

// İlk çağrı JIT ve önbellek ısınmasını içerir (soğuk), ikincisi uzun süre çalışan editörün gördüğüdür (sıcak).
string Twice(Action action) => $"{Ms(Time(action))} → {Ms(Time(action))}";

static double Median(IEnumerable<double> values)
{
    var sorted = values.Order().ToList();
    return sorted[sorted.Count / 2];
}

Console.WriteLine($"Ölçüm: {pageCount} sayfa, referans {Path.GetFileName(reference)}, {Environment.ProcessorCount} çekirdek");

// 1) Proje kurma: referans sayfalarının makrolarını yeni sayfalara yerleştir (gerçek cihaz, tel ve yerleşim).
Console.WriteLine("\nProje kurma");
var template = ProjectFile.Load(reference).Value;
var stack = new CommandStack(template);
var macros = new[] { template.Pages[1], template.Pages[2] }
    .Select(page => MacroService.Capture(template, page.Id, page.Title).Value)
    .ToArray();
var inserts = new List<double>();
var templateId = template.SheetTemplates[0].Id;
while (template.Pages.Count < pageCount)
{
    var index = template.Pages.Count;
    var page = new Ake.Core.Model.Page(PageId.New(), (index + 1).ToString(CultureInfo.InvariantCulture), $"Sayfa {index + 1}", templateId: templateId);
    var watch = Stopwatch.StartNew();
    Check(stack.Execute(new AddPage(page), ChangeSource.Import, "bench"));
    var insert = MacroService.Insert(template, macros[index % macros.Length], page.Id, 40, 40);
    Check(stack.Execute(insert.Value, ChangeSource.Import, "bench"));
    inserts.Add(watch.Elapsed.TotalMilliseconds);
}

static void Check(Result result)
{
    if (result.IsFailure)
    {
        throw new InvalidOperationException(result.Error!.Message);
    }
}

Console.WriteLine($"  {template.Pages.Count} sayfa, {template.Devices.Count} cihaz, {template.Connections.Count} bağlantı, {template.Placements.Count} yerleşim");
Add("sayfa ekle + makro yerleştir (ilk 10 ortanca)", Ms(Median(inserts.Take(10))));
Add("sayfa ekle + makro yerleştir (son 10 ortanca)", Ms(Median(inserts.TakeLast(10))));

var directory = Directory.CreateTempSubdirectory("ake-bench-");
var path = Path.Combine(directory.FullName, "buyuk.json");
var saveMs = Time(() => Check(ProjectFile.Save(template, path)));
Add("kaydet", Ms(saveMs));
Add("dosya boyutu", $"{new FileInfo(path).Length / 1024.0 / 1024.0:0.0} MB");
template = null!;
stack = null!;

// 2) Oturum düzeyi: açma, sayfa çizimi, navigator'lar, komut.
Console.WriteLine("\nOturum (arayüzsüz)");
GC.Collect();
using var session = new EditorSession("bench");
Add("proje yükle (yalnızca dosya)", Ms(Time(() => _ = ProjectFile.Load(path).Value)));
Add("proje aç (yükle + ERC)", Ms(Time(() => Check(session.Open(path)))));
Add("ERC tek başına (soğuk → sıcak)", Twice(() => _ = Ake.Core.Erc.ErcEngine.Default.Run(session.Project!)));
var rng = new Random(7);
var pages = Enumerable.Range(0, 20).Select(_ => rng.Next(session.Project!.Pages.Count)).ToList();
var scene = pages.Select(page => Time(() => _ = session.SceneFor(page, DateOnly.FromDateTime(DateTime.Now)).Value)).ToList();
Add("sayfa çiz (SceneFor), ortanca / en kötü", $"{Ms(Median(scene))} / {Ms(scene.Max())}", Median(scene) < 100);
Add("sembol kutuları (BoxesFor)", Ms(Median(pages.Select(page => Time(() => _ = session.BoxesFor(page))))));
Add("tel yolları (WiresFor)", Ms(Median(pages.Select(page => Time(() => _ = session.WiresFor(page))))));
Add("sayfa ağacı (soğuk → sıcak)", Twice(() => _ = session.PageTree()));
Add("sayfa ağacı, süzgeçli", Twice(() => _ = session.PageTree("motor")));
Add("cihaz navigator'ı", Twice(() => _ = session.DeviceRows()));
Add("potansiyel navigator'ı", Twice(() => _ = session.PotentialRows()));
Add("klemens navigator'ı", Twice(() => _ = session.TerminalRows()));
Add("PLC navigator'ı", Twice(() => _ = session.PlcRows()));
Add("kablo navigator'ı", Twice(() => _ = session.CableRows()));
Add("sayfa ağacı satırları (düzleştirilmiş)", Twice(() => _ = session.PageTreeRows(null, new HashSet<string>())));
Add("mesajlar (ERC bulguları)", Twice(() => _ = session.FindingRows()));

var placement = session.BoxesFor(150).First().Placement;
var move = new List<double>();
for (var i = 0; i < 5; i++)
{
    move.Add(Time(() => Check(session.MoveMany([placement], 5, 0))));
    move.Add(Time(() => Check(session.Undo())));
}

Add("taşı / geri al (komut + ERC), ortanca", Ms(Median(move)), Median(move) < 250);
Add("kaydet (oturum)", Ms(Time(() => Check(session.SaveAs(Path.Combine(directory.FullName, "kopya.json"))))));

// 3) Pencere düzeyi (başsız): soğuk açılış, sayfa geçişi, bellek.
AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

void Frames(Avalonia.Controls.Window window)
{
    for (var i = 0; i < 3; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}

var window = new MainWindow();
window.Show();
Console.WriteLine("\nSoğuk açılış (ayrı süreç)");
var cache = Path.Combine(Path.GetTempPath(), "ake-bench");
Directory.CreateDirectory(cache);
var kept = Path.Combine(cache, $"buyuk-{pageCount}.json");
File.Copy(path, kept, overwrite: true);
var self = Environment.ProcessPath!;
var launch = Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
    ? new ProcessStartInfo(self, [Path.Combine(AppContext.BaseDirectory, "Ake.Bench.dll"), "--ac", kept])
    : new ProcessStartInfo(self, ["--ac", kept]);
launch.RedirectStandardOutput = true;

// Soğuk açılış çalıştırmalar arasında ±1 sn oynar (disk önbelleği, arka plan yükü); üç çalıştırmanın ortancası alınır.
var colds = new List<double>();
for (var run = 0; run < 3; run++)
{
    using var child = Process.Start(launch)!;
    var lines = child.StandardOutput.ReadToEnd().Split((char)10, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    child.WaitForExit();
    if (run == 0)
    {
        foreach (var line in lines.Where(line => line.StartsWith(". ", StringComparison.Ordinal)))
        {
            Console.WriteLine(line);
        }
    }

    colds.Add(double.TryParse(lines.LastOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : double.NaN);
}

var coldLimit = verifyOnly ? 6.0 : 3.0;
Add(
    "süreç başlangıcından ilk kareye, ortanca (en az / en çok)",
    $"{Median(colds):0.0} sn ({colds.Min():0.0} / {colds.Max():0.0})",
    Median(colds) < coldLimit);

Console.WriteLine("\nPencere (başsız çizim, ısınmış süreç)");
var openMs = Time(() =>
{
    window.OpenProject(path);
    Frames(window);
});
Add("pencerede proje aç + ilk kare", Ms(openMs));

var switches = new List<double>();
foreach (var page in pages.Distinct())
{
    switches.Add(Time(() =>
    {
        window.OpenPage(page);
        Frames(window);
    }));
}

Add("sayfa geçişi (aç + çiz), ortanca / en kötü", $"{Ms(Median(switches))} / {Ms(switches.Max())}", Median(switches) < 500);
var expectedTabs = pages.Distinct().Append(0).Distinct().Count();
Add(
    "açık sekme sayısı (gezilen sayfalar + ilk sayfa)",
    $"{window.OpenPageCount} / {pageCount} sayfa (beklenen {expectedTabs})",
    window.OpenPageCount == expectedTabs);

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var managed = GC.GetTotalMemory(forceFullCollection: true) / 1024.0 / 1024.0;
var workingSet = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;
Add("yönetilen bellek (GC sonrası)", $"{managed:0} MB", managed < 300);
Add("çalışma kümesi (süreç)", $"{workingSet:0} MB", workingSet < 300);

directory.Delete(recursive: true);
Console.WriteLine();
var failed = report.Count(item => item.Ok == false);
Console.WriteLine(failed == 0 ? "Hedeflerin hepsi sağlandı." : $"{failed} hedef aşıldı.");
return failed == 0 ? 0 : 1;

static string FindReference()
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
    {
        root = root.Parent;
    }

    return Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json");
}
