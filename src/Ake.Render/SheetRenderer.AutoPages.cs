using System.Globalization;
using Ake.Core.Model;
using Ake.Reports;

namespace Ake.Render;

/// <summary>
/// Otomatik üretilen sayfa türleri (ADR-0046): kapak sayfası ve tablo sayfaları (içindekiler, malzeme listesi, klemens planı, kablo listesi).
/// Çerçeve ve antet normal sayfalarla aynı şablondan gelir; içerik projeden üretilir ve proje değişince sayfa kendiliğinden güncellenir.
/// </summary>
public static partial class SheetRenderer
{
    private const double TableTextMm = 2.5;
    private const double TableRowMm = 6;
    private const double TableHeadingMm = 5;
    private const double CharWidthMm = 1.5;

    // Kapakta gösterilen proje özellikleri: etiket ve özellik anahtarı. Değeri olmayan satır çizilmez.
    private static readonly (string Label, string Key)[] CoverFields =
    [
        ("Müşteri", "customer"),
        ("Sipariş / iş no", "commission"),
        ("Çizim no", "drawingNo"),
        ("Firma", "company"),
        ("Firma adresi", "companyAddress"),
        ("Çizen", "drawnBy"),
        ("Tarih", "date"),
        ("Revizyon", "revision"),
    ];

    /// <summary>Türü otomatik üretilen bir sayfa mı (kapak ya da tablo sayfası).</summary>
    public static bool IsGenerated(PageKind kind) =>
        kind is PageKind.Cover or PageKind.PlcDiagram || IsTable(kind);

    /// <summary>Tablo türü sayfa mı (satırlar sayfalara bölünür).</summary>
    public static bool IsTable(PageKind kind) =>
        kind is PageKind.Contents or PageKind.PartsList or PageKind.TerminalPlan or PageKind.CableList
            or PageKind.PotentialOverview or PageKind.PinConnections or PageKind.RevisionOverview or PageKind.FrameDocumentation;

    /// <summary>Şablondaki bir sayfaya sığan tablo satırı sayısı.</summary>
    public static int RowsPerPage(SheetTemplate template)
    {
        var area = SheetGeometry.For(template).DrawingArea;
        return Math.Max(1, (int)((area.Bottom - (area.Y + TableHeadingMm + 8) - TableRowMm - 4) / TableRowMm));
    }

    /// <summary>
    /// Tablo türü bir sayfanın tabloyu göstermek için kaç sayfa (devam sayfalarıyla) gerektirdiği; en az 1. Kapak için 1.
    /// </summary>
    public static int PartCount(Project project, PageKind kind, SheetTemplate template)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(template);

        if (kind == PageKind.PlcDiagram)
        {
            return PlcPartCount(project, template);
        }

        if (!IsTable(kind))
        {
            return 1;
        }

        var rows = TableOf(project, kind).Rows.Count;
        return Math.Max(1, (int)Math.Ceiling(rows / (double)RowsPerPage(template)));
    }

    private static void AddGenerated(List<SceneItem> items, List<string> warnings, Project project, Page page, SheetTemplate template, SheetGeometry geometry)
    {
        if (page.Kind == PageKind.Cover)
        {
            AddCover(items, project, page, template, geometry.DrawingArea);
        }
        else if (page.Kind == PageKind.PlcDiagram)
        {
            AddPlcDiagram(items, warnings, project, page, template, geometry.DrawingArea);
        }
        else
        {
            AddTable(items, warnings, project, page, template, geometry.DrawingArea);
        }
    }

    private static void AddCover(List<SceneItem> items, Project project, Page page, SheetTemplate template, Box area)
    {
        var padX = Math.Max(10, Math.Round(area.Width * 0.06, 1));
        var left = area.X + padX;
        var right = area.Right - padX;
        var width = Math.Max(40, right - left);
        var centre = area.X + (area.Width / 2);

        // Başlık ve açıklama dar kâğıtta (A4 dikey) çerçeveden taşmasın diye genişliğe sığacak boyuta küçülür.
        double Fitted(string text, double size) => Math.Max(2.5, Math.Min(size, (width - 12) / (Math.Max(1, text.Length) * 0.55)));

        // Üst başlık kutusu: üst şeritte sayfa türü/başlığı, ortada proje adı ve varsa açıklaması.
        var language = project.Properties.GetValueOrDefault("language");
        var pageTitle = MultilingualText.Resolve(page.Title, language);
        var projectName = MultilingualText.Resolve(project.Name, language);
        var headerTop = area.Y + Math.Round(area.Height * 0.07, 1);
        var headerHeight = Math.Round(area.Height * 0.24, 1);
        items.Add(new RectItem(left, headerTop, width, headerHeight, 0.4));
        items.Add(new LineItem(left, headerTop + 9, right, headerTop + 9, 0.2));
        items.Add(new TextItem(centre, headerTop + 6.3, pageTitle, Fitted(pageTitle, 3.8), template.FontFamily, TextAlign.Center));
        items.Add(new TextItem(centre, headerTop + (headerHeight * 0.54), projectName, Fitted(projectName, 11.5), template.FontFamily, TextAlign.Center));
        if (project.Properties.GetValueOrDefault("companyLogo") is { Length: > 0 } logoPath)
        {
            var logoMargin = 4.0;
            var logoSize = Math.Max(12.0, headerHeight - 9 - (logoMargin * 2));
            items.Add(new ImageItem(left + logoMargin, headerTop + 9 + logoMargin, logoSize, logoSize, logoPath));
        }

        if (project.Properties.GetValueOrDefault("description") is { Length: > 0 } description)
        {
            var resolvedDescription = MultilingualText.Resolve(description, language);
            items.Add(new TextItem(centre, headerTop + (headerHeight * 0.82), resolvedDescription, Fitted(resolvedDescription, 5.0), template.FontFamily, TextAlign.Center));
        }

        // Orta alan: yalnızca dolu proje alanları geniş sayfada iki sütunlu kartlara, dar sayfada tek sütuna dağılır.
        var activeFields = CoverFields
            .Where(field => project.Properties.GetValueOrDefault(field.Key) is { Length: > 0 })
            .Select(field => (field.Label, Value: project.Properties[field.Key]))
            .ToList();

        var bodyTop = headerTop + headerHeight + Math.Round(area.Height * 0.06, 1);
        var stripHeight = 14.0;
        var stripTop = area.Bottom - stripHeight - Math.Round(area.Height * 0.05, 1);

        if (activeFields.Count > 0)
        {
            const double rowHeight = 10.5;
            var twoColumns = width >= 210 && activeFields.Count >= 2;
            var colGap = 14.0;
            var colWidth = twoColumns ? (width - colGap) / 2 : width;
            var split = twoColumns ? (activeFields.Count + 1) / 2 : activeFields.Count;
            var columns = twoColumns
                ? new[] { (X: left, Rows: activeFields.Take(split).ToList()), (X: left + colWidth + colGap, Rows: activeFields.Skip(split).ToList()) }
                : [(X: left, Rows: activeFields)];

            var maxRows = columns.Max(col => col.Rows.Count);
            var cardHeight = maxRows * rowHeight;
            var labelW = Math.Round(Math.Min(42, colWidth * 0.38), 1);

            foreach (var (colX, rows) in columns)
            {
                items.Add(new RectItem(colX, bodyTop, colWidth, cardHeight, 0.3));
                items.Add(new LineItem(colX + labelW, bodyTop, colX + labelW, bodyTop + cardHeight, 0.13));
                for (var r = 0; r < rows.Count; r++)
                {
                    var rowY = bodyTop + (r * rowHeight);
                    if (r > 0)
                    {
                        items.Add(new LineItem(colX, rowY, colX + colWidth, rowY, 0.13));
                    }

                    items.Add(new TextItem(colX + 4, rowY + (rowHeight * 0.64), rows[r].Label, 3.1, template.FontFamily));
                    items.Add(new TextItem(colX + labelW + 3, rowY + (rowHeight * 0.64), Fit(rows[r].Value, colWidth - labelW - 6), 3.3, template.FontFamily));
                }
            }
        }

        // Alt özet şeridi: toplam sayfa, şablon ve standart bilgisi.
        if (stripTop > bodyTop + 14)
        {
            items.Add(new RectItem(left, stripTop, width, stripHeight, 0.3));
            var cellW = width / 3;
            var summary = new[]
            {
                ("Toplam sayfa", project.Pages.Count.ToString(CultureInfo.InvariantCulture)),
                ("Sayfa şablonu", Fit(template.Name, cellW - 6)),
                ("Standart", Fit("IEC 81346 / IEC 60617", cellW - 6)),
            };
            for (var c = 0; c < summary.Length; c++)
            {
                var cx = left + (c * cellW);
                if (c > 0)
                {
                    items.Add(new LineItem(cx, stripTop, cx, stripTop + stripHeight, 0.2));
                }

                items.Add(new TextItem(cx + 3.5, stripTop + 4.5, summary[c].Item1, 2.2, template.FontFamily));
                items.Add(new TextItem(cx + 3.5, stripTop + 10.8, summary[c].Item2, 3.2, template.FontFamily));
            }
        }
    }

    private static void AddTable(List<SceneItem> items, List<string> warnings, Project project, Page page, SheetTemplate template, Box area)
    {
        var table = TableOf(project, page.Kind);
        var rowsPerPage = RowsPerPage(template);
        var left = area.X + 6;
        var width = area.Width - 12;
        var top = area.Y + TableHeadingMm + 8;

        var headingTitle = MultilingualText.Resolve(page.Title, project.Properties.GetValueOrDefault("language"));
        items.Add(new TextItem(left, area.Y + TableHeadingMm + 3, page.Part > 0 ? $"{headingTitle} (devam {page.Part + 1})" : headingTitle, TableHeadingMm, template.FontFamily));

        var widths = ColumnWidths(table, width);
        var starts = new double[widths.Length];
        var x = left;
        for (var c = 0; c < widths.Length; c++)
        {
            starts[c] = x;
            x += widths[c];
        }

        var y = top;
        for (var c = 0; c < table.Header.Count; c++)
        {
            items.Add(new TextItem(starts[c] + 2, y + TableRowMm - 1.8, Fit(table.Header[c], widths[c]), TableTextMm, template.FontFamily));
        }

        y += TableRowMm;
        items.Add(new LineItem(left, y, left + width, y, 0.35));

        var first = page.Part * rowsPerPage;
        var shown = table.Rows.Skip(first).Take(rowsPerPage).ToList();
        for (var r = 0; r < shown.Count; r++)
        {
            for (var c = 0; c < shown[r].Count; c++)
            {
                if (shown[r][c].Length > 0)
                {
                    items.Add(new TextItem(starts[c] + 2, y + TableRowMm - 1.8, Fit(shown[r][c], widths[c]), TableTextMm, template.FontFamily));
                }
            }

            if (table.Anchors is { } anchors && anchors[first + r] is { } anchor)
            {
                items.Add(new LinkItem(left, y, width, TableRowMm, anchor));
            }

            y += TableRowMm;
            items.Add(new LineItem(left, y, left + width, y, 0.13));
        }

        if (table.Rows.Count == 0)
        {
            items.Add(new TextItem(left + 2, y + TableRowMm - 1.8, "(kayıt yok)", TableTextMm, template.FontFamily));
        }

        // Devam sayfası zaten eklenmişse uyarı gerekmez.
        var remaining = table.Rows.Count - first - shown.Count;
        var continued = project.Pages.Any(other => other.Kind == page.Kind && other.Part > page.Part);
        if (remaining > 0 && !continued)
        {
            warnings.Add($"Tablo sürüyor: {remaining} satır bu sayfaya sığmadı; devam sayfası ekleyin.");
        }
        else if (page.Part > 0 && shown.Count == 0)
        {
            warnings.Add("Bu devam sayfasında gösterilecek satır kalmadı; sayfayı silebilirsiniz.");
        }
    }

    // Sütun genişlikleri içeriğe göre (en uzun hücre), toplam genişliğe sığacak biçimde orantılı küçültülür ya da büyütülür.
    private static double[] ColumnWidths(PageTable table, double total)
    {
        var wanted = new double[table.Header.Count];
        for (var c = 0; c < wanted.Length; c++)
        {
            var longest = Math.Max(table.Header[c].Length, table.Rows.Select(row => row[c].Length).DefaultIfEmpty(0).Max());
            wanted[c] = Math.Clamp((longest * CharWidthMm) + 4, 12, 90);
        }

        var scale = total / wanted.Sum();
        return [.. wanted.Select(width => width * scale)];
    }

    // Sütuna sığmayan metin "…" ile kısaltılır.
    private static string Fit(string text, double width)
    {
        var chars = Math.Max(1, (int)((width - 3) / CharWidthMm));
        return text.Length <= chars ? text : string.Concat(text.AsSpan(0, Math.Max(1, chars - 1)), "…");
    }

    private sealed record PageTable(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string?>? Anchors);

    private static PageTable TableOf(Project project, PageKind kind) => kind switch
    {
        PageKind.Contents => new PageTable(
            ["Sayfa", "Başlık", "Fonksiyon", "Yer"],
            [.. project.Pages.Select(page => (IReadOnlyList<string>)
            [
                page.Number,
                MultilingualText.Resolve(page.Title, project.Properties.GetValueOrDefault("language")),
                project.StructurePath(page.FunctionNodeId) is { } function ? $"={function}" : string.Empty,
                project.StructurePath(page.LocationNodeId) is { } location ? $"+{location}" : string.Empty,
            ])],
            [.. project.Pages.Select(AnchorOf)]),
        PageKind.PartsList => FromReport(ReportCatalog.Bom(project)),
        PageKind.TerminalPlan => FromReport(ReportCatalog.Terminals(project)),
        PageKind.CableList => FromReport(ReportCatalog.Cables(project)),
        PageKind.PotentialOverview => PotentialTable(project),
        PageKind.PinConnections => PinTable(project),
        PageKind.RevisionOverview => new PageTable(
            ["Rev.", "Tarih", "Hazırlayan", "Açıklama"],
            [.. project.Revisions.Select(revision => (IReadOnlyList<string>)[revision.Mark, revision.Date, revision.Author, revision.Description])],
            null),
        PageKind.FrameDocumentation => FrameTable(project),
        _ => new PageTable([], [], null),
    };

    // Potansiyel özeti: iki ya da daha çok uçlu potansiyeller; adı (kaynak adı), tel numaraları ve uçları. Çok uzun uç listesi kısaltılır.
    private static PageTable PotentialTable(Project project)
    {
        var connectivity = Connectivity.Compute(project);
        var rows = new List<(string Name, IReadOnlyList<string> Row)>();
        foreach (var potential in connectivity.Potentials)
        {
            var points = potential.Points.ToList();
            if (points.Count < 2)
            {
                continue;
            }

            var set = points.ToHashSet();
            var numbers = project.Connections
                .Where(connection => set.Contains(connection.From) && connection.Wire?.Number is not null)
                .Select(connection => connection.Wire!.Number!)
                .Distinct()
                .OrderBy(number => long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : long.MaxValue)
                .ThenBy(number => number, StringComparer.Ordinal)
                .ToList();
            var tags = points.Select(point => project.DesignationOf(point)?.ToString() ?? string.Empty).Where(tag => tag.Length > 0).Order(StringComparer.Ordinal).ToList();
            var name = potential.Name ?? (potential.HasConflict ? string.Join(" / ", potential.SourceNames) + " (çakışma)" : string.Empty);
            rows.Add((name, [name, string.Join(", ", numbers), points.Count.ToString(CultureInfo.InvariantCulture), string.Join(", ", tags.Take(8)) + (tags.Count > 8 ? ", …" : string.Empty)]));
        }

        return new PageTable(
            ["Potansiyel", "Tel no", "Uç sayısı", "Uçlar"],
            [.. rows.OrderBy(row => row.Name.Length == 0).ThenBy(row => row.Name, StringComparer.Ordinal).Select(row => row.Row)],
            null);
    }

    // Pin-bağlantı şeması: her bağlantının iki ucu için bir satır (cihaz ucu → hedef ucu), cihaz sırasına göre; ortam tel numarası ya da kablo damarıdır.
    private static PageTable PinTable(Project project)
    {
        var cores = project.Cables
            .SelectMany(cable => cable.Cores.Select(core => (core.Id, Label: $"-{cable.ProductTag}:{core.Designation}")))
            .ToDictionary(core => core.Id, core => core.Label);
        var rows = new List<(string Device, string Pin, string Target, string Medium)>();
        foreach (var connection in project.Connections)
        {
            var medium = connection.Wire?.Number ?? (connection.CoreId is { } coreId && cores.TryGetValue(coreId, out var label) ? label : string.Empty);
            foreach (var (here, there) in new[] { (connection.From, connection.To), (connection.To, connection.From) })
            {
                if (project.Locate(here) is not { } location || project.DesignationOf(there)?.ToString() is not { } target)
                {
                    continue;
                }

                rows.Add((project.DesignationOf(location.Device).ToString(), location.Point.Name, target, medium));
            }
        }

        return new PageTable(
            ["Cihaz", "Uç", "Hedef", "Tel / damar"],
            [.. rows.OrderBy(row => row.Device, StringComparer.Ordinal).ThenBy(row => row.Pin, StringComparer.Ordinal).ThenBy(row => row.Target, StringComparer.Ordinal)
                .Select(row => (IReadOnlyList<string>)[row.Device, row.Pin, row.Target, row.Medium])],
            null);
    }

    // Çerçeve dokümantasyonu: projedeki her şablonun kâğıdı, ızgarası, kenar boşluğu, anteti ve kullanan sayfa sayısı.
    private static PageTable FrameTable(Project project) => new(
        ["Şablon", "Kâğıt (mm)", "Kolon x satır", "Kenar boşluğu", "Izgara bandı", "Antet yüksekliği", "Sayfa"],
        [.. project.SheetTemplates.Select(template => (IReadOnlyList<string>)
        [
            template.Name,
            $"{Cell(template.WidthMm)} x {Cell(template.HeightMm)}",
            $"{template.Columns.ToString(CultureInfo.InvariantCulture)} x {template.Rows.ToString(CultureInfo.InvariantCulture)}",
            $"{Cell(template.MarginMm)} mm",
            $"{Cell(template.GridBandMm)} mm",
            $"{Cell(template.TitleBlockHeightMm)} mm",
            project.Pages.Count(page => project.TemplateFor(page)?.Id == template.Id).ToString(CultureInfo.InvariantCulture),
        ])],
        null);

    private static string Cell(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static PageTable FromReport(ReportTable table) => new(
        [.. table.Columns.Select(column => column.Header)],
        [.. table.Rows.Select(row => (IReadOnlyList<string>)[.. row.Select(Cell)])],
        null);

    private static string Cell(object? value) => value switch
    {
        null => string.Empty,
        decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
