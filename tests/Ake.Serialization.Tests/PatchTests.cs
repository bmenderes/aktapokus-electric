using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;

namespace Ake.Serialization.Tests;

public class PatchTests
{
    private static readonly string Examples = Path.Combine(FindRepositoryRoot(), "examples", "ref-machine");

    private static Project RefMachine() => ProjectFile.Load(Path.Combine(Examples, "ref-machine.json")).Value;

    private static Patch ExamplePatch(string name) => PatchJson.Load(Path.Combine(Examples, "patches", name)).Value;

    private static string Commands(string commandsJson) =>
        $$"""{ "patchVersion": 1, "commands": [ {{commandsJson}} ] }""";

    private static Result Apply(Project project, string commandsJson)
    {
        var patch = PatchJson.Deserialize(Commands(commandsJson));
        Assert.True(patch.IsSuccess, patch.Error?.Message);
        return new CommandStack(project).Execute(patch.Value.ToCommand(), patch.Value.Source, "test");
    }

    private static Device Device(Project project, string tag) =>
        project.Devices.Single(device => device.ProductTag == tag);

    private static Channel Channel(Project project, string symbolName) =>
        project.Channels.Single(channel => channel.SymbolName == symbolName);

    // ---- DoD: referans projeye patch uygulanıp ERC raporu alınabiliyor

    [Fact]
    public void Example_patch_adds_a_contactor_wires_it_and_stays_erc_clean()
    {
        var project = RefMachine();
        var patch = ExamplePatch("01-yedek-kontaktor.json");
        var stack = new CommandStack(project);
        var before = ProjectJson.Serialize(project);

        var applied = stack.Execute(patch.ToCommand(), patch.Source, "test");

        Assert.True(applied.IsSuccess, applied.Error?.Message);
        Assert.True(project.CheckIntegrity().IsSuccess);
        Assert.Equal("=M1+P1-K3", project.DesignationOf(Device(project, "K3")).ToString());
        Assert.Equal(69, project.Connections.Count);
        Assert.Equal(2, Channel(project, "bK3Run").Index);
        Assert.Equal(7, Channel(project, "bK3Feedback").Index);
        Assert.Empty(ErcEngine.Default.Run(project).Findings);

        var after = ProjectJson.Serialize(project);
        Assert.True(stack.Undo().IsSuccess);
        Assert.Equal(before, ProjectJson.Serialize(project));
        Assert.True(stack.Redo().IsSuccess);
        Assert.Equal(after, ProjectJson.Serialize(project));
        Assert.Equal("Motor 1 için yedek kontaktör eklendi ve PLC'ye bağlandı", stack.Audit[0].Summary);
    }

    [Fact]
    public void Faulty_example_patch_applies_but_erc_reports_the_short_circuit()
    {
        var project = RefMachine();
        var patch = ExamplePatch("02-kisa-devre.json");

        var applied = new CommandStack(project).Execute(patch.ToCommand(), patch.Source, "test");
        var report = ErcEngine.Default.Run(project);

        Assert.True(applied.IsSuccess, applied.Error?.Message);
        Assert.True(report.HasErrors);
        Assert.Equal("erc.kisa_devre", Assert.Single(report.Findings).RuleId);
    }

    // ---- atıf çözme

    [Fact]
    public void Patch_is_all_or_nothing()
    {
        var project = RefMachine();
        var before = ProjectJson.Serialize(project);

        var result = Apply(
            project,
            """
            { "op": "addDevice", "tag": "=M1+P1-K7", "functions": [ { "kind": "Coil", "connectionPoints": ["A1", "A2"] } ] },
            { "op": "connect", "from": "-K7:A1", "to": "-YOK:1" }
            """);

        Assert.Equal("patch.bulunamadi", result.Error!.Code);
        Assert.Contains("Adım 2/2", result.Error.Message);
        Assert.Equal(before, ProjectJson.Serialize(project));
    }

    [Fact]
    public void Short_tag_matching_several_devices_is_rejected_as_ambiguous()
    {
        var project = RefMachine();

        var result = Apply(
            project,
            """
            { "op": "addDevice", "tag": "=M2+P1-K1", "functions": [ { "kind": "Coil", "connectionPoints": ["A1", "A2"] } ] },
            { "op": "connect", "from": "-K1:A2", "to": "-T1:-" }
            """);

        Assert.Equal("patch.tag_belirsiz", result.Error!.Code);
    }

    [Fact]
    public void Full_tag_resolves_what_a_short_tag_cannot()
    {
        var project = RefMachine();

        var result = Apply(
            project,
            """
            { "op": "addDevice", "tag": "=M2+P1-K1", "functions": [ { "kind": "Coil", "connectionPoints": ["A1", "A2"] } ] },
            { "op": "connect", "from": "=M2+P1-K1:A2", "to": "-T1:-" }
            """);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(66, project.Connections.Count);
    }

    [Fact]
    public void Objects_can_be_referenced_by_id()
    {
        var project = RefMachine();
        var contactor = Device(project, "K1");

        var result = Apply(project, $$"""{ "op": "removeDevice", "device": "{{contactor.Id}}" }""");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.DoesNotContain(contactor, project.Devices);
    }

    [Fact]
    public void AddDevice_does_not_create_missing_structure_nodes()
    {
        var result = Apply(RefMachine(), """{ "op": "addDevice", "tag": "=ZZ+P1-K9" }""");

        Assert.Equal("patch.dugum_yok", result.Error!.Code);
    }

    [Fact]
    public void AddDevice_accepts_a_caller_supplied_id()
    {
        var project = RefMachine();

        Apply(project, """{ "op": "addDevice", "tag": "-K9", "id": "0193a000-0000-7000-8000-0000000000aa" }""");

        Assert.Equal(Guid.Parse("0193a000-0000-7000-8000-0000000000aa"), Device(project, "K9").Id.Value);
    }

    [Fact]
    public void Retag_changes_only_the_aspects_that_are_written()
    {
        var project = RefMachine();
        var contactor = Device(project, "K1");

        Assert.True(Apply(project, """{ "op": "retag", "device": "=M1+P1-K1", "tag": "-K7" }""").IsSuccess);
        Assert.Equal("=M1+P1-K7", project.DesignationOf(contactor).ToString());

        Assert.True(Apply(project, """{ "op": "retag", "device": "-K7", "tag": "=E1" }""").IsSuccess);
        Assert.Equal("=E1+P1-K7", project.DesignationOf(contactor).ToString());
    }

    [Fact]
    public void Disconnect_finds_the_connection_from_either_end()
    {
        var project = RefMachine();

        var result = Apply(project, """{ "op": "disconnect", "from": "-K1:A1", "to": "-K0:14" }""");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(64, project.Connections.Count);
    }

    [Fact]
    public void AssignWire_keeps_the_identity_of_the_existing_wire()
    {
        var project = RefMachine();
        var connection = project.Connections[0];
        var wireId = connection.Wire!.Id;

        var result = Apply(
            project,
            """{ "op": "assignWire", "from": "-X0:L1.a", "to": "-Q1:1", "wire": { "number": "L1", "crossSectionMm2": 4, "color": "BK" } }""");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(wireId, connection.Wire!.Id);
        Assert.Equal("L1", connection.Wire.Number);
        Assert.Equal(4m, connection.Wire.CrossSectionMm2);
    }

    [Fact]
    public void Connect_can_use_a_free_cable_core()
    {
        var project = RefMachine();

        var result = Apply(
            project,
            """
            { "op": "disconnect", "from": "-X1:1.b", "to": "-M1:U1" },
            { "op": "connect", "from": "-X1:1.b", "to": "-M1:U1", "core": "-W1:1" }
            """);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.NotNull(project.Connections[^1].CoreId);
    }

    [Fact]
    public void AssignIo_leaves_unwritten_fields_untouched()
    {
        var project = RefMachine();
        var channel = Channel(project, "bStartButton");

        var result = Apply(project, """{ "op": "assignIo", "channel": "-A11:1", "address": "%IX0.0" }""");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("%IX0.0", channel.Address);
        Assert.Equal("bStartButton", channel.SymbolName);
        Assert.Equal("Start butonu", channel.Description);
    }

    // ---- kütüphane ürünü

    [Fact]
    public void AddDevice_can_take_its_functions_from_a_product_catalog()
    {
        var project = RefMachine();
        var patch = PatchJson.Deserialize(Commands(
            """
            { "op": "addDevice", "tag": "=M2+P1-K3", "product": "C-9", "description": "Yedek" },
            { "op": "connect", "from": "-K3:A2", "to": "-T1:-" }
            """)).Value;

        var result = new CommandStack(project).Execute(patch.ToCommand(new FakeCatalog()), patch.Source, "test");

        Assert.True(result.IsSuccess, result.Error?.Message);
        var device = Device(project, "K3");
        Assert.Equal(new PartRef("Acme", "C-9"), device.Part);
        Assert.Equal("Yedek", device.Description);
        Assert.Equal(["A1", "A2", "13", "14"], device.Functions.SelectMany(f => f.ConnectionPoints).Select(p => p.Name));
        Assert.Equal("=M2+P1-K3", project.DesignationOf(device).ToString());
    }

    [Fact]
    public void AddDevice_with_a_product_fails_cleanly_when_it_cannot_be_resolved()
    {
        var withProduct = """{ "op": "addDevice", "tag": "-K9", "product": "C-9" }""";
        var unknown = """{ "op": "addDevice", "tag": "-K9", "product": "YOK" }""";
        var conflicting = """{ "op": "addDevice", "tag": "-K9", "product": "C-9", "functions": [] }""";

        Assert.Equal("patch.kutuphane_yok", Apply(RefMachine(), withProduct).Error!.Code);
        Assert.Equal("test.urun_yok", ApplyWithCatalog(unknown).Error!.Code);
        Assert.Equal("patch.urun_cakisma", ApplyWithCatalog(conflicting).Error!.Code);

        static Result ApplyWithCatalog(string commandsJson)
        {
            var patch = PatchJson.Deserialize(Commands(commandsJson)).Value;
            return new CommandStack(RefMachine()).Execute(patch.ToCommand(new FakeCatalog()), patch.Source, "test");
        }
    }

    [Fact]
    public void AssignSymbol_copies_a_symbol_from_the_symbol_library_when_the_project_does_not_have_it()
    {
        var project = RefMachine();
        var patch = PatchJson.Deserialize(Commands("""{ "op": "assignSymbol", "function": "-F3:1", "symbol": "kutuphane/Sigorta" }""")).Value;
        var stack = new CommandStack(project);

        var result = stack.Execute(patch.ToCommand(symbols: new FakeSymbols()), patch.Source, "test");

        Assert.True(result.IsSuccess, result.Error?.Message);
        var copied = Assert.Single(project.Symbols);
        Assert.Equal(copied.Id, Device(project, "F3").Functions[0].SymbolId);

        stack.Undo();
        Assert.Empty(project.Symbols);
        Assert.Null(Device(project, "F3").Functions[0].SymbolId);
    }

    [Fact]
    public void Symbol_missing_from_both_project_and_library_fails_with_the_library_reason()
    {
        var patch = PatchJson.Deserialize(Commands("""{ "op": "assignSymbol", "function": "-F3:1", "symbol": "yok" }""")).Value;

        var withLibrary = new CommandStack(RefMachine()).Execute(patch.ToCommand(symbols: new FakeSymbols()), patch.Source, "test");
        var withoutLibrary = new CommandStack(RefMachine()).Execute(patch.ToCommand(), patch.Source, "test");

        Assert.Equal("test.sembol_yok", withLibrary.Error!.Code);
        Assert.Equal("patch.bulunamadi", withoutLibrary.Error!.Code);
    }

    private sealed class FakeSymbols : Ake.Core.Catalog.ISymbolCatalog
    {
        private static readonly SymbolDefinition Fuse = new(
            new SymbolId(Guid.Parse("0193e000-0000-7000-8000-000000000001")),
            "Sigorta",
            [new SymbolRect(-1.5, 4, 3, 7), new SymbolLine(0, 0, 0, 15)],
            [new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 15, 0, 1)],
            "test",
            "CC0");

        public Result<SymbolDefinition> FindSymbol(string reference) =>
            reference == "kutuphane/Sigorta"
                ? Result.Success(Fuse)
                : Result.Failure<SymbolDefinition>(new Error("test.sembol_yok", "yok"));
    }

    private sealed class FakeCatalog : Ake.Core.Catalog.IProductCatalog
    {
        public Result<Ake.Core.Catalog.ProductDefinition> Find(string orderCode, string? manufacturer = null) =>
            orderCode == "C-9"
                ? Result.Success(new Ake.Core.Catalog.ProductDefinition(
                    "Acme",
                    "C-9",
                    [
                        new Ake.Core.Catalog.FunctionTemplate(FunctionKind.Coil, ["A1", "A2"]),
                        new Ake.Core.Catalog.FunctionTemplate(FunctionKind.NoContact, ["13", "14"]),
                    ]))
                : Result.Failure<Ake.Core.Catalog.ProductDefinition>(new Error("test.urun_yok", "yok"));
    }

    // ---- dosya biçimi

    [Fact]
    public void Source_and_actor_are_read_from_the_patch()
    {
        var patch = PatchJson.Deserialize(
            """{ "patchVersion": 1, "source": "ai", "actor": "asistan", "commands": [] }""").Value;

        Assert.Equal(ChangeSource.Ai, patch.Source);
        Assert.Equal("asistan", patch.Actor);
        Assert.Equal(0, patch.CommandCount);
    }

    [Theory]
    [InlineData("""{ "commands": [] }""", "patch.sema_yok")]
    [InlineData("""{ "patchVersion": 2, "commands": [] }""", "patch.sema_yeni")]
    [InlineData("""{ "patchVersion": 1 }""", "patch.json_gecersiz")]
    [InlineData("""{ "patchVersion": 1, "commands": [ { "op": "uydurma" } ] }""", "patch.json_gecersiz")]
    [InlineData("""{ "patchVersion": 1, "commands": [ { "op": "connect", "from": "-K1:A1" } ] }""", "patch.json_gecersiz")]
    [InlineData("""{ "patchVersion": 1, "commands": [ { "op": "retag", "device": "-K1", "tag": "-K2", "x": 1 } ] }""", "patch.json_gecersiz")]
    [InlineData("bozuk", "patch.json_gecersiz")]
    public void Malformed_patch_files_are_rejected(string json, string expectedCode)
    {
        Assert.Equal(expectedCode, PatchJson.Deserialize(json).Error!.Code);
    }

    [Fact]
    public void Loading_a_missing_patch_file_fails_without_throwing()
    {
        var result = PatchJson.Load(Path.Combine(Path.GetTempPath(), $"ake-yok-{Guid.NewGuid()}.json"));

        Assert.Equal("patch.dosya_okunamadi", result.Error!.Code);
    }

    // ---- audit log

    [Fact]
    public void Audit_log_is_appended_one_json_line_per_entry()
    {
        var directory = Directory.CreateTempSubdirectory("ake-test-");
        try
        {
            var path = AuditLog.PathFor(Path.Combine(directory.FullName, "proje.json"));
            var at = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
            var first = new AuditEntry(at, ChangeSource.User, "barış", AuditAction.Execute, "Device eklendi: -K1");
            var second = new AuditEntry(at, ChangeSource.Ai, "asistan", AuditAction.Undo, "Device eklendi: -K1");

            Assert.True(AuditLog.Append(path, [first]).IsSuccess);
            Assert.True(AuditLog.Append(path, [second]).IsSuccess);

            Assert.EndsWith("proje.audit.jsonl", path);
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Equal(
                """{"at":"2026-11-15T10:00:00+00:00","source":"User","actor":"barış","action":"Execute","summary":"Device eklendi: -K1"}""",
                lines[0]);
            Assert.Contains("\"source\":\"Ai\"", lines[1]);
            Assert.Contains("\"action\":\"Undo\"", lines[1]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }
}
