using System;
using System.Globalization;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Shared.Formatting;

internal static class PackageProgressTextFormatter
{
    public static bool TryFormat(
        PackageUpdateProgress progress,
        ILauncherStringLocalizer stringLocalizer,
        out string message,
        out int percentage)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(stringLocalizer);

        if (!progress.ProgressPercentage.HasValue)
        {
            message = string.Empty;
            percentage = 0;
            return false;
        }

        percentage = Convert.ToInt32(progress.ProgressPercentage.Value);
        message = String.Format(
            stringLocalizer["DownloadInProgress"],
            ByteSizeFormatter.Format(progress.BytesRead),
            ByteSizeFormatter.Format(progress.TotalBytes.GetValueOrDefault()));

        string speedText = FormatDownloadSpeed(progress.DownloadSpeedBytesPerSecond);
        string etaText = FormatEta(progress.EstimatedTimeRemaining);
        if (!String.IsNullOrEmpty(speedText))
        {
            message = $"{message} - {speedText}";
        }

        if (!String.IsNullOrEmpty(etaText))
        {
            message = $"{message} - {String.Format(
                stringLocalizer["EstimatedTimeRemaining"],
                etaText)}";
        }

        if (percentage == 100)
        {
            message = stringLocalizer["UnpackingPreparing"];
        }

        return true;
    }

    private static string FormatDownloadSpeed(double? bytesPerSecond)
    {
        if (!bytesPerSecond.HasValue || bytesPerSecond.Value <= 0)
        {
            return String.Empty;
        }

        return $"{ByteSizeFormatter.Format(bytesPerSecond.Value)}/s";
    }

    private static string FormatEta(TimeSpan? estimatedTimeRemaining)
    {
        if (!estimatedTimeRemaining.HasValue)
        {
            return String.Empty;
        }

        TimeSpan eta = estimatedTimeRemaining.Value;
        if (eta.TotalHours >= 1)
        {
            return string.Create(
                CultureInfo.CurrentCulture,
                $"{(int)eta.TotalHours:0}:{eta.Minutes:00}:{eta.Seconds:00}");
        }

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{eta.Minutes:00}:{eta.Seconds:00}");
    }
}
