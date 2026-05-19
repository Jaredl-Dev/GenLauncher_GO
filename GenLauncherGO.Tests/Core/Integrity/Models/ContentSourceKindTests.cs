using GenLauncherGO.Core.Integrity.Models;

namespace GenLauncherGO.Tests.Core.Integrity.Models;

public sealed class ContentSourceKindTests
{
    [Theory]
    [InlineData(ContentSourceKind.UnknownLegacy, false)]
    [InlineData(ContentSourceKind.ManagedS3, true)]
    [InlineData(ContentSourceKind.ManagedSingleFile, true)]
    [InlineData(ContentSourceKind.Manual, false)]
    public void IsManagedRemote_ClassifiesRestorableSources(
        ContentSourceKind sourceKind,
        bool expected)
    {
        bool isManagedRemote = sourceKind.IsManagedRemote();

        isManagedRemote.Should().Be(expected);
    }
}
