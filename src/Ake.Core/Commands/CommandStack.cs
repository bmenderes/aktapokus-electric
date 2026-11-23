using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>
/// Modeli değiştiren tek yol (ADR-0001). <see cref="Execute"/> ön koşul sağlanmıyorsa modeli hiç
/// değiştirmeden başarısız döner; başarılıysa geri almak için gerekeni kendi içinde tutar.
/// </summary>
public interface ICommand
{
    /// <summary>Audit log'a yazılan özet. Çalıştırıldıktan sonra okunur.</summary>
    string Description { get; }

    Result Execute(Project project);

    /// <summary>Yalnızca başarıyla çalışmış command'da çağrılır; başarısız olamaz.</summary>
    void Undo(Project project);
}

public enum ChangeSource
{
    User,
    Ai,
    Import,
}

public enum AuditAction
{
    Execute,
    Undo,
    Redo,
}

public sealed record AuditEntry(
    DateTimeOffset At,
    ChangeSource Source,
    string Actor,
    AuditAction Action,
    string Summary);

/// <summary>Command'ları çalıştırır, undo/redo yığınını ve audit kaydını tutar.</summary>
public sealed class CommandStack(Project project, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Stack<Entry> _undo = new();
    private readonly Stack<Entry> _redo = new();
    private readonly List<AuditEntry> _audit = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Kayıt silinmez; undo ve redo ayrı eylem olarak eklenir.</summary>
    public IReadOnlyList<AuditEntry> Audit => _audit;

    public Result Execute(ICommand command, ChangeSource source, string actor)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);

        var result = command.Execute(project);
        if (result.IsFailure)
        {
            return result;
        }

        var entry = new Entry(command, source, actor);
        _undo.Push(entry);
        _redo.Clear();
        Record(entry, AuditAction.Execute);
        return result;
    }

    public Result Undo()
    {
        if (!_undo.TryPop(out var entry))
        {
            return Result.Failure(new Error("komut.geri_alinacak_yok", "Geri alınacak işlem yok."));
        }

        entry.Command.Undo(project);
        _redo.Push(entry);
        Record(entry, AuditAction.Undo);
        return Result.Success();
    }

    public Result Redo()
    {
        if (!_redo.TryPop(out var entry))
        {
            return Result.Failure(new Error("komut.yinelenecek_yok", "Yinelenecek işlem yok."));
        }

        var result = entry.Command.Execute(project);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Geri alınmış command yeniden çalıştırılamadı: {result.Error!.Code}");
        }

        _undo.Push(entry);
        Record(entry, AuditAction.Redo);
        return Result.Success();
    }

    private void Record(Entry entry, AuditAction action) =>
        _audit.Add(new AuditEntry(_time.GetUtcNow(), entry.Source, entry.Actor, action, entry.Command.Description));

    private sealed record Entry(ICommand Command, ChangeSource Source, string Actor);
}
