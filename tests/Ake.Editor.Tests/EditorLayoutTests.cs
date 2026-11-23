using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Avalonia.Json;
using Dock.Model.Core;

namespace Ake.Editor.Tests;

public sealed class EditorLayoutTests
{
    private static (Factory Factory, RootDock Root, ToolDock Left, DocumentDock Documents, ToolDock Bottom, ProportionalDock Center) BuildTree()
    {
        var factory = new Factory();
        var left = new ToolDock
        {
            Id = "sol",
            Alignment = Alignment.Left,
            Proportion = 0.2,
            CollapsedProportion = 0.2,
            IsCollapsable = false,
            VisibleDockables = factory.CreateList<IDockable>(new Tool { Id = "sayfalar", Title = "Sayfalar" }),
        };
        var documents = new DocumentDock
        {
            Id = "sayfa-sekmeleri",
            CanCreateDocument = false,
            IsCollapsable = false,
            Proportion = 0.74,
            CollapsedProportion = 0.74,
            MinHeight = 160,
            VisibleDockables = factory.CreateList<IDockable>(),
        };
        var bottom = new ToolDock
        {
            Id = "alt",
            Alignment = Alignment.Bottom,
            Proportion = 0.26,
            CollapsedProportion = 0.26,
            IsCollapsable = false,
            VisibleDockables = factory.CreateList<IDockable>(new Tool { Id = "mesajlar", Title = "Mesajlar" }),
        };
        var center = new ProportionalDock
        {
            Id = "orta",
            Orientation = Orientation.Vertical,
            IsCollapsable = false,
            Proportion = 0.8,
            CollapsedProportion = 0.8,
            MinWidth = 240,
            VisibleDockables = factory.CreateList<IDockable>(documents, new ProportionalDockSplitter(), bottom),
        };
        var main = new ProportionalDock
        {
            Id = "ana",
            Orientation = Orientation.Horizontal,
            IsCollapsable = false,
            VisibleDockables = factory.CreateList<IDockable>(left, new ProportionalDockSplitter(), center),
        };
        var root = (RootDock)factory.CreateRootDock();
        root.Id = "kok";
        root.IsCollapsable = false;
        root.VisibleDockables = factory.CreateList<IDockable>(main);
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        factory.InitLayout(root);
        return (factory, root, left, documents, bottom, center);
    }

    [Fact]
    public void NormalizeLayout_repairs_collapsed_documents_when_messages_took_full_height()
    {
        var (factory, root, _, documents, bottom, _) = BuildTree();
        documents.IsEmpty = true;
        documents.Proportion = 0.0;
        documents.MinHeight = double.NaN;
        bottom.Proportion = 1.0;

        var repaired = EditorLayout.NormalizeLayout(factory, root, documents, navigatorsHidden: false, messagesHidden: false);

        Assert.True(repaired);
        Assert.False(documents.IsEmpty);
        Assert.Equal(0.74, documents.Proportion, 3);
        Assert.Equal(0.26, bottom.Proportion, 3);
        Assert.True(documents.MinHeight >= 160);
    }

    [Fact]
    public void NormalizeLayout_repairs_nan_documents_proportion_when_bottom_expanded()
    {
        var (factory, root, _, documents, bottom, _) = BuildTree();
        documents.IsEmpty = false;
        documents.Proportion = double.NaN;
        bottom.Proportion = 1.0;

        var repaired = EditorLayout.NormalizeLayout(factory, root, documents, navigatorsHidden: false, messagesHidden: false);

        Assert.True(repaired);
        Assert.Equal(0.74, documents.Proportion, 3);
        Assert.Equal(0.26, bottom.Proportion, 3);
    }

    [Fact]
    public void NormalizeLayout_preserves_valid_user_proportions()
    {
        var (factory, root, left, documents, bottom, center) = BuildTree();
        documents.IsEmpty = false;
        documents.Proportion = 0.65;
        bottom.Proportion = 0.35;
        left.Proportion = 0.25;
        center.Proportion = 0.75;
        documents.MinHeight = 160;
        center.MinWidth = 240;

        var repaired = EditorLayout.NormalizeLayout(factory, root, documents, navigatorsHidden: false, messagesHidden: false);

        Assert.False(repaired);
        Assert.Equal(0.65, documents.Proportion, 3);
        Assert.Equal(0.35, bottom.Proportion, 3);
        Assert.Equal(0.25, left.Proportion, 3);
        Assert.Equal(0.75, center.Proportion, 3);
    }

    [Fact]
    public void NormalizeLayout_expands_documents_to_full_height_when_messages_are_hidden()
    {
        var (factory, root, _, documents, _, _) = BuildTree();
        documents.IsEmpty = false;
        documents.Proportion = 0.0;

        var repaired = EditorLayout.NormalizeLayout(factory, root, documents, navigatorsHidden: false, messagesHidden: true);

        Assert.True(repaired);
        Assert.Equal(1.0, documents.Proportion, 3);
    }

    [Fact]
    public void Repaired_layout_survives_json_round_trip()
    {
        var (factory, root, _, documents, bottom, _) = BuildTree();
        documents.IsEmpty = true;
        documents.Proportion = 0.0;
        bottom.Proportion = 1.0;
        Assert.True(EditorLayout.NormalizeLayout(factory, root, documents, navigatorsHidden: false, messagesHidden: false));

        var serializer = new AvaloniaDockSerializer();
        var json = serializer.Serialize(root);
        var loaded = serializer.Deserialize<RootDock>(json)!;
        var loadedFactory = new Factory();
        var loadedDocs = loadedFactory.Find(loaded, d => d.Id == "sayfa-sekmeleri").OfType<DocumentDock>().Distinct().Single();
        var loadedBottom = loadedFactory.Find(loaded, d => d.Id == "alt").OfType<ToolDock>().Distinct().Single();

        Assert.False(loadedDocs.IsCollapsable);
        Assert.Equal(0.74, loadedDocs.Proportion, 3);
        Assert.Equal(0.26, loadedBottom.Proportion, 3);
    }
}
