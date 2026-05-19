namespace GenLauncherGO.Core.Updating.Models;

/// <summary>
///     Describes the single, terminal outcome of a launcher package download.
/// </summary>
public sealed record PackageDownloadResult
{
    private PackageDownloadResult(
        PackageDownloadStatus status,
        string message)
    {
        Status = status;
        Message = message;
    }

    public PackageDownloadStatus Status { get; }

    public string Message { get; }

    public static PackageDownloadResult Succeeded()
    {
        return new PackageDownloadResult(
            PackageDownloadStatus.Succeeded,
            string.Empty);
    }

    public static PackageDownloadResult Canceled()
    {
        return new PackageDownloadResult(
            PackageDownloadStatus.Canceled,
            string.Empty);
    }

    /// <summary>
    ///     Creates a result for a download the launcher stopped to shut down, keeping its partial content to resume.
    /// </summary>
    public static PackageDownloadResult Suspended()
    {
        return new PackageDownloadResult(
            PackageDownloadStatus.Suspended,
            string.Empty);
    }

    /// <summary>
    ///     Creates an expected failure result that can normally be retried or corrected by the user.
    /// </summary>
    public static PackageDownloadResult RecoverableFailure(string message)
    {
        return new PackageDownloadResult(
            PackageDownloadStatus.RecoverableFailure,
            message);
    }

    /// <summary>
    ///     Creates an unexpected failure result while preserving diagnostic detail.
    /// </summary>
    public static PackageDownloadResult UnexpectedFailure(string message)
    {
        return new PackageDownloadResult(
            PackageDownloadStatus.UnexpectedFailure,
            message);
    }
}
