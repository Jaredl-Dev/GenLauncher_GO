using System;
using GenLauncherGO.Infrastructure.Updating.Support;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Support;

public sealed class DownloadLinkResolverTests
{
    /// <summary>
    ///     The catalog is an external contract that hands over share links with their query parameters in whatever
    ///     order the sharing UI produced, so each part is located by name rather than by position.
    /// </summary>
    [Theory]
    [InlineData(
        "https://www.dropbox.com/s/example/Package.7z?dl=0",
        "https://www.dropbox.com/s/example/Package.7z?dl=1")]
    [InlineData(
        "https://onedrive.live.com/embed?cid=abc&resid=abc%211",
        "https://onedrive.live.com/download?cid=abc&resid=abc%211")]
    [InlineData(
        "https://onedrive.live.com/?authkey=%21key&cid=896C9369E9176506&id=896C9369E9176506%21464&parId=896C9369E9176506%21463&o=OneUp",
        "https://onedrive.live.com/download?cid=896C9369E9176506&resid=896C9369E9176506%21464&authkey=%21key")]
    [InlineData(
        "https://onedrive.live.com/?cid=896C9369E9176506&id=896C9369E9176506%21464&authkey=%21key",
        "https://onedrive.live.com/download?cid=896C9369E9176506&resid=896C9369E9176506%21464&authkey=%21key")]
    public void ResolveDirectDownloadLink_ConvertsSupportedShareLinks(
        string link,
        string expected)
    {
        string resolved = DownloadLinkResolver.ResolveDirectDownloadLink(link);

        resolved.Should().Be(expected);
    }

    /// <summary>
    ///     The share links come from a catalog nobody here controls, so a OneDrive address can arrive with its query
    ///     parameters truncated or renamed. Each part is resolved independently and a missing one leaves its slot
    ///     empty: the resulting address fails as a download, which the downloader already reports, rather than
    ///     throwing out of link resolution and taking the whole catalog load down with it.
    /// </summary>
    [Theory]
    [InlineData(
        "https://onedrive.live.com/?o=OneUp",
        "https://onedrive.live.com/download?cid=&resid=&authkey=")]
    [InlineData(
        "https://onedrive.live.com/?cid=896C9369E9176506&o=OneUp",
        "https://onedrive.live.com/download?cid=896C9369E9176506&resid=&authkey=")]
    public void ResolveDirectDownloadLink_OneDriveLinkMissingParts_LeavesThoseSlotsEmpty(
        string link,
        string expected)
    {
        string resolved = DownloadLinkResolver.ResolveDirectDownloadLink(link);

        resolved.Should().Be(expected);
    }

    /// <summary>
    ///     A catalog entry that carries no download link is rejected as bad metadata, instead of being carried into the
    ///     downloader as an unusable address.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveDirectDownloadLink_MissingLink_RejectsTheMetadata(string link)
    {
        Action resolve = () => DownloadLinkResolver.ResolveDirectDownloadLink(link);

        resolve.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(link));
    }
}
