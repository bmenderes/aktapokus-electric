using System.Globalization;
using Ake.Core.Model;

namespace Ake.Core.Numbering;

/// <summary>Revizyon işareti önerisi (ADR-0050): ilk revizyon "A"; sonrakiler son işaretin devamıdır (A → B … Z → AA; 1 → 2).</summary>
public static class RevisionNumbering
{
    public static string NextMark(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var next = project.Revisions.Count == 0 ? "A" : Increment(project.Revisions[^1].Mark);
        while (project.Revisions.Any(revision => revision.Mark == next))
        {
            next = Increment(next);
        }

        return next;
    }

    private static string Increment(string mark)
    {
        if (int.TryParse(mark, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return (number + 1).ToString(CultureInfo.InvariantCulture);
        }

        // Harf işareti: sondaki Z → A ve soldaki harf artar (Z → AA, AZ → BA).
        var letters = mark.ToUpperInvariant().ToCharArray();
        for (var i = letters.Length - 1; i >= 0; i--)
        {
            if (letters[i] is >= 'A' and < 'Z')
            {
                letters[i]++;
                return new string(letters);
            }

            letters[i] = 'A';
        }

        return "A" + new string(letters);
    }
}
