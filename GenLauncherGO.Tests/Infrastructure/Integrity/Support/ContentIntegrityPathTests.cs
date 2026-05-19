using System;
using System.IO;
using GenLauncherGO.Infrastructure.Integrity.Support;

namespace GenLauncherGO.Tests.Infrastructure.Integrity.Support;

public sealed class ContentIntegrityPathTests
{
    /// <summary>
    ///     Every relative path a scan produces is later resolved back against the target root and can be handed to a
    ///     delete, so one that already points outside the target must stop here rather than become a usable path.
    /// </summary>
    [Fact]
    public void GetRelativePath_PathOutsideRoot_Throws()
    {
        using TestDirectory directory = new();
        string root = directory.CreateDirectory("content");
        string outsidePath = directory.CreateFile("outside.txt", "outside");

        Action getRelativePath = () => ContentIntegrityPath.GetRelativePath(root, outsidePath);

        getRelativePath.Should().Throw<InvalidDataException>();
    }
}
