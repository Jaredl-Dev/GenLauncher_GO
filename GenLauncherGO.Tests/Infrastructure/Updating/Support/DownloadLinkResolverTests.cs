using GenLauncherGO.Infrastructure.Updating.Support;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Support;

public sealed class DownloadLinkResolverTests
{
    [Fact]
    public void ResolveDirectDownloadLink_ConvertsDropboxPreviewLinkToDownloadLink()
    {
        const string link = "https://www.dropbox.com/s/example/Package.7z?dl=0";

        string resolved = DownloadLinkResolver.ResolveDirectDownloadLink(link);

        resolved.Should().Be("https://www.dropbox.com/s/example/Package.7z?dl=1");
    }

    [Fact]
    public void ResolveDirectDownloadLink_ConvertsOneDriveEmbedLinkToDownloadLink()
    {
        const string link = "https://onedrive.live.com/embed?cid=abc&resid=abc%211";

        string resolved = DownloadLinkResolver.ResolveDirectDownloadLink(link);

        resolved.Should().Be("https://onedrive.live.com/download?cid=abc&resid=abc%211");
    }

    [Fact]
    public void ResolveDirectDownloadLink_ConvertsOneDriveShareLinkToDownloadLink()
    {
        const string link =
            "https://onedrive.live.com/?authkey=%21key&cid=896C9369E9176506&id=896C9369E9176506%21464&parId=896C9369E9176506%21463&o=OneUp";

        string resolved = DownloadLinkResolver.ResolveDirectDownloadLink(link);

        resolved.Should()
            .Be("https://onedrive.live.com/download?cid=896C9369E9176506&resid=896C9369E9176506%21464&authkey=%21key");
    }

}
