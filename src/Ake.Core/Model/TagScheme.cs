namespace Ake.Core.Model;

/// <summary>Device tag'lerinin numaralandırma şeması; projenin ayarıdır.</summary>
public enum TagScheme
{
    /// <summary>Harf + sıra (<c>K1</c>); sıra aynı yapı düğümleri ve harf içinde sayılır.</summary>
    LetterNumber,

    /// <summary>
    /// Sayfa + harf + sıra (<c>3K1</c>): cihazın ilk göründüğü sayfanın numarası, harf ve o sayfada
    /// aynı harfi taşıyanlar arasındaki sırası.
    /// </summary>
    PageLetterNumber,
}
