using System.Text;
using System.Text.RegularExpressions;

namespace Ake.Core.Model;

/// <summary>
/// IEC 81346 reference designation: <c>=Fonksiyon +Yer -Ürün</c>, isteğe bağlı <c>:BağlantıNoktası</c>.
/// Alanlar ön ek işareti olmadan tutulur; iç içe düğümler nokta ile ayrılır (<c>=A1.M1</c>).
/// </summary>
public sealed partial record ReferenceDesignation(
    string? Function,
    string? Location,
    string? Product,
    string? ConnectionPoint = null)
{
    [GeneratedRegex(@"^(?:=(?<f>[^=+\-:\s]+))?(?:\+(?<l>[^=+\-:\s]+))?(?:-(?<p>[^=+:\s]+))?(?::(?<c>\S+))?$")]
    private static partial Regex Pattern();

    /// <summary>Ürün tag'i boş olamaz; aspect işaretleri (<c>= + :</c>) ve boşluk içeremez.</summary>
    public static bool IsValidProductTag(string? tag) =>
        !string.IsNullOrEmpty(tag) && !tag.Any(c => c is '=' or '+' or ':' || char.IsWhiteSpace(c));

    public static Result<ReferenceDesignation> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Result.Failure<ReferenceDesignation>(new Error("tag.bos", "Tag boş olamaz."));
        }

        var match = Pattern().Match(trimmed);
        if (!match.Success)
        {
            return Result.Failure<ReferenceDesignation>(
                new Error("tag.gecersiz", $"Tag IEC 81346 biçimine uymuyor: '{trimmed}'"));
        }

        return Result.Success(new ReferenceDesignation(
            Optional(match.Groups["f"]),
            Optional(match.Groups["l"]),
            Optional(match.Groups["p"]),
            Optional(match.Groups["c"])));
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        if (Function is not null)
        {
            builder.Append('=').Append(Function);
        }

        if (Location is not null)
        {
            builder.Append('+').Append(Location);
        }

        if (Product is not null)
        {
            builder.Append('-').Append(Product);
        }

        if (ConnectionPoint is not null)
        {
            builder.Append(':').Append(ConnectionPoint);
        }

        return builder.ToString();
    }

    private static string? Optional(Group group) => group.Success ? group.Value : null;
}
