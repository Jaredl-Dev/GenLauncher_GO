using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class FileSystemModificationImageFileServiceTests
{
    [Fact]
    public void FindExistingImageFilePathReturnsFirstMatchingImage()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string imagePath = Path.Combine(imageDirectory, "1.2.jpg");
        File.WriteAllText(imagePath, "image");
        FileSystemModificationImageFileService service = CreateService(paths);

        string? existingImagePath = service.FindExistingImageFilePath("ShockWave", "1.2");

        existingImagePath.Should().Be(imagePath);
    }

    [Fact]
    public void FindExistingImageFilePathReturnsNullForMissingDirectory()
    {
        using TestDirectory directory = new();
        FileSystemModificationImageFileService service = CreateService(CreatePaths(directory.Path));

        string? existingImagePath = service.FindExistingImageFilePath("Missing", "1.2");

        existingImagePath.Should().BeNull();
    }

    [Fact]
    public void CountImageFilesReturnsZeroForMissingDirectory()
    {
        using TestDirectory directory = new();
        FileSystemModificationImageFileService service = CreateService(CreatePaths(directory.Path));

        int count = service.CountImageFiles("Missing");

        count.Should().Be(0);
    }

    [Fact]
    public void CountImageFilesReturnsImageFileCount()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        File.WriteAllText(Path.Combine(imageDirectory, "1.0.png"), "image");
        File.WriteAllText(Path.Combine(imageDirectory, "1.1.jpg"), "image");
        FileSystemModificationImageFileService service = CreateService(paths);

        int count = service.CountImageFiles("ShockWave");

        count.Should().Be(2);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    public void ImageExistsReturnsFalseForMissingPathValues(string? imagePath, bool expected)
    {
        using TestDirectory directory = new();
        FileSystemModificationImageFileService service = CreateService(CreatePaths(directory.Path));

        bool exists = service.ImageExists(imagePath);

        exists.Should().Be(expected);
    }

    [Fact]
    public void ImageExistsReturnsTrueForExistingFile()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory.Path);
        string imagePath = paths.GetModificationImageFilePath("ShockWave", "1.2.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        File.WriteAllText(imagePath, "image");
        FileSystemModificationImageFileService service = CreateService(paths);

        bool exists = service.ImageExists(imagePath);

        exists.Should().BeTrue();
    }

    [Fact]
    public void ImageExistsReturnsFalseForExistingFileOutsideActiveCache()
    {
        using TestDirectory directory = new();
        string imagePath = Path.Combine(directory.Path, "image.png");
        File.WriteAllText(imagePath, "image");
        FileSystemModificationImageFileService service = CreateService(CreatePaths(directory.Path));

        bool exists = service.ImageExists(imagePath);

        exists.Should().BeFalse();
        File.Exists(imagePath).Should().BeTrue();
    }

    [Fact]
    public void TryDeleteImageRemovesMatchingCachedImages()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string pngImagePath = Path.Combine(imageDirectory, "1.2.png");
        string jpgImagePath = Path.Combine(imageDirectory, "1.2.jpg");
        File.WriteAllText(pngImagePath, "png");
        File.WriteAllText(jpgImagePath, "jpg");
        FileSystemModificationImageFileService service = CreateService(paths);

        bool deleted = service.TryDeleteImage("ShockWave", "1.2");

        deleted.Should().BeTrue();
        File.Exists(pngImagePath).Should().BeFalse();
        File.Exists(jpgImagePath).Should().BeFalse();
    }

    [Fact]
    public void TryDeleteImageRejectsUnsafeCacheIdentity()
    {
        using TestDirectory directory = new();
        string outsideImagePath = directory.CreateFile("victim.png", "outside");
        FileSystemModificationImageFileService service = CreateService(CreatePaths(directory.Path));

        bool deleted = service.TryDeleteImage("..", "victim");

        deleted.Should().BeFalse();
        File.ReadAllText(outsideImagePath).Should().Be("outside");
    }

    [Fact]
    public void TryDeleteImageReturnsTrueWhenFileDoesNotExist()
    {
        using TestDirectory directory = new();
        FileSystemModificationImageFileService service = CreateService(CreatePaths(directory.Path));

        bool deleted = service.TryDeleteImage("ShockWave", "missing");

        deleted.Should().BeTrue();
    }

    [SymbolicLinkFact]
    public void FindExistingImageFilePathRejectsLinkedImageDirectory()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        string outsideImagePath = Path.Combine(outsideDirectory, "1.2.png");
        File.WriteAllText(outsideImagePath, "outside");
        SymbolicLinkTestSupport.CreateDirectoryLink(
            paths.GetModificationImagesDirectory("ShockWave"),
            outsideDirectory);
        FileSystemModificationImageFileService service = CreateService(paths);

        Action act = () => service.FindExistingImageFilePath("ShockWave", "1.2");

        act.Should().Throw<InvalidDataException>();
        File.ReadAllText(outsideImagePath).Should().Be("outside");
    }

    [SymbolicLinkFact]
    public void TryDeleteImageDoesNotFollowLinkedImageDirectory()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        string outsideImagePath = Path.Combine(outsideDirectory, "1.2.png");
        File.WriteAllText(outsideImagePath, "outside");
        SymbolicLinkTestSupport.CreateDirectoryLink(
            paths.GetModificationImagesDirectory("ShockWave"),
            outsideDirectory);
        FileSystemModificationImageFileService service = CreateService(paths);

        bool deleted = service.TryDeleteImage("ShockWave", "1.2");

        deleted.Should().BeFalse();
        File.ReadAllText(outsideImagePath).Should().Be("outside");
    }

    [SymbolicLinkFact]
    public async Task ReplaceImageAsyncDoesNotFollowLinkedImageDirectoryAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        string outsideImagePath = Path.Combine(outsideDirectory, "1.2.jpg");
        File.WriteAllText(outsideImagePath, "outside");
        SymbolicLinkTestSupport.CreateDirectoryLink(
            paths.GetModificationImagesDirectory("ShockWave"),
            outsideDirectory);
        string sourceImagePath = directory.CreateFile("selected.png", "new");
        FileSystemModificationImageFileService service = CreateService(paths);

        Func<Task> act = () => service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        File.ReadAllText(outsideImagePath).Should().Be("outside");
        File.Exists(Path.Combine(outsideDirectory, "1.2.png")).Should().BeFalse();
    }

    [Fact]
    public async Task ReplaceImageAsyncDeletesStaleExtensionsAndCopiesSelectedImageAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string staleImagePath = Path.Combine(imageDirectory, "1.2.jpg");
        File.WriteAllText(staleImagePath, "old");
        string sourceImagePath = Path.Combine(directory.Path, "selected.png");
        File.WriteAllText(sourceImagePath, "new");
        FileSystemModificationImageFileService service = CreateService(paths);

        string destinationPath = await service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            CancellationToken.None);

        destinationPath.Should().Be(Path.Combine(imageDirectory, "1.2.png"));
        File.Exists(staleImagePath).Should().BeFalse();
        File.ReadAllText(destinationPath).Should().Be("new");
    }

    [Fact]
    public async Task ReplaceImageAsyncNoOpsWhenSourceAlreadyIsDestinationAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string existingImagePath = Path.Combine(imageDirectory, "1.2.png");
        File.WriteAllText(existingImagePath, "same");
        FileSystemModificationImageFileService service = CreateService(paths);

        string destinationPath = await service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", existingImagePath),
            CancellationToken.None);

        destinationPath.Should().Be(existingImagePath);
        File.ReadAllText(existingImagePath).Should().Be("same");
    }

    [Fact]
    public async Task ReplaceImageAsyncThrowsForSourceWithoutExtensionAsync()
    {
        using TestDirectory directory = new();
        string sourceImagePath = Path.Combine(directory.Path, "selected");
        File.WriteAllText(sourceImagePath, "new");
        FileSystemModificationImageFileService service = CreateService(CreatePaths(directory.Path));

        Func<Task> act = () => service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReplaceImageAsyncThrowsIOExceptionWhenSourceCannotBeCopiedAsync()
    {
        using TestDirectory directory = new();
        string sourceImagePath = Path.Combine(directory.Path, "missing.png");
        FileSystemModificationImageFileService service = CreateService(CreatePaths(directory.Path));

        Func<Task> act = () => service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            CancellationToken.None);

        (await act.Should().ThrowAsync<IOException>()
                .WithMessage("Could not replace cached image '1.2' for modification 'ShockWave'."))
            .Which.InnerException.Should().BeOfType<FileNotFoundException>();
    }

    [Fact]
    public async Task ReplaceImageAsyncHonorsPreCanceledTokenAsync()
    {
        using TestDirectory directory = new();
        string sourceImagePath = Path.Combine(directory.Path, "selected.png");
        File.WriteAllText(sourceImagePath, "new");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        FileSystemModificationImageFileService service = CreateService(CreatePaths(directory.Path));

        Func<Task> act = () => service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReplaceImageAsyncUsesNewGameCacheWithoutRebuildingServiceAsync()
    {
        using TestDirectory directory = new();
        string executableDirectory = directory.CreateDirectory("Launcher");
        var storagePaths = new LauncherStoragePaths(executableDirectory);
        LauncherPaths generalsPaths = storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            directory.CreateDirectory("GeneralsGame"));
        LauncherPaths zeroHourPaths = storagePaths.CreateGamePaths(
            SupportedGame.ZeroHour,
            directory.CreateDirectory("ZeroHourGame"));
        var runtimePaths = new LauncherRuntimePathContext(storagePaths, generalsPaths);
        var service = new FileSystemModificationImageFileService(
            runtimePaths,
            NullLogger<FileSystemModificationImageFileService>.Instance);
        string sourceImagePath = Path.Combine(directory.Path, "selected.png");
        File.WriteAllText(sourceImagePath, "image");
        var request = new ModificationImageReplacementRequest("Shared Mod", "1.0", sourceImagePath);

        string generalsImage = await service.ReplaceImageAsync(request, CancellationToken.None);
        runtimePaths.SwitchActive(zeroHourPaths);
        string zeroHourImage = await service.ReplaceImageAsync(request, CancellationToken.None);

        generalsImage.Should().StartWith(generalsPaths.ImagesDirectory);
        zeroHourImage.Should().StartWith(zeroHourPaths.ImagesDirectory);
        File.Exists(generalsImage).Should().BeTrue();
        File.Exists(zeroHourImage).Should().BeTrue();
    }

    private static FileSystemModificationImageFileService CreateService(LauncherPaths paths)
    {
        return new FileSystemModificationImageFileService(
            TestLauncherPaths.CreateRuntimePathContext(paths),
            NullLogger<FileSystemModificationImageFileService>.Instance);
    }

    private static LauncherPaths CreatePaths(string root)
    {
        return TestLauncherPaths.Create(root);
    }
}
