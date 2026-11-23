using Ake.Core;
using Ake.Core.Model;
using Ake.Render;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Ake.Editor;

/// <summary>Sağ tık menüsü ve çapraz referans bağlantıları (Faz 6.2, madde 12).</summary>
public partial class MainWindow
{
    /// <summary>
    /// Verilen sayfa noktasındaki çapraz referans bağlantısının hedef sayfa sırası (kontak ayna tablosu, uç notu,
    /// kontağın altındaki bobin yeri); yoksa <c>null</c>.
    /// </summary>
    private int? LinkTargetAt(SceneView view, double x, double y) =>
        view.Scene?.Items.OfType<LinkItem>().LastOrDefault(link =>
            x >= link.X && x <= link.X + link.Width && y >= link.Y && y <= link.Y + link.Height) is { } found
            ? _session.PageIndexOfAnchor(found.Target)
            : null;

    // Sağ tık: tıklanan nesne seçilir (seçili değilse), sonra o nesneye uygun komutlar menüde açılır. Komutlar şerit ve
    // kısayollarla aynıdır; yapılamayanlar soluk görünür.
    private void ShowContextMenu(SceneView view, int page, double x, double y)
    {
        if (_wireMode || _session.Project is null)
        {
            return;
        }

        _contextPoint = (x, y);

        var symbol = _session.HitTest(page, x, y);
        if (symbol is not null)
        {
            if (_selection.All(box => box.Placement != symbol.Placement))
            {
                Select(symbol);
            }
        }
        else if (_session.GraphicAt(page, x, y) is { } graphic)
        {
            if (_graphicSelection.All(box => box.Graphic != graphic.Graphic))
            {
                SetSelection([], null, [graphic]);
            }
        }
        else if (_session.WireAt(page, x, y) is { } wire)
        {
            if (_wireSelection.All(w => w.Connection != wire.Connection))
            {
                SetSelection([], null, [], [wire]);
            }
        }
        else
        {
            Select(null);
        }

        var menu = new ContextMenu { Placement = PlacementMode.Pointer };

        if (LinkTargetAt(view, x, y) is { } target)
        {
            AddItem(menu, "Hedef sayfaya git", null, true, () => OpenPage(target));
            menu.Items.Add(new Separator());
        }

        if (_selection.Count > 0)
        {
            AddCommand(menu, EditorCommands.Properties);
            AddCommand(menu, EditorCommands.ChangeSymbol);
            AddCommand(menu, EditorCommands.DrawWire);
            menu.Items.Add(new Separator());
            foreach (var id in new[] { EditorCommands.Rotate, EditorCommands.Copy, EditorCommands.Cut, EditorCommands.Delete })
            {
                AddCommand(menu, id);
            }

            menu.Items.Add(new Separator());
            AddCommand(menu, EditorCommands.EditSymbol);
            if (StripToEdit() is not null)
            {
                AddCommand(menu, EditorCommands.TerminalStrip);
            }

            AddCommand(menu, EditorCommands.Potentials);
            AddCommand(menu, EditorCommands.MacroFromSelection);
        }
        else if (_graphicSelection.Count > 0 || _wireSelection.Count > 0)
        {
            if (SelectedText is not null)
            {
                AddCommand(menu, EditorCommands.Properties, "Yazıyı düzenle");
            }

            // Tek tel seçiliyse köşe noktaları: elle yönlendir ya da otomatiğe döndür.
            if (_wireSelection is [{ } onlyWire] && _graphicSelection.Count == 0 && _session.EndsOf(page, onlyWire.Connection) is not null)
            {
                AddItem(menu, "Köşe noktaları ver", null, true, () => StartReroute(page, onlyWire));
                AddItem(menu, "Otomatik yönlendir", null, _session.HasRoute(onlyWire.Connection), () => Apply(_session.SetWireRoute(onlyWire.Connection, [])));
                menu.Items.Add(new Separator());
            }

            // Seçili teller için bağlantı türü (renk ve çizgi biçimi): tür listesi, "Türsüz" ve türleri ekleme.
            if (_wireSelection.Count > 0 && _graphicSelection.Count == 0)
            {
                var wires = _wireSelection.Select(wire => wire.Connection).ToList();
                var kinds = new MenuItem { Header = UiStrings.T("Bağlantı türü") };
                foreach (var kind in _session.ConnectionKinds)
                {
                    var item = new MenuItem { Header = $"{kind.Name}  ({kind.Color}, {kind.Style})" };
                    var chosen = kind.Id;
                    item.Click += (_, _) => Apply(_session.SetConnectionKind(wires, chosen));
                    kinds.Items.Add(item);
                }

                if (_session.ConnectionKinds.Count == 0)
                {
                    var add = new MenuItem { Header = UiStrings.T("Standart türleri ekle") };
                    add.Click += (_, _) => AddStandardKindsWithFeedback();
                    kinds.Items.Add(add);
                }
                else
                {
                    kinds.Items.Add(new Separator());
                    var none = new MenuItem { Header = UiStrings.T("Türsüz") };
                    none.Click += (_, _) => Apply(_session.SetConnectionKind(wires, null));
                    kinds.Items.Add(none);
                }

                menu.Items.Add(kinds);
                AddCommand(menu, EditorCommands.Cables, "Tel / kablo damarı…");
                AddCommand(menu, EditorCommands.Potentials);
                menu.Items.Add(new Separator());
            }

            if (_graphicSelection.Count > 0)
            {
                AddCommand(menu, EditorCommands.RecognizeGraphics);
                menu.Items.Add(new Separator());
            }

            AddCommand(menu, EditorCommands.Delete);
        }
        else
        {
            // Kapak sayfasında sağ tık önce proje bilgilerini açar (müşteri, sipariş no, çizim no… kapakta görünen bilgiler).
            var isCover = page >= 0 && page < _session.Project.Pages.Count && _session.Project.Pages[page].Kind == PageKind.Cover;
            if (isCover)
            {
                AddCommand(menu, EditorCommands.ProjectProperties, "Proje özellikleri… (kapak bilgileri)");
                menu.Items.Add(new Separator());
            }

            if (_session.IsSchematicPage(page))
            {
                AddItem(menu, EditorCommands.Get(EditorCommands.InsertSymbol).Label, _keys.GestureOf(EditorCommands.InsertSymbol), true, () => _ = InsertSymbolAt(page, x, y));
                AddCommand(menu, EditorCommands.DrawWire);
                AddItem(menu, EditorCommands.Get(EditorCommands.AddText).Label, _keys.GestureOf(EditorCommands.AddText), true, () => _ = AddTextAt(page, x, y));
                AddCommand(menu, EditorCommands.InsertMacro);
                if (_session.PageHasGraphics(page))
                {
                    AddCommand(menu, EditorCommands.RecognizeGraphics);
                }

                menu.Items.Add(new Separator());
            }

            foreach (var id in new[] { EditorCommands.Paste, EditorCommands.SelectAll, EditorCommands.Fit })
            {
                AddCommand(menu, id);
            }

            menu.Items.Add(new Separator());
            AddCommand(menu, EditorCommands.NewPage);
            AddCommand(menu, EditorCommands.InsertPage);
            AddCommand(menu, EditorCommands.PageProperties);
            AddCommand(menu, EditorCommands.TitleBlockEditor);
            if (!isCover)
            {
                AddCommand(menu, EditorCommands.ProjectProperties);
            }
        }

        menu.Open(view);
    }

    // Sayfa listesinde sağ tık menüsü. Satıra tıklandıysa araya ekleme ve numara değiştirme o sayfa için çalışır.
    private void ShowPageMenu(int? pageIndex)
    {
        if (_session.Project is null)
        {
            return;
        }

        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        AddCommand(menu, EditorCommands.NewPage);
        AddItem(menu, "Araya sayfa ekle (3.1)", null, pageIndex is not null, () =>
        {
            var inserted = _session.InsertPageAfter(pageIndex!.Value);
            if (inserted.IsFailure)
            {
                Apply(Result.Failure(inserted.Error!));
                return;
            }

            Apply(Result.Success());
            OpenPage(inserted.Value);
        });
        menu.Items.Add(new Separator());
        var count = _session.PageLabels.Count;
        AddItem(menu, "Sayfayı en başa taşı", null, pageIndex is > 0, () => MovePage(pageIndex!.Value, 0));
        AddItem(menu, "Sayfayı yukarı taşı", null, pageIndex is > 0, () => MovePage(pageIndex!.Value, pageIndex.Value - 1));
        AddItem(menu, "Sayfayı aşağı taşı", null, pageIndex is { } at && at < count - 1, () => MovePage(pageIndex!.Value, pageIndex.Value + 1));
        menu.Items.Add(new Separator());
        AddItem(menu, EditorCommands.Get(EditorCommands.PageProperties).Label, _keys.GestureOf(EditorCommands.PageProperties), pageIndex is not null, () => _ = ShowPageProperties(pageIndex!.Value));
        AddItem(menu, "Sayfa numarasını değiştir…", null, pageIndex is not null, () => _ = ChangePageNumber(pageIndex!.Value));
        AddCommand(menu, EditorCommands.RenumberPages);
        menu.Items.Add(new Separator());
        AddCommand(menu, EditorCommands.TitleBlockEditor);
        AddCommand(menu, EditorCommands.ProjectProperties);
        menu.Open(PageTree);
    }

    private async Task ChangePageNumber(int pageIndex)
    {
        if (_session.Project is null || pageIndex < 0 || pageIndex >= _session.Project.Pages.Count)
        {
            return;
        }

        var page = _session.Project.Pages[pageIndex];
        var pageId = page.Id;
        var number = await new TextPromptDialog("Sayfa numarası", page.Number).ShowDialog<string?>(this);
        if (number is null)
        {
            return;
        }

        var result = _session.SetPageNumberAt(pageIndex, number);
        Apply(result);
        if (result.IsSuccess && _session.IndexOfPage(pageId) is >= 0 and var movedIndex)
        {
            OpenPage(movedIndex);
            SyncPageList(movedIndex, _session.PageLabels);
        }
    }

    // Düzenlenecek klemens şeridi: seçili sembol bir klemense aitse onun şeridi; değilse Klemensler listesinde seçili şerit satırı.
    private DeviceId? StripToEdit()
    {
        if (_selected is { } symbol && _session.StripOfPlacement(symbol.Placement) is { } fromSymbol)
        {
            return fromSymbol;
        }

        return TerminalList.SelectedItem is NavigatorRow { Device: { } device } && _session.TerminalStripOf(device) is not null ? device : null;
    }

    private async Task ShowTerminalStrip()
    {
        if (StripToEdit() is not { } strip || _session.TerminalStripOf(strip) is not { } data)
        {
            return;
        }

        var edits = await new TerminalStripWindow(data).ShowDialog<TerminalStripEdits?>(this);
        if (edits is not null)
        {
            Apply(_session.ApplyTerminalStrip(edits));
        }
    }

    private void AddCommand(ContextMenu menu, string id, string? label = null)
    {
        var command = EditorCommands.Get(id);
        AddItem(menu, label ?? command.Label, _keys.GestureOf(id), CanRun(id), () => Ribbon.Run(id));
    }

    private static void AddItem(ContextMenu menu, string header, string? gesture, bool enabled, Action run)
    {
        var item = new MenuItem { Header = UiStrings.T(header), InputGesture = gesture is null ? null : Avalonia.Input.KeyGesture.Parse(gesture), IsEnabled = enabled };
        item.Click += (_, _) => run();
        menu.Items.Add(item);
    }
}
