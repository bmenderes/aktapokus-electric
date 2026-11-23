using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Ake.Editor;

/// <summary>Hakkında penceresinin içeriği (ADR-0038): ad, sürüm, açık kaynak bilgisi, kullanılan bileşenler. Arayüzden bağımsız, sınanabilir.</summary>
public static class AboutInfo
{
    public const string ProductName = "aktapokus-electric";

    public const string Tagline = "Elektrik projelendirme (E-CAD) uygulaması";

    public const string LicenseName = "Apache License 2.0";

    /// <summary>Uygulamanın sürümü (derleme sürümü); okunamazsa "0.1.0".</summary>
    public static string Version =>
        typeof(AboutInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";

    public const string OpenSourceText =
        "aktapokus-electric özgür ve açık kaynaklı bir yazılımdır. Apache License 2.0 altında yayımlanır: kaynak kodunu "
        + "inceleyebilir, değiştirebilir ve (lisans koşullarıyla) dağıtabilirsiniz. Proje kendi dosya biçimini (JSON) açık tutar; "
        + "çizimleriniz sizindir ve başka araçlara PDF, DXF ve DWG olarak aktarılabilir. Katkılar ve geri bildirimler açıktır.";

    public const string SymbolsText =
        "Standart komponent seti bu projenin kendi çizimidir (Apache-2.0). İçe aktarılan üçüncü taraf sembol kütüphaneleri "
        + "(örn. B. Favre DXF sembol kütüphanesi, CC BY 4.0) kaynak, lisans ve atıfla birlikte kütüphanede kayıtlıdır.";

    public const string WarrantyText = "Yazılım olduğu gibi sunulur; hiçbir garanti verilmez.";

    /// <summary>Kullanılan açık kaynak bileşenler ve lisansları.</summary>
    public static IReadOnlyList<(string Name, string License)> Components { get; } =
    [
        ("Avalonia, Dock.Avalonia, FluentIcons", "MIT"),
        ("SkiaSharp", "MIT"),
        ("ACadSharp (DXF/DWG)", "MIT"),
        ("PdfPig (PDF okuma)", "Apache-2.0"),
        ("ClosedXML (Excel)", "MIT"),
        ("Microsoft.Data.Sqlite", "MIT"),
        ("Serilog", "Apache-2.0"),
        ("Inter yazı tipi", "SIL Open Font License 1.1"),
    ];

    /// <summary>Gömülü LICENSE metni (Apache License 2.0).</summary>
    public static string LicenseText()
    {
        using var stream = typeof(AboutInfo).Assembly.GetManifestResourceStream("Ake.Editor.LICENSE.txt");
        if (stream is null)
        {
            return LicenseName;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>Hakkında penceresi: logo, ad, sürüm, açık kaynak bilgisi ve lisans.</summary>
public sealed class AboutWindow : Window
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#2B2619"));

    public AboutWindow()
    {
        Title = $"Hakkında — {AboutInfo.ProductName}";
        Width = 560;
        Height = 780;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 14;
        Background = new SolidColorBrush(Color.Parse("#F2EDE1"));

        var content = new StackPanel { Spacing = 10, Margin = new Thickness(28, 24) };
        content.Children.Add(Logo());
        content.Children.Add(new TextBlock { Text = AboutInfo.ProductName, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock { Text = AboutInfo.Tagline, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock
        {
            Text = $"Sürüm {AboutInfo.Version}  ·  {AboutInfo.LicenseName}  ·  Açık kaynak",
            FontSize = 12,
            Foreground = Ink,
            Opacity = 0.75,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        content.Children.Add(Card(
            UiStrings.T("Açık kaynak"),
            [new TextBlock { Text = AboutInfo.OpenSourceText, TextWrapping = TextWrapping.Wrap, Foreground = Ink }]));
        content.Children.Add(Card(
            UiStrings.T("Sembol kaynakları"),
            [new TextBlock { Text = AboutInfo.SymbolsText, TextWrapping = TextWrapping.Wrap, Foreground = Ink }]));
        content.Children.Add(Card(
            UiStrings.T("Kullanılan açık kaynak bileşenler"),
            [.. AboutInfo.Components.Select(item => (Control)new TextBlock { Text = $"{item.Name} — {item.License}", FontSize = 12, Foreground = Ink })]));

        var license = new Button { Content = UiStrings.T("Lisansı göster"), Padding = new Thickness(14, 5), HorizontalAlignment = HorizontalAlignment.Left };
        license.Click += (_, _) => ShowLicense();
        var close = new Button { Content = UiStrings.T("Kapat"), IsDefault = true, IsCancel = true, Padding = new Thickness(18, 5), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetColumn(close, 1);
        buttons.Children.Add(license);
        buttons.Children.Add(close);
        content.Children.Add(buttons);
        content.Children.Add(new TextBlock { Text = AboutInfo.WarrantyText, FontSize = 11, Foreground = Ink, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center });

        Content = new ScrollViewer { Content = content };
    }

    private static Control Logo()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Ake.Editor/Assets/logo-256.png"));
            return new Image { Source = new Bitmap(stream), Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center };
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or InvalidOperationException)
        {
            return new TextBlock { Text = "⚡", FontSize = 64, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }

    private static Border Card(string title, IReadOnlyList<Control> lines)
    {
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Ink });
        panel.Children.AddRange(lines);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E8E0CC")),
            BorderBrush = Ink,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8),
            Child = panel,
        };
    }

    private async void ShowLicense()
    {
        var text = new TextBox
        {
            Text = AboutInfo.LicenseText(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, monospace"),
            FontSize = 12,
        };
        var window = new Window
        {
            Title = AboutInfo.LicenseName,
            Width = 760,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = text,
        };
        await window.ShowDialog(this);
    }
}
