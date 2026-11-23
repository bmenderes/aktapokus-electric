using System.Globalization;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Editor;

/// <summary>Yeni proje açılış önayarları (ADR-0058).</summary>
public enum ProjectPreset
{
    /// <summary>Tek başlangıç şeması ve kapak sayfası.</summary>
    Standard,

    /// <summary>Güç dağıtımı, kumanda, tek hat ve haberleşme şeması sayfalarıyla endüstriyel makine şablonu.</summary>
    IndustrialMachine,

    /// <summary>Ağ topolojisi, haberleşme şeması ve tek hat diyagramı odak şablonu.</summary>
    CommunicationTopology,

    /// <summary>Pnömatik hat türü ve valf adası şeması şablonu.</summary>
    Pneumatic,
}

/// <summary>Yeni proje penceresinin seçimi (ADR-0047): ad, oluşturan, tarih, saklama klasörü, şablon ve proje önayarı.</summary>
public sealed record NewProjectInfo(
    string Name,
    string Creator = "",
    string Date = "",
    string? Folder = null,
    SheetTemplateId? TemplateId = null,
    ProjectPreset Preset = ProjectPreset.Standard);

/// <summary>Proje özellikleri ve yeni proje, oturum tarafı (ADR-0047): kapak, antet ve tablo sayfalarının kullandığı proje bilgileri.</summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// Proje özellikleri penceresinin düzenlenebilir alanları: özellik anahtarı ve etiketi. Anahtarlar kapak sayfasının ve antet yer tutucularının
    /// (<c>{project.drawingNo}</c> gibi) kullandığı anahtarlardır; başka alan eklemek için <c>ake apply</c> (<c>setProperty</c>) kullanılır.
    /// </summary>
    public static IReadOnlyList<(string Key, string Label)> ProjectFields { get; } =
    [
        ("description", "Proje açıklaması"),
        ("customer", "Müşteri"),
        ("commission", "Sipariş / iş numarası"),
        ("drawingNo", "Çizim numarası"),
        ("company", "Firma adı"),
        ("companyAddress", "Firma adresi"),
        ("drawnBy", "Çizen"),
        ("date", "Tarih"),
        ("revision", "Revizyon"),
    ];

    /// <summary>
    /// Proje adını ve alanları tek adımda değiştirir (geri alınır). Boş değer alanı siler; değişmeyen alana dokunulmaz. Ad boş olamaz.
    /// Değişiklik yoksa hiçbir şey yazılmaz.
    /// </summary>
    public Result SetProjectInfo(string name, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(values);
        if (Project is null)
        {
            return NoProject();
        }

        var commands = new List<ICommand>();
        if (name.Trim() != Project.Name)
        {
            commands.Add(new SetProjectName(name));
        }

        foreach (var (key, raw) in values)
        {
            var wanted = raw?.Trim() ?? string.Empty;
            var current = Project.Properties.GetValueOrDefault(key);
            if (wanted.Length == 0 ? current is not null : wanted != current)
            {
                commands.Add(new SetProjectProperty(key, wanted.Length == 0 ? null : wanted));
            }
        }

        return commands.Count == 0 ? Result.Success() : Execute(new Transaction("Proje özellikleri değiştirildi", commands));
    }

    /// <summary>
    /// Revizyon penceresinin önerisi: sıradaki işaret, bugünün tarihi (gg.aa.yyyy) ve hazırlayan (proje <c>drawnBy</c>, yoksa oturum kullanıcısı);
    /// açıklama boştur.
    /// </summary>
    public Revision SuggestRevision(DateOnly today) => new(
        Project is null ? "A" : Ake.Core.Numbering.RevisionNumbering.NextMark(Project),
        today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        Project?.Properties.GetValueOrDefault("drawnBy") ?? Environment.UserName,
        string.Empty);

    /// <summary>
    /// Revizyon ekler ve antetteki revizyonu (<c>revision</c> özelliği) o işarete getirir; tek adımda geri alınır. İşaret ya da açıklama geçersizse
    /// hiçbir şey değişmez.
    /// </summary>
    public Result AddRevisionRecord(Revision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return Project is null
            ? NoProject()
            : Execute(new Transaction($"Revizyon eklendi: {revision.Mark}", [new AddRevision(revision), new SetProjectProperty("revision", revision.Mark)]));
    }

    /// <summary>Proje özellikleri penceresinin İstatistik sekmesi: etiket ve değer çiftleri.</summary>
    public IReadOnlyList<(string Label, string Value)> ProjectStatistics()
    {
        if (Project is null)
        {
            return [];
        }

        string N(int count) => count.ToString(CultureInfo.InvariantCulture);
        var lines = new List<(string, string)>
        {
            ("Sayfa", N(Project.Pages.Count)),
        };
        foreach (var group in Project.Pages.GroupBy(page => page.Kind).OrderBy(group => group.Key))
        {
            lines.Add(($"   {KindName(group.Key)}", N(group.Count())));
        }

        lines.Add(("Cihaz", N(Project.Devices.Count(device => device.Kind != DeviceKind.Junction))));
        lines.Add(("Klemens grubu", N(Project.Devices.Count(device => device.Kind == DeviceKind.TerminalStrip))));
        lines.Add(("Bağlantı", N(Project.Connections.Count)));
        lines.Add(("Numaralı tel", N(Project.Connections.Count(connection => connection.Wire?.Number is not null))));
        lines.Add(("Kablo", N(Project.Cables.Count)));
        lines.Add(("Sembol yerleşimi", N(Project.Placements.Count)));
        lines.Add(("Sayfa şablonu", N(Project.SheetTemplates.Count)));
        return lines;
    }

    /// <summary>Yapı sekmesi: fonksiyon (=) ve yer (+) düğümleri, girintili ve her düğümün kaç sayfası ve cihazı olduğuyla.</summary>
    public IReadOnlyList<string> ProjectStructureLines()
    {
        if (Project is null)
        {
            return [];
        }

        var project = Project;
        var lines = new List<string>();
        foreach (var aspect in new[] { Aspect.Function, Aspect.Location })
        {
            var prefix = aspect == Aspect.Function ? '=' : '+';
            lines.Add(aspect == Aspect.Function ? "Fonksiyon yapısı" : "Yer yapısı");
            var nodes = project.Structure.Where(node => node.Aspect == aspect).ToList();
            if (nodes.Count == 0)
            {
                lines.Add("   (yok)");
            }

            foreach (var node in nodes.OrderBy(node => project.StructurePath(node.Id), StringComparer.Ordinal))
            {
                var path = project.StructurePath(node.Id)!;
                var depth = path.Count(ch => ch == '.');
                var pages = project.Pages.Count(page => (aspect == Aspect.Function ? page.FunctionNodeId : page.LocationNodeId) == node.Id);
                var devices = project.Devices.Count(device => (aspect == Aspect.Function ? device.FunctionNodeId : device.LocationNodeId) == node.Id);
                var detail = string.IsNullOrWhiteSpace(node.Description) ? string.Empty : $" — {node.Description}";
                lines.Add($"{new string(' ', 3 + (depth * 3))}{prefix}{path}{detail}   ({pages} sayfa, {devices} cihaz)");
            }

            lines.Add(string.Empty);
        }

        return lines;
    }

    /// <summary>Durum sekmesi: dosya, kayıt durumu, son değişiklik ve ERC özeti.</summary>
    public IReadOnlyList<(string Label, string Value)> ProjectStatus()
    {
        if (Project is null || _stack is null)
        {
            return [];
        }

        var last = _stack.Audit.LastOrDefault();
        return
        [
            ("Dosya", ProjectPath ?? "(henüz kaydedilmedi)"),
            ("Kayıt durumu", IsDirty ? "Kaydedilmemiş değişiklik var" : "Kaydedildi"),
            ("Dosya biçimi", $"şema sürümü {ProjectJson.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}"),
            ("Bu oturumdaki değişiklik", _stack.Audit.Count.ToString(CultureInfo.InvariantCulture)),
            ("Son değişiklik", last is null ? "—" : $"{last.Summary} ({last.Actor})"),
            ("ERC", Erc is null ? "—" : $"{Erc.Count(Severity.Error)} hata, {Erc.Count(Severity.Warning)} uyarı"),
        ];
    }

    /// <summary>
    /// Yeni proje (ADR-0047): <see cref="NewProject(string)"/> gibi boş projeyi kurar, ayrıca oluşturan (<c>drawnBy</c>) ve tarihi (<c>date</c>)
    /// proje özelliği olarak yazar ve projeyi kapak sayfasıyla (1) ve boş bir şema sayfasıyla (2) başlatır. Tarih boşsa bugünün tarihi (gg.aa.yyyy) alınır.
    /// </summary>
    public Result NewProject(NewProjectInfo info, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(info);

        var properties = new Dictionary<string, string>
        {
            ["date"] = string.IsNullOrWhiteSpace(info.Date) ? today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : info.Date.Trim(),
        };
        if (!string.IsNullOrWhiteSpace(info.Creator))
        {
            properties["drawnBy"] = info.Creator.Trim();
        }

        var created = NewProjectCore(info.Name, properties, withCover: true, info.TemplateId);
        if (created.IsFailure || info.Preset == ProjectPreset.Standard)
        {
            return created;
        }

        return ApplyPreset(info.Preset);
    }

    private Result ApplyPreset(ProjectPreset preset)
    {
        if (Project is null)
        {
            return NoProject();
        }

        var templateId = Project.Pages.FirstOrDefault(p => p.Kind == PageKind.Schematic)?.TemplateId
            ?? Project.SheetTemplates.FirstOrDefault()?.Id;
        var commands = new List<ICommand>();

        switch (preset)
        {
            case ProjectPreset.IndustrialMachine:
                BuildIndustrialPreset(commands, templateId);
                break;

            case ProjectPreset.CommunicationTopology:
                commands.Add(new AddPage(new Page(PageId.New(), "2", "Haberleşme şeması", null, null, templateId, PageKind.CommunicationDiagram)));
                commands.Add(new AddPage(new Page(PageId.New(), "3", "Tek hat ağ topolojisi", null, null, templateId, PageKind.SingleLineDiagram)));
                commands.Add(new PutConnectionKind(new ConnectionKind(ConnectionKindId.New(), "PROFINET", "#008844", LineStyle.Solid, PinKind.Signal)));
                commands.Add(new PutConnectionKind(new ConnectionKind(ConnectionKindId.New(), "EtherNet/IP", "#0066aa", LineStyle.Solid, PinKind.Signal)));
                break;

            case ProjectPreset.Pneumatic:
                commands.Add(new SetProjectProperty("discipline", "pneumatic"));
                commands.Add(new AddPage(new Page(PageId.New(), "2", "Valf adası ve hava hazırlama", null, null, templateId, PageKind.Schematic)));
                commands.Add(new PutConnectionKind(new ConnectionKind(ConnectionKindId.New(), "Pnömatik basınç hattı", "#0077b6", LineStyle.Solid, PinKind.Unspecified)));
                break;
        }

        return commands.Count == 0
            ? Result.Success()
            : new Transaction("Proje önayarı uygulandı", commands).Execute(Project);
    }

    private static void BuildIndustrialPreset(List<ICommand> commands, SheetTemplateId? templateId)
    {
        var guc = new StructureNode(StructureNodeId.New(), Aspect.Function, "GUC", description: "Güç dağıtımı");
        var kum = new StructureNode(StructureNodeId.New(), Aspect.Function, "KUM", description: "Kumanda");
        var p1 = new StructureNode(StructureNodeId.New(), Aspect.Location, "P1", description: "Ana pano");
        commands.Add(new AddStructureNode(guc));
        commands.Add(new AddStructureNode(kum));
        commands.Add(new AddStructureNode(p1));
        commands.Add(new AddPage(new Page(PageId.New(), "2", "Güç dağıtımı", guc.Id, p1.Id, templateId, PageKind.Schematic)));
        commands.Add(new AddPage(new Page(PageId.New(), "3", "Kumanda devresi", kum.Id, p1.Id, templateId, PageKind.Schematic)));
        commands.Add(new AddPage(new Page(PageId.New(), "4", "Tek hat dağıtım", null, null, templateId, PageKind.SingleLineDiagram)));
        commands.Add(new AddPage(new Page(PageId.New(), "5", "Haberleşme topolojisi", null, null, templateId, PageKind.CommunicationDiagram)));
        commands.Add(new PutConnectionKind(new ConnectionKind(ConnectionKindId.New(), "400VAC L1-L2-L3", "#cc0000", LineStyle.Solid, PinKind.Power)));
        commands.Add(new PutConnectionKind(new ConnectionKind(ConnectionKindId.New(), "24VDC Kontrol", "#0055cc", LineStyle.Solid, PinKind.Signal)));
    }

    /// <summary>Sayfa türünün pencerelerde görünen adı.</summary>
    public static string KindName(PageKind kind) => kind switch
    {
        PageKind.Schematic => "Şema sayfası",
        PageKind.CommunicationDiagram => "Haberleşme şeması",
        PageKind.SingleLineDiagram => "Tek hat şeması",
        PageKind.Cover => "Kapak",
        PageKind.Contents => "İçindekiler",
        PageKind.PartsList => "Malzeme listesi",
        PageKind.TerminalPlan => "Klemens planı",
        PageKind.CableList => "Kablo listesi",
        PageKind.PotentialOverview => "Potansiyel özeti",
        PageKind.PinConnections => "Pin-bağlantı şeması",
        PageKind.PlcDiagram => "PLC şeması",
        PageKind.RevisionOverview => "Revizyon özeti",
        PageKind.FrameDocumentation => "Çerçeve dokümantasyonu",
        _ => kind.ToString(),
    };
}
