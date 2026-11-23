using Ake.Core.Model;

namespace Ake.Serialization.Tests;

public class ProjectJsonTests
{
    private const string Minimal = """{ "schemaVersion": 1, "id": "0193a000-0000-7000-8000-000000000001", "name": "p" }""";

    private static string WithDevices(string devicesJson) =>
        $$"""
        {
          "schemaVersion": 1,
          "id": "0193a000-0000-7000-8000-000000000001",
          "name": "p",
          "devices": {{devicesJson}}
        }
        """;

    [Fact]
    public void Minimal_project_loads_with_empty_collections()
    {
        var result = ProjectJson.Deserialize(Minimal);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("p", result.Value.Name);
        Assert.Empty(result.Value.Devices);
    }

    [Fact]
    public void Serialized_text_is_stable_across_round_trips()
    {
        var first = ProjectJson.Serialize(ProjectJson.Deserialize(Minimal).Value);
        var second = ProjectJson.Serialize(ProjectJson.Deserialize(first).Value);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Output_uses_lf_line_endings_and_ends_with_a_single_newline()
    {
        var json = ProjectJson.Serialize(ProjectJson.Deserialize(Minimal).Value);

        Assert.DoesNotContain("\r", json);
        Assert.EndsWith("}\n", json);
    }

    [Fact]
    public void Non_ascii_text_and_enums_are_written_readably()
    {
        var device = new Device(
            DeviceId.New(),
            "K1",
            [new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")])],
            description: "Kontaktör çıkışı");
        var project = Project.Create(ProjectId.New(), "Güç", devices: [device]).Value;

        var json = ProjectJson.Serialize(project);

        Assert.Contains("\"description\": \"Kontaktör çıkışı\"", json);
        Assert.Contains("\"kind\": \"Coil\"", json);
        Assert.Contains($"\"schemaVersion\": {ProjectJson.CurrentSchemaVersion}", json);
    }

    [Fact]
    public void Absent_optional_values_are_not_written()
    {
        var project = Project.Create(ProjectId.New(), "p", devices: [new Device(DeviceId.New(), "K1", [])]).Value;

        var json = ProjectJson.Serialize(project);

        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain("\"part\"", json);
    }

    [Fact]
    public void Schema_1_file_is_migrated_and_saved_as_the_current_schema()
    {
        var loaded = ProjectJson.Deserialize(WithDevices(
            """[ { "id": "0193a000-0000-7000-8000-000000000002", "tag": "F1", "functions": [ { "id": "0193a000-0000-7000-8000-000000000003", "kind": "Protection" } ] } ]"""));

        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        Assert.Null(loaded.Value.Devices[0].Functions[0].RatedCurrentA);
        Assert.Contains($"\"schemaVersion\": {ProjectJson.CurrentSchemaVersion}", ProjectJson.Serialize(loaded.Value));
    }

    [Fact]
    public void Rated_current_survives_a_round_trip()
    {
        var breaker = new Device(
            DeviceId.New(),
            "F1",
            [new Function(FunctionId.New(), FunctionKind.Protection, [], ratedCurrentA: 6.3m)]);
        var project = Project.Create(ProjectId.New(), "p", devices: [breaker]).Value;

        var json = ProjectJson.Serialize(project);

        Assert.Contains("\"ratedCurrentA\": 6.3", json);
        Assert.Equal(6.3m, ProjectJson.Deserialize(json).Value.Devices[0].Functions[0].RatedCurrentA);
    }

    [Fact]
    public void Tag_scheme_is_written_only_when_it_is_not_the_default()
    {
        var letters = Project.Create(ProjectId.New(), "p").Value;
        var pages = Project.Create(ProjectId.New(), "p", tagScheme: TagScheme.PageLetterNumber).Value;

        var json = ProjectJson.Serialize(pages);

        Assert.DoesNotContain("tagScheme", ProjectJson.Serialize(letters));
        Assert.Contains("\"tagScheme\": \"PageLetterNumber\"", json);
        Assert.Equal(TagScheme.PageLetterNumber, ProjectJson.Deserialize(json).Value.TagScheme);
    }

    [Fact]
    public void Schema_4_file_without_a_tag_scheme_uses_letters()
    {
        var loaded = ProjectJson.Deserialize("""{ "schemaVersion": 4, "id": "0193a000-0000-7000-8000-000000000001", "name": "p" }""");

        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        Assert.Equal(TagScheme.LetterNumber, loaded.Value.TagScheme);
    }

    [Fact]
    public void Text_that_is_not_json_is_rejected()
    {
        Assert.Equal("proje.json_gecersiz", ProjectJson.Deserialize("bu json değil").Error!.Code);
    }

    [Theory]
    [InlineData("""{ "id": "0193a000-0000-7000-8000-000000000001", "name": "p" }""")]
    [InlineData("""{ "schemaVersion": "bir", "id": "0193a000-0000-7000-8000-000000000001", "name": "p" }""")]
    [InlineData("""{ "schemaVersion": 0, "id": "0193a000-0000-7000-8000-000000000001", "name": "p" }""")]
    [InlineData("[]")]
    public void Missing_or_invalid_schema_version_is_rejected(string json)
    {
        Assert.Equal("proje.sema_yok", ProjectJson.Deserialize(json).Error!.Code);
    }

    [Fact]
    public void Newer_schema_version_is_reported_as_such_even_with_unknown_fields()
    {
        var json = """{ "schemaVersion": 99, "id": "0193a000-0000-7000-8000-000000000001", "name": "p", "yeniAlan": 1 }""";

        Assert.Equal("proje.sema_yeni", ProjectJson.Deserialize(json).Error!.Code);
    }

    [Fact]
    public void Unknown_property_is_rejected_instead_of_silently_dropped()
    {
        var json = """{ "schemaVersion": 1, "id": "0193a000-0000-7000-8000-000000000001", "name": "p", "devcies": [] }""";

        Assert.Equal("proje.json_gecersiz", ProjectJson.Deserialize(json).Error!.Code);
    }

    [Fact]
    public void Missing_required_property_is_rejected()
    {
        var json = WithDevices("""[ { "id": "0193a000-0000-7000-8000-000000000002" } ]""");

        Assert.Equal("proje.json_gecersiz", ProjectJson.Deserialize(json).Error!.Code);
    }

    [Fact]
    public void Unknown_enum_value_is_rejected()
    {
        var json = WithDevices(
            """[ { "id": "0193a000-0000-7000-8000-000000000002", "tag": "K1", "kind": "Uydurma" } ]""");

        Assert.Equal("proje.json_gecersiz", ProjectJson.Deserialize(json).Error!.Code);
    }

    [Fact]
    public void Model_errors_keep_their_own_code()
    {
        var json = """
            {
              "schemaVersion": 1,
              "id": "0193a000-0000-7000-8000-000000000001",
              "name": "p",
              "connections": [
                {
                  "id": "0193a000-0000-7000-8000-000000000002",
                  "from": "0193a000-0000-7000-8000-000000000003",
                  "to": "0193a000-0000-7000-8000-000000000004"
                }
              ]
            }
            """;

        Assert.Equal("connection.uc_yok", ProjectJson.Deserialize(json).Error!.Code);
    }

    [Fact]
    public void Generated_page_kinds_and_their_part_round_trip_and_ordinary_pages_stay_compact()
    {
        var project = Project.Create(
            ProjectId.New(),
            "p",
            pages:
            [
                new Page(PageId.New(), "1", "Kapak", kind: PageKind.Cover),
                new Page(PageId.New(), "2", "Malzeme", kind: PageKind.PartsList),
                new Page(PageId.New(), "2.1", "Malzeme", kind: PageKind.PartsList, part: 1),
                new Page(PageId.New(), "3", "Şema"),
            ]).Value;

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Equal(
            [(PageKind.Cover, 0), (PageKind.PartsList, 0), (PageKind.PartsList, 1), (PageKind.Schematic, 0)],
            loaded.Pages.Select(page => (page.Kind, page.Part)));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"part\""));
        Assert.DoesNotContain("\"part\": 0", json, StringComparison.Ordinal);
        Assert.Equal(ProjectJson.Serialize(loaded), json);
    }

    [Fact]
    public void The_revision_history_round_trips_and_is_not_written_when_empty()
    {
        var empty = Project.Create(ProjectId.New(), "p").Value;
        Assert.DoesNotContain("revisions", ProjectJson.Serialize(empty), StringComparison.Ordinal);

        var project = Project.Create(
            ProjectId.New(),
            "p",
            revisions: [new Revision("A", "15.11.2026", "AKE", "İlk yayın"), new Revision("B", "19.11.2026", "AKE", "K7 eklendi")]).Value;

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Equal(project.Revisions, loaded.Revisions);
        Assert.Equal(json, ProjectJson.Serialize(loaded));
    }
}
