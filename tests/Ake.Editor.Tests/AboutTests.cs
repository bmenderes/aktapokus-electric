namespace Ake.Editor.Tests;

/// <summary>Hakkında bilgisi: ad, açık kaynak, lisans, bileşenler ve komutun şeritte yeri (ADR-0038).</summary>
public class AboutTests
{
    [Fact]
    public void The_product_is_named_and_declared_open_source_with_its_license()
    {
        Assert.Equal("aktapokus-electric", AboutInfo.ProductName);
        Assert.Contains("açık kaynaklı", AboutInfo.OpenSourceText, StringComparison.Ordinal);
        Assert.Contains("Apache License 2.0", AboutInfo.OpenSourceText, StringComparison.Ordinal);
        Assert.Equal("Apache License 2.0", AboutInfo.LicenseName);
        Assert.False(string.IsNullOrWhiteSpace(AboutInfo.Version));
        Assert.Contains("B. Favre", AboutInfo.SymbolsText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_embedded_license_text_is_the_apache_license()
    {
        var text = AboutInfo.LicenseText();

        Assert.Contains("Apache License", text, StringComparison.Ordinal);
        Assert.Contains("Version 2.0", text, StringComparison.Ordinal);
        Assert.True(text.Length > 5000);
    }

    [Fact]
    public void Third_party_components_are_listed_with_open_licenses_only()
    {
        Assert.Contains(AboutInfo.Components, item => item.Name.Contains("Avalonia", StringComparison.Ordinal));
        Assert.Contains(AboutInfo.Components, item => item.Name.Contains("SkiaSharp", StringComparison.Ordinal));
        Assert.All(AboutInfo.Components, item => Assert.Contains(item.License, new[] { "MIT", "Apache-2.0", "SIL Open Font License 1.1" }));
    }

    [Fact]
    public void The_about_command_is_in_the_tools_tab_under_help_and_always_available()
    {
        var command = EditorCommands.Get(EditorCommands.About);
        Assert.Equal("Hakkında", command.Label);

        var tab = EditorCommands.Main.Tabs.Single(candidate => candidate.Label == "Araçlar");
        var help = tab.Groups.Single(group => group.Label == "Yardım");
        Assert.Contains(help.Items, item => item.CommandId == EditorCommands.About);
    }
}
