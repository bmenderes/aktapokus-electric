namespace Ake.Core.Model;

/// <summary>
/// Bir revizyon kaydı (ADR-0050): revizyon işareti (A, B, 1…), tarih, hazırlayan ve ne değiştiği. Revizyon özeti sayfası bu kayıtları listeler;
/// antetteki <c>{project.revision}</c> son revizyonun işaretini gösterir.
/// </summary>
public sealed record Revision(string Mark, string Date, string Author, string Description);
