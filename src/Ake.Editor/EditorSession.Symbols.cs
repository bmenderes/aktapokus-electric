using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Blok editörü için oturum tarafı: başlangıç sembolü ve projeye kaydetme.</summary>
public sealed partial class EditorSession
{
    /// <summary>Yerleşimin function'ı; blok editörü sembolü buna atayabilir.</summary>
    public Function? FunctionOf(PlacementId placementId)
    {
        if (Project?.Placements.FirstOrDefault(placement => placement.Id == placementId) is not { } found)
        {
            return null;
        }

        return Project.Devices.SelectMany(device => device.Functions).FirstOrDefault(function => function.Id == found.FunctionId);
    }

    /// <summary>
    /// Blok editörünün başlayacağı sembol. Function'a atanmış kullanıcı sembolü varsa onun kendisi
    /// (aynı kimlikle; kaydetmek onu günceller); yoksa yerleşik parametrik sembolün kopyası (yeni
    /// kimlikle; kaydetmek yeni bir sembol ekler). Function yoksa boş taslak.
    /// </summary>
    public SymbolDefinition? StartingSymbolFor(Function? function)
    {
        if (function is null || Project is null)
        {
            return null;
        }

        if (function.SymbolId is { } id && Project.FindSymbol(id) is { } existing)
        {
            return existing;
        }

        var builtIn = SymbolLibrary.For(function);
        return new SymbolDefinition(
            SymbolId.New(),
            $"{function.Kind} ({function.ConnectionPoints.Count} uç)",
            SymbolShapesFrom(builtIn),
            [.. builtIn.Pins.Select(pin => new SymbolPin(pin.X, pin.Y, (int)pin.DirectionX, (int)pin.DirectionY))],
            "Blok editörü (yerleşik sembolden)");
    }

    internal static List<SymbolShape> SymbolShapesFrom(Symbol symbol)
    {
        var shapes = new List<SymbolShape>();
        foreach (var item in symbol.Items)
        {
            switch (item)
            {
                case LineItem line:
                    shapes.Add(new SymbolLine(line.X1, line.Y1, line.X2, line.Y2, line.StrokeMm));
                    break;
                case RectItem rect:
                    shapes.Add(new SymbolRect(rect.X, rect.Y, rect.Width, rect.Height, rect.StrokeMm));
                    break;
                case CircleItem circle:
                    shapes.Add(new SymbolCircle(circle.X, circle.Y, circle.Radius, circle.StrokeMm, circle.Filled));
                    break;
                case TextItem text:
                    shapes.Add(new SymbolText(text.X, text.Y, text.Text, text.SizeMm, text.Align, text.RotationDeg));
                    break;
            }
        }

        return shapes;
    }

    /// <summary>
    /// Sembolü projeye kaydeder; istenirse aynı adımda bir function'a atar. İkisi tek command'dır,
    /// birlikte geri alınır. Uç sayısı function'a uymazsa hiçbiri yapılmaz.
    /// </summary>
    public Result SaveSymbol(SymbolDefinition symbol, FunctionId? assignTo)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        ICommand command = assignTo is { } functionId
            ? new Transaction($"Sembol kaydedildi ve atandı: {symbol.Name}", [new PutSymbol(symbol), new AssignSymbol(functionId, symbol.Id)])
            : new PutSymbol(symbol);
        return Execute(command);
    }
}
