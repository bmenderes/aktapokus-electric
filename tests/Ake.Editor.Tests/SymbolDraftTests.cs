using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor.Tests;

public class SymbolDraftTests
{
    private static SymbolDraft LampDraft()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Pin, PinDirection = (0, -1) };
        draft.Gesture(0, 0, 0, 0);
        draft.PinDirection = (0, 1);
        draft.Gesture(0, 15, 0, 15);
        draft.Tool = SymbolTool.Circle;
        draft.Gesture(0, 7.5, 5, 7.5);
        draft.Tool = SymbolTool.Line;
        draft.Gesture(0, 0, 0, 2.5);
        return draft;
    }

    [Fact]
    public void Drawing_tools_add_shapes_snapped_to_the_symbol_grid()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Line };

        draft.Gesture(0.4, -0.3, 9.6, 5.1);
        draft.Tool = SymbolTool.Rect;
        draft.Gesture(6, 6, -4, -1);
        draft.Tool = SymbolTool.Circle;
        draft.Gesture(0, 0, 3, 4);
        draft.Text = " M ";
        draft.Tool = SymbolTool.Text;
        draft.Gesture(1, 1, 1.1, 1.2);

        Assert.Equal(
            [
                new SymbolLine(0, 0, 10, 5),
                new SymbolRect(-5, 0, 10, 5),
                new SymbolCircle(0, 0, 5),
                new SymbolText(0, 0, "M"),
            ],
            draft.Shapes);
        Assert.True(draft.IsDirty);
    }

    [Fact]
    public void Zero_size_gestures_add_nothing()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Line };

        draft.Gesture(1, 1, 1.2, 0.9);
        draft.Tool = SymbolTool.Rect;
        draft.Gesture(0, 0, 10, 0);
        draft.Text = "  ";
        draft.Tool = SymbolTool.Text;
        draft.Gesture(0, 0, 5, 5);

        Assert.Empty(draft.Shapes);
        Assert.False(draft.CanUndo);
    }

    [Fact]
    public void Pins_are_numbered_in_order_and_cannot_overlap()
    {
        var draft = LampDraft();

        Assert.Equal([new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 15, 0, 1)], draft.Pins);
        draft.Tool = SymbolTool.Pin;
        Assert.Equal("sembol.uc_cakisma", draft.Gesture(0.3, 14.8, 0.3, 14.8).Error!.Code);
        Assert.Equal(2, draft.Pins.Count);
    }

    [Fact]
    public void Selecting_prefers_pins_then_the_last_drawn_shape()
    {
        var draft = LampDraft();

        Assert.Equal(new DraftItem(true, 0), draft.SelectAt(0.5, 0.5));
        Assert.Equal(new DraftItem(false, 1), draft.SelectAt(0, 2));
        Assert.Equal(new DraftItem(false, 0), draft.SelectAt(4, 7.5));
        Assert.Null(draft.SelectAt(20, 20));
        Assert.Null(draft.SelectAt(0, 7.5));
    }

    [Fact]
    public void Select_tool_moves_the_selected_item_by_whole_grid_steps()
    {
        var draft = LampDraft();
        draft.Tool = SymbolTool.Select;
        draft.SelectAt(4, 7.5);

        draft.Gesture(4, 7.5, 9.1, 7.4);

        Assert.Equal(new SymbolCircle(5, 7.5, 5), draft.Shapes[0]);
    }

    [Fact]
    public void Selected_pin_can_be_turned_moved_to_first_and_deleted()
    {
        var draft = LampDraft();

        draft.SelectAt(0, 15);
        draft.TurnSelectedPin();
        Assert.Equal((-1, 0), (draft.Pins[1].DirectionX, draft.Pins[1].DirectionY));

        draft.MakeSelectedPinFirst();
        Assert.Equal(new SymbolPin(0, 15, -1, 0), draft.Pins[0]);
        Assert.Equal(new DraftItem(true, 0), draft.Selected);

        draft.DeleteSelected();
        Assert.Single(draft.Pins);
        Assert.Null(draft.Selected);
    }

    [Fact]
    public void Undo_and_redo_restore_the_draft_step_by_step()
    {
        var draft = LampDraft();
        var shapes = draft.Shapes.ToList();
        var pins = draft.Pins.ToList();

        draft.Undo();
        Assert.Single(draft.Shapes);
        draft.Undo();
        Assert.Empty(draft.Shapes);
        Assert.Equal(2, draft.Pins.Count);

        draft.Redo();
        draft.Redo();
        Assert.Equal(shapes, draft.Shapes);
        Assert.Equal(pins, draft.Pins);
        Assert.False(draft.CanRedo);
    }

    [Fact]
    public void Definition_puts_the_first_pin_at_the_origin()
    {
        var draft = LampDraft();
        draft.SelectAt(0, 15);
        draft.MakeSelectedPinFirst();
        draft.Name = "  Lamba ";

        var definition = draft.ToDefinition().Value;

        Assert.Equal("Lamba", definition.Name);
        Assert.Equal(new SymbolPin(0, 0, 0, 1), definition.Pins[0]);
        Assert.Equal(new SymbolPin(0, -15, 0, -1), definition.Pins[1]);
        Assert.Equal(new SymbolCircle(0, -7.5, 5), definition.Shapes[0]);
        Assert.Equal(draft.Id, definition.Id);
    }

    [Fact]
    public void Draft_without_pins_or_name_cannot_become_a_symbol()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Line };
        draft.Gesture(0, 0, 10, 0);

        Assert.Equal("sembol.gecersiz", draft.ToDefinition().Error!.Code);

        var named = LampDraft();
        named.Name = " ";
        Assert.Equal("sembol.gecersiz", named.ToDefinition().Error!.Code);
    }

    [Fact]
    public void Scene_shows_the_origin_every_shape_and_numbered_pins()
    {
        var scene = LampDraft().ToScene();

        Assert.Equal((80.0, 60.0), (scene.WidthMm, scene.HeightMm));
        Assert.Contains(scene.Items.OfType<CircleItem>(), circle => (circle.X, circle.Y, circle.Radius) == (40, 37.5, 5));
        Assert.Contains(scene.Items.OfType<TextItem>(), text => text.Text == "1");
        Assert.Contains(scene.Items.OfType<TextItem>(), text => text.Text == "2");
        // Uçlar içi boştur; ilk uç (başlangıç noktası) çift halkayla ayrılır.
        Assert.DoesNotContain(scene.Items.OfType<CircleItem>(), circle => circle.Filled);
        Assert.Single(scene.Items.OfType<CircleItem>(), circle => (circle.X, circle.Y, circle.Radius) == (40, 30, 1.2));
    }

    [Fact]
    public void Starting_from_an_existing_symbol_keeps_its_identity()
    {
        var existing = LampDraft().ToDefinition().Value;

        var draft = new SymbolDraft(existing);

        Assert.Equal(existing.Id, draft.Id);
        Assert.Equal(existing.Shapes, draft.Shapes);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void Selected_pin_gets_name_and_attributes_which_reach_the_definition_and_undo()
    {
        var draft = LampDraft();
        draft.Tool = SymbolTool.Select;
        draft.SelectAt(0, 15);
        var attributes = new PointAttributes(PinKind.Signal, ConnectorType.M12, ContactType.NormallyClosed, " A-kod ");
        draft.DeviceName = " Endüktif sensör ";
        draft.TagLetter = "b";

        Assert.True(draft.SetSelectedPin(" OUT ", attributes).IsSuccess);

        var definition = draft.ToDefinition().Value;
        Assert.Equal("OUT", definition.Pins[1].Name);
        Assert.Equal(attributes with { ConnectorCode = "A-kod" }, definition.Pins[1].Attributes);
        Assert.Equal(("Endüktif sensör", "B"), (definition.DeviceName, definition.TagLetter));
        Assert.Contains(draft.ToScene().Items.OfType<Ake.Render.TextItem>(), text => text.Text == "OUT NC  M12");

        draft.Undo();
        Assert.Null(draft.Pins[1].Name);
        Assert.True(draft.Pins[1].Attributes.IsEmpty);
    }

    [Fact]
    public void Two_pins_cannot_share_a_name()
    {
        var draft = LampDraft();
        draft.Tool = SymbolTool.Select;
        draft.SelectAt(0, 0);
        Assert.True(draft.SetSelectedPin("A", default).IsSuccess);
        draft.SelectAt(0, 15);

        Assert.Equal("sembol.uc_ad_tekrar", draft.SetSelectedPin("A", default).Error!.Code);
        Assert.Null(draft.Pins[1].Name);
    }

    [Fact]
    public void An_existing_symbols_name_attributes_and_texts_load_back_into_the_draft()
    {
        var symbol = new SymbolDefinition(
            SymbolId.New(), "Sensör", [new SymbolLine(0, 0, 0, 10)],
            [new SymbolPin(0, 0, 0, -1, "OUT", new PointAttributes(Contact: ContactType.NormallyOpen)), new SymbolPin(0, 10, 0, 1)],
            DeviceName: "Sensör cihazı", TagLetter: "B");

        var draft = new SymbolDraft(symbol);

        Assert.Equal(("Sensör cihazı", "B"), (draft.DeviceName, draft.TagLetter));
        Assert.Equal(symbol.Pins, draft.Pins);
    }

    [Fact]
    public void Canvas_grows_dynamically_for_large_symbols_and_keeps_origin_aligned()
    {
        var wide = new SymbolDefinition(
            SymbolId.New(),
            "LXM62 Sürücü",
            [
                new SymbolRect(-10, 0, 220, 45),
                new SymbolText(95, 22, "DIGITAL INPUTS/OUTPUTS", 3.5),
            ],
            [new SymbolPin(0, 0, 0, -1, "CN1/1"), new SymbolPin(205, 0, 0, -1, "CN6/2")],
            "PDF",
            "Kullanıcı");

        var draft = new SymbolDraft(wide);
        var scene = draft.ToScene();

        Assert.True(scene.WidthMm >= 260, $"Genişlik yetersiz: {scene.WidthMm}");
        Assert.True(scene.HeightMm >= 80, $"Yükseklik yetersiz: {scene.HeightMm}");
        Assert.Equal(scene.WidthMm, draft.SceneWidthMm);
        Assert.Equal(scene.HeightMm, draft.SceneHeightMm);
        Assert.Equal(40.0, draft.SceneOriginX);

        foreach (var rect in scene.Items.OfType<RectItem>())
        {
            Assert.InRange(rect.X, 0, scene.WidthMm);
            Assert.InRange(rect.X + rect.Width, 0, scene.WidthMm);
            Assert.InRange(rect.Y, 0, scene.HeightMm);
            Assert.InRange(rect.Y + rect.Height, 0, scene.HeightMm);
        }

        var negDraft = new SymbolDraft(new SymbolDefinition(
            SymbolId.New(),
            "Negatif sol kenar",
            [new SymbolRect(-60, -25, 80, 40)],
            [new SymbolPin(0, 0, 0, -1)],
            "test",
            "test"));
        Assert.True(negDraft.SceneOriginX >= 70);
        Assert.True(negDraft.SceneOriginY >= 35);
        Assert.True(negDraft.SceneWidthMm >= 100);
    }

    [Fact]
    public void Rotated_and_closely_packed_labels_are_selected_individually_and_edited_with_undo()
    {
        var symbol = new SymbolDefinition(
            SymbolId.New(),
            "Sürücü etiketleri",
            [
                new SymbolLine(55, 10, 110, 10),
                new SymbolText(64.8, 9.1, "A_DI1", 3.47, TextAlign.Left, -45),
                new SymbolText(69.8, 9.1, "A_DI2", 3.47, TextAlign.Left, -45),
                new SymbolText(74.8, 9.1, "A_DI3", 3.47, TextAlign.Left, -45),
                new SymbolText(60, 12, "CN4", 3.5, TextAlign.Left, -90),
                new SymbolText(65, 18, "DIGITAL INPUTS/OUTPUTS", 3.5, TextAlign.Left, 0),
            ],
            [new SymbolPin(0, 0, 0, -1)],
            "PDF",
            "Kullanıcı");

        var draft = new SymbolDraft(symbol) { Tool = SymbolTool.Select };

        // -45 derece eğik yazının kendi dönmüş gövdesinin ortasına tıklama
        var hit1 = draft.SelectAt(64.8 + 2.6, 9.1 - 4.4);
        Assert.NotNull(hit1);
        Assert.Equal("A_DI1", draft.SelectedText?.Text);

        var hit2 = draft.SelectAt(69.8 + 2.6, 9.1 - 4.4);
        Assert.NotNull(hit2);
        Assert.Equal("A_DI2", draft.SelectedText?.Text);

        var hit3 = draft.SelectAt(74.8 + 2.6, 9.1 - 4.4);
        Assert.NotNull(hit3);
        Assert.Equal("A_DI3", draft.SelectedText?.Text);

        // -90 derece dikey yazının yukarı uzanan gövdesine tıklama ve dikey vurgulu kutu kontrolü
        var hitCn4 = draft.SelectAt(60 - 1.5, 12 - 3.0);
        Assert.NotNull(hitCn4);
        Assert.Equal("CN4", draft.SelectedText?.Text);
        var cn4Box = draft.SelectedBox()!.Value;
        Assert.True(cn4Box.Height > cn4Box.Width, $"Dikey yazı kutusu dikey olmalı: {cn4Box}");

        var hitTitle = draft.SelectAt(80, 16.5);
        Assert.NotNull(hitTitle);
        Assert.Equal("DIGITAL INPUTS/OUTPUTS", draft.SelectedText?.Text);

        Assert.Equal("sembol.yazi_bos", draft.SetSelectedText("  ", 3.5, TextAlign.Left, 0).Error!.Code);

        var edited = draft.SetSelectedText(" DİJİTAL GİRİŞLER ", 4.0, TextAlign.Center, 0);
        Assert.True(edited.IsSuccess, edited.Error?.Message);
        Assert.Equal("DİJİTAL GİRİŞLER", draft.SelectedText?.Text);
        Assert.Equal(4.0, draft.SelectedText?.SizeMm);
        Assert.Equal(TextAlign.Center, draft.SelectedText?.Align);

        draft.Undo();
        Assert.Equal("DIGITAL INPUTS/OUTPUTS", ((SymbolText)draft.Shapes[5]).Text);
    }
}
