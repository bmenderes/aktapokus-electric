using Ake.Editor;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

// Editör pencerelerini ekrana açmadan (başsız) çizer ve PNG olarak kaydeder. Görsel denetim için:
// masaüstünde fare ya da klavye kullanmaya gerek kalmaz.
//
// Kullanım: dotnet run --project tools/Ake.EditorSnapshot -- <proje.json> <çıktı-klasörü> [sembol-kütüphanesi.db] [arama]
// Kütüphane verilirse ana pencere bir de Semboller sekmesiyle çizilir: ilk sembol seçili ve önizlemede.
if (args.Length is < 2 or > 4)
{
    Console.Error.WriteLine("Kullanım: Ake.EditorSnapshot <proje.json> <çıktı-klasörü> [sembol-kütüphanesi.db] [arama]");
    return 1;
}

var (projectPath, output) = (args[0], args[1]);
Directory.CreateDirectory(output);

AppBuilder.Configure<App>()
    .UseSkia()
    .WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

void Save(Window window, string name)
{
    window.Show();
    for (var i = 0; i < 5; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    using var frame = window.CaptureRenderedFrame()!;
    using var file = File.Create(Path.Combine(output, name));
    frame.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    Console.WriteLine($"Yazıldı: {Path.Combine(output, name)}");
}

var main = new MainWindow();
main.OpenProject(projectPath);
main.OpenPage(1);
main.OpenPage(2);
Save(main, "ana-pencere.png");

// Tam çizim alanı (F11): paneller kapalı, şerit daraltılmış.
var ribbon = main.FindControl<RibbonView>("Ribbon")!;
ribbon.SelectTab(1);
Save(main, "serit-ekle.png");
ribbon.SelectTab(3);
Save(main, "serit-sayfa.png");
// Sayfa ekle: yeni sayfa sekmede açılır ve soldaki sayfa listesinde görünür.
ribbon.Run(EditorCommands.NewPage);
ribbon.Run(EditorCommands.InsertPage);
Save(main, "sayfa-eklendi.png");
ribbon.Run(EditorCommands.Undo);
ribbon.Run(EditorCommands.Undo);
Save(main, "sayfa-geri-alindi.png");
ribbon.SelectTab(0);
ribbon.Run(EditorCommands.SelectAll);
Save(main, "coklu-secim.png");
ribbon.Run(EditorCommands.Deselect);

// Tele tıklayınca tel seçilir ve kalın vurguyla çizilir.
main.OpenPage(1);
var wireSession = new EditorSession("snapshot");
wireSession.Open(projectPath);
var wire = wireSession.WiresFor(1).First();
var segment = Enumerable.Range(1, wire.Path.Count - 1).Select(i => (A: wire.Path[i - 1], B: wire.Path[i]))
    .OrderByDescending(s => Math.Abs(s.B.X - s.A.X) + Math.Abs(s.B.Y - s.A.Y)).First();
main.ActiveCanvas.ContentPressed?.Invoke((segment.A.X + segment.B.X) / 2, (segment.A.Y + segment.B.Y) / 2);
Save(main, "tel-secimi.png");

// Alt paneldeki geniş listeler ve sayfa ağacı süzgeci.
foreach (var panel in new[] { "klemensler", "plc", "kablolar" })
{
    main.ShowPanel(panel);
    Save(main, $"liste-{panel}.png");
}

main.ShowPanel("mesajlar");
main.FindControl<TextBox>("PageFilter")!.Text = "motor";
Save(main, "sayfa-agaci-suzgec.png");
main.FindControl<TextBox>("PageFilter")!.Text = string.Empty;

// Sayfa gezgini (ADR-0051): liste görünümü ve alan bazlı süzgeç (Fonksiyon: =M1); seçili sayfanın önizlemesi altta.
main.FindControl<Avalonia.Controls.Primitives.ToggleButton>("PageListModeButton")!.RaiseEvent(
    new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));
Save(main, "sayfa-liste.png");
main.FindControl<ComboBox>("PageFilterFieldBox")!.SelectedIndex = 4;
main.FindControl<TextBox>("PageFilter")!.Text = "m1";
Save(main, "sayfa-liste-alan-suzgeci.png");
main.FindControl<TextBox>("PageFilter")!.Text = string.Empty;
main.FindControl<ComboBox>("PageFilterFieldBox")!.SelectedIndex = 0;
main.FindControl<Avalonia.Controls.Primitives.ToggleButton>("PageTreeModeButton")!.RaiseEvent(
    new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));

// Köşe noktalı tel: tel çekme modunda ilk uca, boş bir yere (köşe) ve ikinci uca tıklanır; tel köşeden geçer.
var routed = new MainWindow();
routed.OpenProject(projectPath);
routed.OpenPage(2);
var routing = new EditorSession("snapshot");
routing.Open(projectPath);
var freePins = routing.PinsFor(2)
    .Where(pin => routing.Project!.Connections.All(c => c.From != pin.Point && c.To != pin.Point))
    .OrderBy(pin => pin.Y).ThenBy(pin => pin.X)
    .Take(2)
    .ToList();
if (freePins.Count == 2)
{
    routed.FindControl<RibbonView>("Ribbon")!.Run(EditorCommands.DrawWire);
    var pinA = freePins[0];
    var pinB = freePins[1];
    routed.ActiveCanvas.ContentPressed?.Invoke(pinA.X, pinA.Y);
    routed.ActiveCanvas.ContentPressed?.Invoke(pinA.X + 60, pinA.Y + 40);
    routed.ActiveCanvas.ContentPressed?.Invoke(pinA.X + 60, pinB.Y + 45);
    routed.ActiveCanvas.ContentPressed?.Invoke(pinB.X, pinB.Y);
    Save(routed, "tel-koseli.png");
}

// Sembole sağ tık: bağlam menüsü (başsız ortamda açılır pencere çizilmeyebilir; yalnızca hata vermediği denenir).
var symbolBox = wireSession.BoxesFor(1).First();
main.ActiveCanvas.RequestContextMenu(symbolBox.Bounds.X + (symbolBox.Bounds.Width / 2), symbolBox.Bounds.Y + (symbolBox.Bounds.Height / 2));
Save(main, "sag-tik-sembol.png");
main.ActiveCanvas.RequestContextMenu(300, 150);
Save(main, "sag-tik-bos.png");

// Proje oluşturma ve proje özellikleri pencereleri (ADR-0047, ADR-0055).
Save(new NewProjectWindow(Path.Combine(output, "projeler")), "proje-olustur.png");
var projectProps = new ProjectPropertiesWindow(wireSession);
Save(projectProps, "proje-ozellikleri.png");
var projectTabs = projectProps.GetVisualDescendants().OfType<TabControl>().First();
projectTabs.SelectedIndex = 2;
Save(projectProps, "proje-ozellikleri-yapi.png");
var newPageTemplates = new List<(Ake.Core.Model.SheetTemplateId? Id, string Label)> { (null, "Önceki sayfayla aynı") };
newPageTemplates.AddRange(wireSession.Project!.SheetTemplates.Select(t => ((Ake.Core.Model.SheetTemplateId?)t.Id, t.Name)));
Save(new NewPageWindow(true, newPageTemplates, wireSession, 1), "yeni-sayfa.png");
Save(new PagePropertiesWindow(wireSession.PageDataOf(1)!), "sayfa-ozellikleri.png");
Save(new CableWindow(wireSession, wireSession.Project.Connections.First().Id), "kablo-yonetimi.png");
Save(new PotentialWindow(wireSession, wireSession.Project.Devices[0].Functions[0].ConnectionPoints[0].Id), "potansiyel-isareti.png");
ribbon.Run(EditorCommands.Deselect);
ribbon.Run(EditorCommands.Focus);
Save(main, "ana-pencere-tam-alan.png");
ribbon.Run(EditorCommands.Focus);

// Panel düzeni: kapanırken çıktı klasörüne yazılır, yeni pencerede okunur (kullanıcının düzenine dokunulmaz).
main.LayoutPath = Path.Combine(output, "duzen.json");
main.Close();
var reopened = new MainWindow { LayoutPath = main.LayoutPath };
reopened.LoadLayout();
reopened.OpenProject(projectPath);
var status = reopened.FindControl<TextBlock>("StatusText")!.Text ?? string.Empty;
Console.WriteLine(File.Exists(main.LayoutPath) && !status.Contains("düzen", StringComparison.Ordinal)
    ? $"Düzen kaydedildi ve yeniden yüklendi: {main.LayoutPath}"
    : $"DÜZEN YÜKLENEMEDİ: {status}");
Save(reopened, "ana-pencere-duzen-yuklendi.png");
main = reopened;

if (args.Length >= 3)
{
    Ake.Library.StandardSymbols.EnsureInstalled(args[2]);
    main.OpenLibrary(args[2]);
    main.ShowSymbolsTab();
    main.FindControl<TextBox>("SymbolSearch")!.Text = args.Length == 4 ? args[3] : string.Empty;
    Dispatcher.UIThread.RunJobs();
    var tree = main.FindControl<TreeView>("SymbolList")!;
    static SymbolRow? FirstRow(System.Collections.IEnumerable items)
    {
        foreach (var item in items)
        {
            if (item is SymbolRow row)
            {
                return row;
            }

            if (item is SymbolGroup group && FirstRow(group.Items) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

    tree.SelectedItem = tree.ItemsSource is System.Collections.IEnumerable groups ? FirstRow(groups) : null;
    Save(main, "ana-pencere-semboller.png");
}

var session = new EditorSession("snapshot");
if (session.Open(projectPath).IsFailure)
{
    Console.Error.WriteLine("Proje açılamadı.");
    return 1;
}

var first = session.BoxesFor(0).FirstOrDefault();
var function = first is null ? null : session.FunctionOf(first.Placement);
var block = new SymbolEditorWindow(session, function);
block.Ribbon.Run(SymbolEditorCommands.ToolPin);
block.Ribbon.Run(SymbolEditorCommands.DirectionLeft);
Console.WriteLine(block.CurrentTool == SymbolTool.Pin ? "Blok editörü: şeritten Uç aracı seçildi." : $"BLOK EDİTÖRÜ ARAÇ HATASI: {block.CurrentTool}");
Save(block, "blok-editoru.png");
Save(new SymbolEditorWindow(session, null), "blok-editoru-bos.png");

// Sekmeli özellik penceresi: Birincil fonksiyon, Görüntüleme, Sembol/Noktalar, Ürün ve Kontak aynası sekmeleri.
var motorBox = session.BoxesFor(1).First();
var properties = new DevicePropertiesWindow(session.EditorDataOf(motorBox.Placement)!, session);
Save(properties, "ozellikler-genel.png");
var tabs = properties.GetVisualDescendants().OfType<TabControl>().First();
tabs.SelectedIndex = 1;
Save(properties, "ozellikler-goruntuleme.png");
tabs.SelectedIndex = 2;
Save(properties, "ozellikler-noktalar.png");
tabs.SelectedIndex = 3;
Save(properties, "ozellikler-urun.png");
tabs.SelectedIndex = 4;
Save(properties, "ozellikler-kontak-aynasi.png");

if (args.Length >= 3)
{
    var pickerSession = new EditorSession("snapshot");
    pickerSession.Open(projectPath);
    pickerSession.OpenLibrary(args[2]);
    Save(new SymbolSelectionWindow(pickerSession), "sembol-secimi.png");
    Save(new ProductSelectionWindow(pickerSession, allowAddDevice: true), "urun-secimi.png");
}

Save(new ExportWindow(session, 1), "disa-aktar.png");
Save(new PlcIoWindow(session), "plc-capraz-referans.png");

// Klemens şeridi düzenleyicisi (X1 şeridi).
var strip = session.Project!.Devices.First(device => device.ProductTag == "X1");
var stripData = session.TerminalStripOf(strip.Id)!;
Save(new TerminalStripWindow(stripData with { Terminals = [.. stripData.Terminals, new TerminalRow(null, "9", false, false, 0, string.Empty)] }), "klemens-seridi.png");

// Kısayol penceresi (Kaydet için özel bir kısayol atanmış) ve Araçlar sekmesi.
var keys = KeyMap.Default();
keys.Set(EditorCommands.Save, "Ctrl+Shift+K");
Save(new KeyMapWindow(keys), "kisayollar.png");
Save(new AboutWindow(), "hakkinda.png");
ribbon.SelectTab(5);
Save(main, "serit-araclar.png");
ribbon.SelectTab(0);

// Yeni proje (değişiklik yokken soru sorulmaz, işlem hemen biter) ve kaydedilmemiş değişiklik sorusu.
var fresh = new MainWindow();
fresh.NewProject(new NewProjectInfo("Yeni proje")).GetAwaiter().GetResult();
Save(fresh, "yeni-proje.png");

// Kapakta sağ tık: önce "Proje özellikleri… (kapak bilgileri)".
fresh.ActiveCanvas.RequestContextMenu(300, 150);
Save(fresh, "sag-tik-kapak.png");

var coverSession = new EditorSession("snapshot");
coverSession.NewProject(new NewProjectInfo("İki Motorlu Konveyör Hattı", Creator: "Barış Menderes"), new DateOnly(2026, 11, 20));
coverSession.SetProjectInfo("İki Motorlu Konveyör Hattı", new Dictionary<string, string>
{
    ["description"] = "Ana tahrik ve konveyör kumanda panosu elektrik projesi",
    ["customer"] = "Örnek Müşteri A.Ş.",
    ["commission"] = "SP-2026-0412",
    ["drawingNo"] = "ELK-KONV-001",
    ["company"] = "Örnek Firma Ltd. Şti.",
    ["companyAddress"] = "Teknopark Bulvarı No: 42, İstanbul",
    ["drawnBy"] = "Barış Menderes",
    ["date"] = "2026-11-20",
    ["revision"] = "Rev. 02"
});
var coverPath = Path.Combine(output, "_kapak.json");
coverSession.SaveAs(coverPath);
fresh.OpenProject(coverPath);
Save(fresh, "kapak-dolu.png");
File.Delete(coverPath);

Save(new UnsavedChangesDialog(session.Project!.Name), "kaydet-sorusu.png");
return 0;
