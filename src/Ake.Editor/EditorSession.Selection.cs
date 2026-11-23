using Ake.Core;
using Ake.Core.Editing;
using Ake.Core.Macros;
using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Çoklu seçim, toplu düzenleme ve pano (kopyala/yapıştır), oturum tarafı (ADR-0026).</summary>
public sealed partial class EditorSession
{
    private Macro? _clipboard;
    private (double X, double Y) _clipboardOrigin;

    /// <summary>Panoda kopyalanmış bir şey var mı.</summary>
    public bool CanPaste => _clipboard is not null;

    /// <summary>Kutusu tamamen alanın içinde kalan semboller (pencere seçimi).</summary>
    public IReadOnlyList<PlacedBox> BoxesIn(int pageIndex, Box area) =>
        [.. BoxesFor(pageIndex).Where(box =>
            box.Bounds.X >= area.X && box.Bounds.Right <= area.Right
            && box.Bounds.Y >= area.Y && box.Bounds.Bottom <= area.Bottom)];

    /// <summary>Seçili sembolleri yapışma adımına yuvarlanmış mesafe kadar birlikte taşır; tek adım.</summary>
    public Result MoveMany(IReadOnlyCollection<PlacementId> placements, double deltaX, double deltaY)
    {
        if (Project is null)
        {
            return NoProject();
        }

        var (dx, dy) = Snap(deltaX, deltaY);
        return placements.Count == 0 || (dx == 0 && dy == 0)
            ? Result.Success()
            : ExecuteIfAny(Selection.Move(Project, placements, dx, dy));
    }

    /// <summary>Seçili sembollerin her birini kendi ucu çevresinde 90 derece döndürür; tek adım.</summary>
    public Result RotateMany(IReadOnlyCollection<PlacementId> placements) =>
        Project is null ? NoProject() : ExecuteIfAny(Selection.Rotate(Project, placements));

    /// <summary>
    /// Seçili sembolleri siler; sembolü kalmayan cihaz bağlantılarıyla birlikte silinir. Tek adımda geri alınır.
    /// </summary>
    public Result DeleteMany(IReadOnlyCollection<PlacementId> placements) =>
        Project is null ? NoProject() : ExecuteIfAny(Selection.Delete(Project, placements));

    /// <summary>Seçimi panoya alır (makro olarak); model değişmez.</summary>
    public Result Copy(int pageIndex, IReadOnlyCollection<PlacementId> placements)
    {
        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return Result.Failure(page.Error!);
        }

        var captured = MacroService.Capture(Project!, page.Value.Id, "Pano", placements);
        if (captured.IsFailure)
        {
            return Result.Failure(captured.Error!);
        }

        var chosen = Project!.Placements.Where(p => placements.Contains(p.Id)).ToList();
        _clipboard = captured.Value;
        _clipboardOrigin = (chosen.Min(p => p.X), chosen.Min(p => p.Y));
        return Result.Success();
    }

    /// <summary>
    /// Panodakini yeni tag ve kimliklerle yapıştırır; tek adımda geri alınır. Konum verilmezse kopyalanan
    /// yerin 20 mm sağ altına. Yapıştırılan yerleşimler döner (seçim için).
    /// </summary>
    public Result<IReadOnlyList<PlacementId>> Paste(int pageIndex, double? x = null, double? y = null)
    {
        if (_clipboard is null)
        {
            return Result.Failure<IReadOnlyList<PlacementId>>(new Error("pano.bos", "Panoda yapıştırılacak bir şey yok."));
        }

        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PlacementId>>(page.Error!);
        }

        var (sx, sy) = Snap(x ?? _clipboardOrigin.X + 20, y ?? _clipboardOrigin.Y + 20);
        var insert = MacroService.Insert(Project!, _clipboard, page.Value.Id, sx, sy);
        if (insert.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PlacementId>>(insert.Error!);
        }

        var before = Project!.Placements.Select(p => p.Id).ToHashSet();
        var executed = Execute(insert.Value);
        return executed.IsFailure
            ? Result.Failure<IReadOnlyList<PlacementId>>(executed.Error!)
            : Result.Success<IReadOnlyList<PlacementId>>([.. Project.Placements.Select(p => p.Id).Where(id => !before.Contains(id))]);
    }
}
