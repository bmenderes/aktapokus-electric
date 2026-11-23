using Ake.Core.Model;
using Avalonia.Controls;

namespace Ake.Editor;

/// <summary>Uç özelliklerinin (ADR-0033) arayüz metinleri ve açılır liste yardımcıları; özellik penceresi ile blok editörü paylaşır.</summary>
public static class PointAttributeLabels
{
    public static IReadOnlyList<(PinKind Value, string Label)> Kinds { get; } =
    [
        (PinKind.Unspecified, "—"),
        (PinKind.Power, "Güç"),
        (PinKind.Signal, "Sinyal"),
        (PinKind.Communication, "Haberleşme"),
        (PinKind.ProtectiveEarth, "Toprak (PE)"),
        (PinKind.Shield, "Ekran"),
        (PinKind.Pneumatic, "Pnömatik (hava)"),
    ];

    public static IReadOnlyList<(ConnectorType Value, string Label)> Connectors { get; } =
    [
        (ConnectorType.Unspecified, "—"),
        (ConnectorType.Screw, "Vidalı"),
        (ConnectorType.PushIn, "Push-in"),
        (ConnectorType.Faston, "Faston"),
        (ConnectorType.M8, "M8"),
        (ConnectorType.M12, "M12"),
        (ConnectorType.Rj45, "RJ45 (Ethernet)"),
        (ConnectorType.DSub, "D-Sub"),
        (ConnectorType.Thread, "Dişli (G, M, NPT)"),
        (ConnectorType.Other, "Diğer"),
    ];

    public static IReadOnlyList<(ContactType Value, string Label)> Contacts { get; } =
    [
        (ContactType.Unspecified, "—"),
        (ContactType.NormallyOpen, "NO (açık)"),
        (ContactType.NormallyClosed, "NC (kapalı)"),
        (ContactType.Changeover, "CO (değiştirmeli)"),
    ];

    /// <summary>Açılır listeyi etiketlerle doldurur ve değeri seçer.</summary>
    public static ComboBox Combo<T>(IReadOnlyList<(T Value, string Label)> items, T selected, double width)
        where T : struct, Enum
    {
        var combo = new ComboBox { ItemsSource = items.Select(item => item.Label).ToList(), Width = width };
        combo.SelectedIndex = Math.Max(0, items.ToList().FindIndex(item => item.Value.Equals(selected)));
        return combo;
    }

    /// <summary>Açılır listede seçili değer; seçim yoksa varsayılan.</summary>
    public static T Selected<T>(ComboBox combo, IReadOnlyList<(T Value, string Label)> items)
        where T : struct, Enum =>
        combo.SelectedIndex >= 0 && combo.SelectedIndex < items.Count ? items[combo.SelectedIndex].Value : default;
}
