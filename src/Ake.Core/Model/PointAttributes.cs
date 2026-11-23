namespace Ake.Core.Model;

/// <summary>Bağlantı ucunun elektriksel rolü (ADR-0033). <see cref="Unspecified"/>: bilgi yok.</summary>
public enum PinKind
{
    Unspecified,
    Power,
    Signal,
    Communication,
    ProtectiveEarth,
    Shield,

    /// <summary>Pnömatik (hava) bağlantı ucu: valf, silindir, hazırlama ünitesi portu (ADR-0052).</summary>
    Pneumatic,
}

/// <summary>Ucun fiziksel bağlantı biçimi.</summary>
public enum ConnectorType
{
    Unspecified,
    Screw,
    PushIn,
    Faston,
    M8,
    M12,
    Rj45,
    DSub,
    Other,

    /// <summary>Dişli pnömatik/hidrolik bağlantı (G1/8, G1/4, M5, NPT…); ölçü <c>ConnectorCode</c>'dadır.</summary>
    Thread,
}

/// <summary>Ucun kontak türü: normalde açık, normalde kapalı ya da değiştirmeli.</summary>
public enum ContactType
{
    Unspecified,
    NormallyOpen,
    NormallyClosed,
    Changeover,
}

/// <summary>Bir ucun isteğe bağlı özellikleri; sembol ucu ile bağlantı noktası aynı biçimi paylaşır.</summary>
/// <param name="ConnectorCode">Konektör kodu ve pin sayısı gibi serbest bilgi (örn. "A-kod 4 pin").</param>
public readonly record struct PointAttributes(
    PinKind Kind = PinKind.Unspecified,
    ConnectorType Connector = ConnectorType.Unspecified,
    ContactType Contact = ContactType.Unspecified,
    string? ConnectorCode = null)
{
    public const int MaxConnectorCodeLength = 40;

    public bool IsEmpty => this == default;

    /// <summary>Geçerliyse <c>null</c>.</summary>
    public string? Check() =>
        !Enum.IsDefined(Kind) || !Enum.IsDefined(Connector) || !Enum.IsDefined(Contact) ? "bilinmeyen uç özelliği değeri"
        : ConnectorCode is { Length: > MaxConnectorCodeLength } ? $"konektör kodu en çok {MaxConnectorCodeLength} karakter olabilir"
        : null;
}
