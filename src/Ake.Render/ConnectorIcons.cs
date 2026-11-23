using Ake.Core.Model;

namespace Ake.Render;

/// <summary>
/// Bağlantı tipinin (ADR-0033) ucun üstüne çizilen simgesi: her tip kendi biçiminde gösterilir (RJ45 jak, M8/M12 halka, D-Sub yamuk,
/// vida başı, push-in kare, faston dil). Simge ucun merkezindedir ve sade uç halkasının yerini alır. Blok editörü ve şema çizimi
/// aynı simgeyi kullanır.
/// </summary>
public static class ConnectorIcons
{
    /// <summary>Simgenin yaklaşık yarıçapı (mm): ilk uç halkası ve seçim kutusu buna göre ayarlanır.</summary>
    public static double RadiusOf(ConnectorType connector) => connector switch
    {
        ConnectorType.Unspecified => 0.6,
        ConnectorType.Rj45 => 2.6,
        ConnectorType.M12 => 2.2,
        ConnectorType.M8 => 1.6,
        ConnectorType.DSub => 2.4,
        _ => 1.3,
    };

    /// <summary>Kısa ad (simgenin yanına yazılır); sade uç için boş.</summary>
    public static string ShortName(ConnectorType connector) => connector switch
    {
        ConnectorType.Rj45 => "RJ45",
        ConnectorType.M8 => "M8",
        ConnectorType.M12 => "M12",
        ConnectorType.DSub => "D-SUB",
        ConnectorType.PushIn => "PUSH-IN",
        ConnectorType.Faston => "FASTON",
        ConnectorType.Other => "?",
        _ => string.Empty,
    };

    /// <summary>Simgenin sahne öğeleri; merkez (<paramref name="x"/>, <paramref name="y"/>) ucun kendisidir.</summary>
    public static IReadOnlyList<SceneItem> Items(ConnectorType connector, double x, double y)
    {
        const double stroke = 0.2;
        switch (connector)
        {
            case ConnectorType.Screw:
                return
                [
                    new CircleItem(x, y, 1.1, stroke),
                    new LineItem(x - 0.75, y - 0.75, x + 0.75, y + 0.75, stroke),
                    new LineItem(x - 0.75, y + 0.75, x + 0.75, y - 0.75, stroke),
                ];
            case ConnectorType.PushIn:
                return [new RectItem(x - 1.2, y - 1.2, 2.4, 2.4, stroke), new CircleItem(x, y, 0.35, stroke, Filled: true)];
            case ConnectorType.Faston:
                return [new RectItem(x - 0.6, y - 1.6, 1.2, 3.2, stroke), new LineItem(x - 0.6, y - 0.6, x + 0.6, y - 0.6, stroke)];
            case ConnectorType.M8:
                return [new CircleItem(x, y, 1.6, stroke), new CircleItem(x, y, 0.5, stroke, Filled: true)];
            case ConnectorType.M12:
                return [new CircleItem(x, y, 2.2, stroke), new CircleItem(x, y, 1.3, stroke), new CircleItem(x, y, 0.4, stroke, Filled: true)];
            case ConnectorType.Rj45:
                {
                    var items = new List<SceneItem>
                {
                    new RectItem(x - 2.2, y - 1.7, 4.4, 3.4, stroke),
                    new RectItem(x - 0.8, y + 0.3, 1.6, 1.4, stroke),
                };
                    for (var tooth = 0; tooth < 4; tooth++)
                    {
                        var tx = x - 1.35 + (tooth * 0.9);
                        items.Add(new LineItem(tx, y - 1.2, tx, y - 0.1, 0.15));
                    }

                    return items;
                }

            case ConnectorType.DSub:
                return
                [
                    new LineItem(x - 2.2, y - 1.3, x + 2.2, y - 1.3, stroke),
                    new LineItem(x - 1.7, y + 1.3, x + 1.7, y + 1.3, stroke),
                    new LineItem(x - 2.2, y - 1.3, x - 1.7, y + 1.3, stroke),
                    new LineItem(x + 2.2, y - 1.3, x + 1.7, y + 1.3, stroke),
                ];
            case ConnectorType.Other:
                return
                [
                    new LineItem(x, y - 1.3, x + 1.3, y, stroke),
                    new LineItem(x + 1.3, y, x, y + 1.3, stroke),
                    new LineItem(x, y + 1.3, x - 1.3, y, stroke),
                    new LineItem(x - 1.3, y, x, y - 1.3, stroke),
                ];
            default:
                return [new CircleItem(x, y, 0.6, stroke)];
        }
    }
}
