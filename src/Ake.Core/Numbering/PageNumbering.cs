using System.Globalization;
using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Numbering;

/// <summary>
/// Sayfa numaralandırma (ADR-0042). Araya eklenen sayfa sonraki sayfaları yeniden numaralamaz: 3. sayfadan sonra <c>3.1</c>, sonra <c>3.2</c>…
/// Baştan numaralandırma yalnızca kullanıcı isterse yapılır.
/// </summary>
public static class PageNumbering
{
    /// <summary>
    /// Verilen sıradaki sayfanın arkasına eklenecek sayfanın numarası: sayfanın ana numarası (ilk noktaya kadar) ve boş olan en küçük sıradaki alt
    /// numara değil, o ana numaradaki en büyük alt numaranın bir fazlası (<c>3</c> → <c>3.1</c>, <c>3.1</c> → <c>3.2</c>). Projede olmayan bir sıra için ana numara
    /// son sayfadan türetilir.
    /// </summary>
    public static string NextSubNumber(Project project, int afterIndex)
    {
        ArgumentNullException.ThrowIfNull(project);

        var basis = project.Pages.Count == 0
            ? "1"
            : project.Pages[Math.Clamp(afterIndex, 0, project.Pages.Count - 1)].Number;
        var root = basis.Contains('.', StringComparison.Ordinal) ? basis[..basis.IndexOf('.', StringComparison.Ordinal)] : basis;

        var highest = 0;
        foreach (var page in project.Pages)
        {
            if (page.Number.StartsWith(root + ".", StringComparison.Ordinal)
                && int.TryParse(page.Number[(root.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var sub)
                && sub > highest)
            {
                highest = sub;
            }
        }

        var next = highest + 1;
        while (project.Pages.Any(page => page.Number == $"{root}.{next.ToString(CultureInfo.InvariantCulture)}"))
        {
            next++;
        }

        return $"{root}.{next.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Sona eklenecek sayfanın numarası: ana numaraların (noktadan önceki kısım) en büyük tam sayısının bir fazlası (1..5 → 6; 3.1 ana numarası 3
    /// sayılır). Hiç sayısal numara yoksa 1'den başlar. Aynı numara varsa boş olan ilk sayıya kadar ilerler.
    /// </summary>
    public static string NextNumber(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var highest = 0;
        foreach (var page in project.Pages)
        {
            var root = page.Number.Contains('.', StringComparison.Ordinal) ? page.Number[..page.Number.IndexOf('.', StringComparison.Ordinal)] : page.Number;
            if (int.TryParse(root, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > highest)
            {
                highest = value;
            }
        }

        var next = highest + 1;
        while (project.Pages.Any(page => page.Number == next.ToString(CultureInfo.InvariantCulture)))
        {
            next++;
        }

        return next.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Baştan numaralar: sayfalar listedeki sıraya göre 1, 2, 3… olur (alt sayfa numaraları da gider). Tek adımda geri alınır.</summary>
    public static Transaction Renumber(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        // Geçici çakışmayı önlemek için sayfalar önce benzersiz geçici numaraya, sonra son numaraya alınır.
        var temporary = project.Pages.Select((page, index) => new SetPageNumber(page.Id, $"~{index.ToString(CultureInfo.InvariantCulture)}"));
        var final = project.Pages.Select((page, index) => new SetPageNumber(page.Id, (index + 1).ToString(CultureInfo.InvariantCulture)));
        var wanted = project.Pages.Select((page, index) => page.Number == (index + 1).ToString(CultureInfo.InvariantCulture)).All(same => same);
        return new Transaction("Sayfa numaraları baştan verildi", wanted ? [] : [.. temporary, .. final]);
    }

    /// <summary>
    /// İki sayfa numarasını doğal sayısal sırayla karşılaştırır (<c>1 &lt; 1.1 &lt; 1.2 &lt; 1.10 &lt; 2 &lt; 6A &lt; 10</c>).
    /// </summary>
    public static int CompareNumbers(string a, string b)
    {
        var sa = (a ?? string.Empty).Trim().Split('.');
        var sb = (b ?? string.Empty).Trim().Split('.');
        var count = Math.Min(sa.Length, sb.Length);
        for (var i = 0; i < count; i++)
        {
            var (hasA, numA, sufA) = ParseSegment(sa[i]);
            var (hasB, numB, sufB) = ParseSegment(sb[i]);
            if (hasA && hasB)
            {
                var numCmp = numA.CompareTo(numB);
                if (numCmp != 0)
                {
                    return numCmp;
                }

                var sufCmp = StringComparer.OrdinalIgnoreCase.Compare(sufA, sufB);
                if (sufCmp != 0)
                {
                    return sufCmp;
                }
            }
            else if (hasA != hasB)
            {
                return hasA ? -1 : 1;
            }
            else
            {
                var strCmp = StringComparer.OrdinalIgnoreCase.Compare(sa[i], sb[i]);
                if (strCmp != 0)
                {
                    return strCmp;
                }
            }
        }

        return sa.Length.CompareTo(sb.Length);

        static (bool HasNum, long Num, string Suffix) ParseSegment(string segment)
        {
            var digits = 0;
            while (digits < segment.Length && segment[digits] is >= '0' and <= '9')
            {
                digits++;
            }

            if (digits > 0 && long.TryParse(segment[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out var num))
            {
                return (true, num, segment[digits..]);
            }

            return (false, 0, segment);
        }
    }

    /// <summary>
    /// <paramref name="number"/> numaralı sayfanın doğal sayısal sıraya göre <paramref name="project"/> içindeki hedef indeksini
    /// (kaldırılan <paramref name="excludePageId"/> hariç listedeki yer) döndürür.
    /// </summary>
    public static int NaturalIndexFor(Project project, PageId? excludePageId, string number)
    {
        ArgumentNullException.ThrowIfNull(project);

        var others = project.Pages
            .Where(page => excludePageId is null || page.Id != excludePageId.Value)
            .ToList();

        var bestIndex = -1;
        string? bestNumber = null;
        for (var i = 0; i < others.Count; i++)
        {
            var candidate = others[i].Number;
            if (CompareNumbers(candidate, number) <= 0
                && (bestNumber is null || CompareNumbers(candidate, bestNumber) >= 0))
            {
                bestNumber = candidate;
                bestIndex = i;
            }
        }

        return bestIndex + 1;
    }
}
