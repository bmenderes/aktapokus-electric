using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>
/// Birden çok command'ı tek adım yapar: ya hepsi uygulanır ya hiçbiri. Undo yığınında tek kayıttır.
/// </summary>
public sealed class Transaction(string description, IEnumerable<ICommand> commands) : ICommand
{
    private readonly List<ICommand> _commands = [.. commands];

    public string Description { get; } = description;

    public IReadOnlyList<ICommand> Commands => _commands;

    public Result Execute(Project project)
    {
        for (var i = 0; i < _commands.Count; i++)
        {
            var result = _commands[i].Execute(project);
            if (result.IsSuccess)
            {
                continue;
            }

            for (var done = i - 1; done >= 0; done--)
            {
                _commands[done].Undo(project);
            }

            return Result.Failure(new Error(
                result.Error!.Code,
                $"Adım {i + 1}/{_commands.Count} başarısız, hiçbir değişiklik uygulanmadı: {result.Error.Message}"));
        }

        return Result.Success();
    }

    public void Undo(Project project)
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo(project);
        }
    }
}

/// <summary>
/// Asıl command'ı çalışma anında, modelin o andaki durumuna bakarak kurar. Aynı transaction'da
/// önceki adımın eklediği nesneye atıfta bulunan adımlar (patch) için kullanılır.
/// </summary>
public sealed class DeferredCommand(string description, Func<Project, Result<ICommand>> factory) : ICommand
{
    private ICommand? _inner;

    public string Description => _inner?.Description ?? description;

    public Result Execute(Project project)
    {
        // Redo: ilk çalıştırmada kurulan command yeniden kullanılır. Yeniden kurmak, üretilen
        // kimlikleri değiştirir ve redo ilk çalıştırmayla aynı modeli vermez.
        if (_inner is not null)
        {
            return _inner.Execute(project);
        }

        var created = factory(project);
        if (created.IsFailure)
        {
            return Result.Failure(created.Error!);
        }

        var result = created.Value.Execute(project);
        if (result.IsSuccess)
        {
            _inner = created.Value;
        }

        return result;
    }

    public void Undo(Project project)
    {
        if (_inner is null)
        {
            throw new InvalidOperationException("Çalışmamış command geri alınamaz.");
        }

        _inner.Undo(project);
    }
}

internal static class ListEdit
{
    /// <summary>Koşula uyanları çıkarır; geri koymak için özgün konumlarıyla döner.</summary>
    public static List<(int Index, T Item)> RemoveWhere<T>(List<T> list, Func<T, bool> predicate)
    {
        var removed = new List<(int Index, T Item)>();
        for (var i = 0; i < list.Count; i++)
        {
            if (predicate(list[i]))
            {
                removed.Add((i, list[i]));
            }
        }

        for (var i = removed.Count - 1; i >= 0; i--)
        {
            list.RemoveAt(removed[i].Index);
        }

        return removed;
    }

    /// <summary><see cref="RemoveWhere{T}"/> ile çıkarılanları özgün konumlarına geri koyar.</summary>
    public static void Restore<T>(List<T> list, List<(int Index, T Item)> removed)
    {
        foreach (var (index, item) in removed)
        {
            list.Insert(index, item);
        }
    }
}
