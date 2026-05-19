using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GenLauncherGO.Infrastructure.Updating.Support;

/// <summary>
///     Resolves legacy catalog share links into direct package download links.
/// </summary>
internal static class DownloadLinkResolver
{
    public static Uri ResolveDownloadUri(string link)
    {
        return new Uri(ResolveDirectDownloadLink(link), UriKind.Absolute);
    }

    /// <summary>
    ///     Converts supported share links into direct download links.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="link" /> is missing.
    /// </exception>
    public static string ResolveDirectDownloadLink(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            throw new ArgumentException(
                "Download link is missing from the modification metadata.",
                nameof(link));
        }

        if (link.Contains("www.dropbox.com", StringComparison.Ordinal))
        {
            link = link.Replace("?dl=0", "?dl=1", StringComparison.Ordinal);
        }

        if (link.Contains("https://onedrive.live.com", StringComparison.Ordinal))
        {
            link = ResolveOneDriveLink(link);
        }

        return link;
    }

    /// <summary>
    ///     Converts a supported OneDrive share or embed link to a direct download link.
    /// </summary>
    private static string ResolveOneDriveLink(string link)
    {
        if (link.Contains("embed", StringComparison.Ordinal))
        {
            return link.Replace("embed", "download", StringComparison.Ordinal);
        }

        List<string> linkParts = [.. link.Replace("https://onedrive.live.com/?", string.Empty, StringComparison.Ordinal).Split('&')];
        string? cid = linkParts.Where(t => t.Contains("cid=", StringComparison.Ordinal))
            .Select(t => t.Replace("cid=", string.Empty, StringComparison.Ordinal))
            .FirstOrDefault();
        string? authKey = linkParts.Where(t => t.Contains("authkey=", StringComparison.Ordinal))
            .Select(t => t.Replace("authkey=", string.Empty, StringComparison.Ordinal))
            .FirstOrDefault();
        string? resid = linkParts.Where(t =>
                t.Contains("id=", StringComparison.Ordinal) &&
                !t.Contains("cid=", StringComparison.Ordinal))
            .Select(t => t.Replace("id=", string.Empty, StringComparison.Ordinal))
            .FirstOrDefault();

        return string.Format(
            CultureInfo.InvariantCulture,
            "https://onedrive.live.com/download?cid={0}&resid={1}&authkey={2}",
            cid,
            resid,
            authKey);
    }
}
