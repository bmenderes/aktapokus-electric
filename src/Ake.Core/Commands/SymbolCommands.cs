using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>
/// Sembol tanımını projeye koyar: aynı kimlikli sembol varsa yerine geçer, yoksa sona eklenir. Yerine
/// geçen sembol, onu kullanan her function'a uymalıdır (uç sayısı).
/// </summary>
public sealed class PutSymbol(SymbolDefinition symbol) : ICommand
{
    private SymbolDefinition? _previous;
    private int _index;

    public string Description => $"Sembol kaydedildi: {symbol.Name}";

    public Result Execute(Project project)
    {
        if (symbol.Check() is { } invalid)
        {
            return Result.Failure(invalid);
        }

        var symbols = project.SymbolList;
        _index = symbols.FindIndex(existing => existing.Id == symbol.Id);
        if (_index < 0)
        {
            if (project.IdSet().Contains(symbol.Id.Value))
            {
                return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({symbol.Id})."));
            }

            _previous = null;
            symbols.Add(symbol);
            return Result.Success();
        }

        _previous = symbols[_index];
        symbols[_index] = symbol;

        foreach (var device in project.Devices)
        {
            foreach (var function in device.Functions.Where(function => function.SymbolId == symbol.Id))
            {
                if (project.CheckSymbolFits(function, $"-{device.ProductTag}") is { } error)
                {
                    symbols[_index] = _previous;
                    return Result.Failure(error);
                }
            }
        }

        return Result.Success();
    }

    public void Undo(Project project)
    {
        if (_previous is null)
        {
            project.SymbolList.Remove(symbol);
        }
        else
        {
            project.SymbolList[_index] = _previous;
        }
    }
}

/// <summary>
/// Function'ın çizileceği sembolü atar; <c>null</c> yerleşik parametrik sembole döner. Çizimi değiştirir; bağlantılar ve uç
/// adları aynı kalır. Sembolün uç özellikleri (tür, bağlantı tipi, kontak türü), noktada henüz özellik yoksa noktaya geçer
/// (ADR-0033).
/// </summary>
public sealed class AssignSymbol(FunctionId functionId, SymbolId? symbolId) : ICommand
{
    private Function? _function;
    private SymbolId? _previous;
    private List<(ConnectionPoint Point, PointAttributes Previous)> _attributes = [];

    public string Description => symbolId is null ? "Sembol ataması kaldırıldı" : "Sembol atandı";

    public Result Execute(Project project)
    {
        var owner = project.Devices
            .SelectMany(device => device.Functions.Select(function => (device, function)))
            .FirstOrDefault(item => item.function.Id == functionId);
        if (owner.function is null)
        {
            return Result.Failure(new Error("function.yok", $"Function modelde yok ({functionId})."));
        }

        _function = owner.function;
        _previous = owner.function.SymbolId;
        owner.function.SymbolId = symbolId;

        var error = project.CheckSymbolFits(owner.function, $"-{owner.device.ProductTag}");
        if (error is not null)
        {
            owner.function.SymbolId = _previous;
            return Result.Failure(error);
        }

        _attributes = [];
        if (symbolId is { } id && project.FindSymbol(id) is { } symbol)
        {
            for (var i = 0; i < symbol.Pins.Count && i < owner.function.ConnectionPoints.Count; i++)
            {
                var point = owner.function.ConnectionPoints[i];
                if (point.Attributes.IsEmpty && !symbol.Pins[i].Attributes.IsEmpty)
                {
                    _attributes.Add((point, point.Attributes));
                    point.Attributes = symbol.Pins[i].Attributes;
                }
            }
        }

        return Result.Success();
    }

    public void Undo(Project project)
    {
        _function!.SymbolId = _previous;
        foreach (var (point, previous) in _attributes)
        {
            point.Attributes = previous;
        }
    }
}
