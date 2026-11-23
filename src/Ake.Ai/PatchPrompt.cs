using System.Text.Json.Nodes;

namespace Ake.Ai;

/// <summary>Patch üretimi için sistem istemi ve çıktıyı kısıtlayan JSON şeması (ADR-0004 patch biçimi).</summary>
public static class PatchPrompt
{
    public const string System = """
        Sen bir elektrik devre şeması asistanısın. Yalnızca verilen projede istenen değişikliği bir JSON patch olarak yaz; başka hiçbir şey yazma.
        Kurallar:
        - Çıktı {"description": "...", "commands": [ ... ]} biçiminde tek bir JSON nesnesidir.
        - Her komutun "op" alanı vardır: addDevice, removeDevice, connect, disconnect, assignWire, retag, assignIo, setProperty, addPage, assignSymbol.
        - Cihazlara ve bağlantı noktalarına TAG ile atıf yap: cihaz "=M1+P1-K1" ya da "-K1", bağlantı noktası "-K1:A1". Projede olmayan bir cihaza, yapı düğümüne ya da bağlantı noktasına atıf yapma; kimlik (GUID) uydurma.
        - addDevice: "tag", "kind" (Generic, TerminalStrip), "description", "functions": [{"kind": Coil|NoContact|NcContact|PowerContact|Terminal|Protection|Motor|PowerSupply|PlcChannel|Generic, "connectionPoints": ["A1","A2"]}]. Ürün sipariş kodunu uydurma; "product" yalnızca kullanıcı verirse yazılır.
        - Bağlantı noktası adını yalnızca özetteki cihazın "bağlantı noktaları" sütunundan al; olmayan bir nokta adı (örn. "5") yazma. Var olan bir bağlantıyı silerken "from" ve "to" değerlerini özetteki "Bağlantılar" listesinden aynen kopyala.
        - connect: "from" ve "to" bağlantı noktası tag'leridir; isteğe bağlı "wire": {"number", "crossSectionMm2", "color"}. Yeni eklenen cihazın noktalarına aynı patch'te atıf yapabilirsin.
        - Komut örnekleri (alanları aynen bu adlarla yaz, zorunlu alanları eksik bırakma):
          {"op":"addDevice","tag":"=M1+P1-K7","description":"Kontaktör","functions":[{"kind":"Coil","connectionPoints":["A1","A2"]}]}
          {"op":"removeDevice","device":"=M1+P1-K7"}
          {"op":"connect","from":"-K7:A2","to":"-T1:-","wire":{"number":"12"}}
          {"op":"disconnect","from":"-K7:A2","to":"-T1:-"}
          {"op":"retag","device":"-K1","tag":"-K9"}   (device: mevcut cihaz, tag: yeni tag)
          {"op":"setProperty","key":"Proje adı","value":"Yeni"}
          {"op":"addPage","number":"6","title":"Yeni sayfa"}
          {"op":"assignWire","from":"-K7:A2","to":"-T1:-","wire":{"number":"13","crossSectionMm2":1.5,"color":"BK"}}
        - Bir bağlantıyı (tel) silmek için "disconnect" kullan. "removeDevice" CİHAZI tamamen siler ve "device" alanı yalnızca cihaz tag'idir (":" içermez). "retag" yalnızca "device" ve "tag" alanlarını alır.
        - İstekte söylenen HER bağlantı için ayrı bir "connect" komutu yaz; cihazı ekleyip bağlantıyı yazmayı unutma.
        - Emin olmadığın şeyi uydurma; patch'e koyma. Yalnızca istenen değişikliği yap, başka cihaz ya da bağlantıyı silme.
        """;

    /// <summary>
    /// Çıktıyı kısıtlayan JSON şeması: her komut kendi alanlarıyla ayrı bir seçenektir (<c>anyOf</c>), zorunlu alanları vardır ve fazladan alan
    /// kabul etmez. Küçük modeller düz bir "bütün alanlar isteğe bağlı" şemasında alanları karıştırır (retag için value yazmak gibi).
    /// </summary>
    public static string Schema { get; } = BuildSchema();

    private static string BuildSchema()
    {
        static JsonObject Str() => new() { ["type"] = "string" };
        static JsonObject Operation(string op, string[] required, params (string Name, JsonNode Schema)[] properties)
        {
            var props = new JsonObject { ["op"] = new JsonObject { ["const"] = op } };
            foreach (var (name, schema) in properties)
            {
                props[name] = schema;
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = props,
                ["required"] = new JsonArray([.. new[] { "op" }.Concat(required).Select(name => (JsonNode)JsonValue.Create(name)!)]),
                ["additionalProperties"] = false,
            };
        }

        var wire = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["number"] = Str(),
                ["crossSectionMm2"] = new JsonObject { ["type"] = "number" },
                ["color"] = Str(),
            },
            ["additionalProperties"] = false,
        };
        var function = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["kind"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("Generic", "Coil", "NoContact", "NcContact", "PowerContact", "Terminal", "Protection", "Motor", "PowerSupply", "PlcChannel"),
                },
                ["description"] = Str(),
                ["ratedCurrentA"] = new JsonObject { ["type"] = "number" },
                ["connectionPoints"] = new JsonObject { ["type"] = "array", ["items"] = Str() },
            },
            ["required"] = new JsonArray("kind"),
            ["additionalProperties"] = false,
        };

        var operations = new JsonArray(
            Operation("addDevice", ["tag"], ("tag", Str()), ("kind", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Generic", "TerminalStrip") }), ("description", Str()), ("product", Str()), ("functions", new JsonObject { ["type"] = "array", ["items"] = function })),
            Operation("removeDevice", ["device"], ("device", Str())),
            Operation("connect", ["from", "to"], ("from", Str()), ("to", Str()), ("wire", wire.DeepClone())),
            Operation("disconnect", ["from", "to"], ("from", Str()), ("to", Str())),
            Operation("assignWire", ["from", "to"], ("from", Str()), ("to", Str()), ("wire", wire.DeepClone())),
            Operation("retag", ["device", "tag"], ("device", Str()), ("tag", Str())),
            Operation("assignIo", ["channel"], ("channel", Str()), ("function", Str()), ("symbolName", Str()), ("address", Str()), ("description", Str())),
            Operation("setProperty", ["key"], ("key", Str()), ("value", Str())),
            Operation("addPage", ["number", "title"], ("number", Str()), ("title", Str()), ("function", Str()), ("location", Str())),
            Operation("assignSymbol", ["function"], ("function", Str()), ("symbol", Str())));

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["description"] = Str(),
                ["commands"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["anyOf"] = operations } },
            },
            ["required"] = new JsonArray("commands"),
        }.ToJsonString();
    }
}
