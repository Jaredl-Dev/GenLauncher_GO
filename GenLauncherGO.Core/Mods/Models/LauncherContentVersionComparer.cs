using System;
using System.Collections.Generic;
using System.Linq;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
/// Compares launcher content version labels without converting their digits to a bounded integer.
/// </summary>
/// <remarks>
/// The remote catalog does not define a semantic-version contract. For compatibility, comparison retains the legacy
/// numeric projection: ASCII digits are compared in encounter order, the shorter projection is right-padded with
/// zeroes, labels without ASCII digits compare below numeric labels, and two labels without digits compare equally.
/// Punctuation and suffix text therefore do not define precedence. This comparer makes that boundary explicit while
/// avoiding integer overflow for arbitrarily long digit sequences.
/// </remarks>
internal sealed class LauncherContentVersionComparer : IComparer<string?>
{
    private LauncherContentVersionComparer()
    {
    }

    /// <summary>
    /// Gets the shared launcher content version comparer.
    /// </summary>
    public static LauncherContentVersionComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        string leftDigits = ExtractAsciiDigits(x);
        string rightDigits = ExtractAsciiDigits(y);

        if (leftDigits.Length == 0 || rightDigits.Length == 0)
        {
            return leftDigits.Length.CompareTo(rightDigits.Length);
        }

        int projectedLength = Math.Max(leftDigits.Length, rightDigits.Length);
        return String.CompareOrdinal(
            leftDigits.PadRight(projectedLength, '0'),
            rightDigits.PadRight(projectedLength, '0'));
    }

    private static string ExtractAsciiDigits(string? value)
    {
        return value is null
            ? String.Empty
            : new string(value.Where(IsAsciiDigit).ToArray());
    }

    private static bool IsAsciiDigit(char character)
    {
        return character is >= '0' and <= '9';
    }
}
