using System;
using System.Globalization;

namespace GenLauncherGO.UI.Shared.Formatting;

/// <summary>
///     Formats byte counts with compact binary units for launcher package metadata and progress text.
/// </summary>
internal static class ByteSizeFormatter
{
    private const double BytesPerKilobyte = 1024D;

    private const double BytesPerMegabyte = BytesPerKilobyte * 1024D;

    private const double BytesPerGigabyte = BytesPerMegabyte * 1024D;

    public static string Format(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        return Format((double)bytes);
    }

    public static string Format(double bytes)
    {
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        if (bytes >= BytesPerGigabyte)
        {
            return FormatUnit(bytes / BytesPerGigabyte, "GB");
        }

        if (bytes >= BytesPerMegabyte)
        {
            return FormatUnit(bytes / BytesPerMegabyte, "MB");
        }

        if (bytes >= BytesPerKilobyte)
        {
            return FormatUnit(bytes / BytesPerKilobyte, "KB");
        }

        return $"{bytes.ToString("0", CultureInfo.CurrentCulture)} B";
    }

    private static string FormatUnit(double value, string unit)
    {
        return $"{value.ToString("0.#", CultureInfo.CurrentCulture)} {unit}";
    }
}
