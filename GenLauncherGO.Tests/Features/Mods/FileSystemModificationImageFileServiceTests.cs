using System;
using System.IO;
using System.Threading.Tasks;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Features.Mods;

public sealed class FileSystemModificationImageFileServiceTests
{
    [Fact]
    public async Task ReplaceImageAsync_RemovesStaleExtensions_PreservesOtherVersionsAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string oldImage = Path.Combine(imageDirectory, "1.0.jpg");
        string otherImage = Path.Combine(imageDirectory, "2.0.jpg");
        await File.WriteAllTextAsync(oldImage, "old", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(otherImage, "other version", TestContext.Current.CancellationToken);
        string source = directory.CreateFile("replacement.png", "replacement");
        FileSystemModificationImageFileService service = new(
            TestLauncherPaths.CreateRuntimePathContext(paths),
            NullLogger<FileSystemModificationImageFileService>.Instance);

        string result = await service.ReplaceImageAsync(
            "ShockWave", "1.0", source,
            TestContext.Current.CancellationToken);

        File.Exists(oldImage).Should().BeFalse();
        (await File.ReadAllTextAsync(result, TestContext.Current.CancellationToken)).Should().Be("replacement");
        (await File.ReadAllTextAsync(otherImage, TestContext.Current.CancellationToken)).Should().Be("other version");
        (await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken)).Should().Be("replacement");
    }

    [Fact]
    public async Task ReplaceImageAsync_RedirectedCache_LeavesOutsideFilesUntouchedAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string targetDirectory = directory.CreateDirectory("Outside");
        string canary = directory.CreateFile("Outside/1.0.jpg", "outside image");
        ReparsePointTestSupport.CreateDirectoryJunction(
            paths.GetModificationImagesDirectory("ShockWave"), targetDirectory);
        string source = directory.CreateFile("replacement.png", "replacement");
        FileSystemModificationImageFileService service = new(
            TestLauncherPaths.CreateRuntimePathContext(paths),
            NullLogger<FileSystemModificationImageFileService>.Instance);

        Func<Task> act = () => service.ReplaceImageAsync(
            "ShockWave", "1.0", source,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IOException>();
        Directory.GetFiles(targetDirectory).Should().Equal(canary);
        (await File.ReadAllTextAsync(canary, TestContext.Current.CancellationToken)).Should().Be("outside image");
    }
}
