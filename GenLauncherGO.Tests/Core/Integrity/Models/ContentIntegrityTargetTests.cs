using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Integrity.Models;

namespace GenLauncherGO.Tests.Core.Integrity.Models;

public sealed class ContentIntegrityTargetTests
{
    [Fact]
    public void ConstructorDefensivelyCopiesIgnoredPaths()
    {
        HashSet<string> ignoredPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "inactive.png",
        };

        ContentIntegrityTarget target = new(
            "target",
            "Target",
            "content",
            ContentSourceKind.ManagedS3,
            ignoredPaths);
        ignoredPaths.Clear();

        target.IgnoredRelativePaths.Should().Contain("inactive.png");
    }

    [Fact]
    public void ConstructorCanonicalizesIgnoredPathsWithCaseInsensitiveWindowsSemantics()
    {
        ContentIntegrityTarget target = new(
            "target",
            "Target",
            "content",
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.Ordinal) { @"\Inactive\FILE.PNG/" });

        target.IgnoredRelativePaths.Contains("inactive/file.png").Should().BeTrue();
    }
}
