using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>
/// Sayfalara serbest grafik ekler. Binlerce nesne tek komuttur (içe aktarılan bir sayfa tek geri alma
/// adımıdır); biri geçersizse hiçbiri eklenmez.
/// </summary>
public sealed class AddGraphics(IEnumerable<PageGraphic> graphics) : ICommand
{
    private readonly List<PageGraphic> _graphics = [.. graphics];

    public string Description => $"Serbest grafik eklendi: {_graphics.Count}";

    public Result Execute(Project project)
    {
        var used = project.IdSet();
        var pages = project.Pages.Select(page => page.Id).ToHashSet();
        foreach (var graphic in _graphics)
        {
            if (!used.Add(graphic.Id.Value))
            {
                return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({graphic.Id})."));
            }

            if (!pages.Contains(graphic.PageId))
            {
                return Result.Failure(new Error("grafik.sayfa_yok", $"Grafiğin sayfası modelde yok ({graphic.PageId})."));
            }

            if (graphic.Shape.Check() is { } problem)
            {
                return Result.Failure(new Error("grafik.gecersiz", $"Grafik geçersiz: {problem}."));
            }
        }

        project.GraphicList.AddRange(_graphics);
        return Result.Success();
    }

    public void Undo(Project project)
    {
        var ids = _graphics.Select(graphic => graphic.Id).ToHashSet();
        project.GraphicList.RemoveAll(graphic => ids.Contains(graphic.Id));
    }
}

/// <summary>Grafikleri sayfadan kaldırır. Geri alınınca aynı sıralarına döner.</summary>
public sealed class RemoveGraphics(IEnumerable<GraphicId> ids) : ICommand
{
    private readonly HashSet<GraphicId> _ids = [.. ids];
    private List<(int Index, PageGraphic Graphic)> _removed = [];

    public string Description => $"Serbest grafik silindi: {_ids.Count}";

    public Result Execute(Project project)
    {
        var known = project.Graphics.Select(graphic => graphic.Id).ToHashSet();
        foreach (var id in _ids)
        {
            if (!known.Contains(id))
            {
                return Result.Failure(new Error("grafik.yok", $"Grafik modelde yok ({id})."));
            }
        }

        _removed = [.. project.GraphicList
            .Select((graphic, index) => (index, graphic))
            .Where(item => _ids.Contains(item.graphic.Id))];
        project.GraphicList.RemoveAll(graphic => _ids.Contains(graphic.Id));
        return Result.Success();
    }

    public void Undo(Project project)
    {
        foreach (var (index, graphic) in _removed)
        {
            project.GraphicList.Insert(Math.Min(index, project.GraphicList.Count), graphic);
        }
    }
}

/// <summary>
/// Grafiklerin şeklini değiştirir (taşıma, yazı düzenleme, renk). Her grafik için yeni şekil verilir;
/// hepsi tek adımda uygulanır ve geri alınır.
/// </summary>
public sealed class ReplaceGraphicShapes(IReadOnlyDictionary<GraphicId, GraphicShape> shapes) : ICommand
{
    private List<(PageGraphic Graphic, GraphicShape Previous)> _previous = [];

    public string Description => $"Serbest grafik değiştirildi: {shapes.Count}";

    public Result Execute(Project project)
    {
        var byId = project.Graphics.ToDictionary(graphic => graphic.Id);
        foreach (var (id, shape) in shapes)
        {
            if (!byId.ContainsKey(id))
            {
                return Result.Failure(new Error("grafik.yok", $"Grafik modelde yok ({id})."));
            }

            if (shape.Check() is { } problem)
            {
                return Result.Failure(new Error("grafik.gecersiz", $"Grafik geçersiz: {problem}."));
            }
        }

        _previous = [];
        foreach (var (id, shape) in shapes)
        {
            var graphic = byId[id];
            _previous.Add((graphic, graphic.Shape));
            graphic.Shape = shape;
        }

        return Result.Success();
    }

    public void Undo(Project project)
    {
        foreach (var (graphic, previous) in _previous)
        {
            graphic.Shape = previous;
        }
    }
}
