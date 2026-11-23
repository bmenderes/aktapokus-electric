namespace Ake.Core;

/// <summary>Beklenen bir başarısızlığın makine-okunur kodu ve insan-okunur açıklaması.</summary>
public sealed record Error(string Code, string Message);
