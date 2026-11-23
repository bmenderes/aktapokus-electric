using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Ake.Editor;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "aktapokus-electric");
            var langPath = Path.Combine(dataDir, "dil.json");
            UiStrings.Load(langPath);

            var window = new MainWindow();
            desktop.MainWindow = window;

            try
            {
                window.LanguagePath = langPath;
                window.ApplyLanguage();

                // Panel düzeni kullanıcı profilinde saklanır; kapanırken yazılır, açılışta okunur.
                window.LayoutPath = Path.Combine(dataDir, "layout.json");
                window.LoadLayout();

                // Kısayol profili: değiştirilenler kisayollar.json'da saklanır (docs/kisayollar.md).
                window.KeyMapPath = Path.Combine(dataDir, "kisayollar.json");
                window.LoadKeyMap();

                // Yapay zeka sağlayıcısı ve modeli kullanıcı profilinde saklanır (ADR-0044).
                window.UseAiSettingsFile(Path.Combine(dataDir, "yapay-zeka.json"));

                // Kullanıcının varsayılan kütüphanesi açılır; yoksa oluşturulur ve standart komponent seti (ADR-0035) hemen konur.
                var library = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aktapokus-electric", "library.db");
                Directory.CreateDirectory(Path.GetDirectoryName(library)!);
                var installed = Ake.Library.StandardSymbols.EnsureInstalled(library);
                if (installed.IsSuccess || File.Exists(library))
                {
                    window.OpenLibrary(library);
                }

                // Ek DXF/QElectroTech koleksiyonları pencere açılışını bekletmeden arka planda içe aktarılır.
                if (installed.IsSuccess)
                {
                    var check = Ake.Library.ComponentLibrary.Open(library);
                    if (check.IsSuccess)
                    {
                        int symbolCount;
                        using (var lib = check.Value)
                        {
                            symbolCount = lib.SymbolCount;
                        }

                        if (symbolCount <= Ake.Library.StandardSymbols.All.Count)
                        {
                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    var imported = Ake.Library.StandardSymbols.ImportDiscoveredCollections(library);
                                    if (imported.IsSuccess && imported.Value.Added > 0)
                                    {
                                        Dispatcher.UIThread.Post(() => window.OpenLibrary(library));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Serilog.Log.Warning(ex, "Arka planda sembol koleksiyonu yüklenemedi");
                                }
                            });
                        }
                    }
                }

                // Komut satirindaki ilk arguman proje dosyasidir.
                if (desktop.Args is { Length: > 0 } args)
                {
                    window.OpenProject(args[0]);
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Uygulama başlangıç yüklemesi başarısız oldu");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
