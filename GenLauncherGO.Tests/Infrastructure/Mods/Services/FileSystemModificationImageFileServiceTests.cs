using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class FileSystemModificationImageFileServiceTests
{
    [Fact]
    public void FindExistingImageFilePath_FindsCachedImageWhateverItsExtension()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string imagePath = Path.Combine(imageDirectory, "1.2.jpg");
        File.WriteAllText(imagePath, "image");
        File.WriteAllText(Path.Combine(imageDirectory, "2.0.png"), "other image");
        FileSystemModificationImageFileService service = CreateService(paths);

        string? existingImagePath = service.FindExistingImageFilePath("ShockWave", "1.2");

        existingImagePath.Should().Be(imagePath);
    }

    /// <summary>
    ///     Characterizes today's behaviour, which is not the behaviour anyone wants: the cache lookup builds a
    ///     <c>&lt;version&gt;.*</c> wildcard, so version "1.2" also matches the artwork cached for "1.2.5". Revisit
    ///     with PR-11, which replaces the wildcard with an exact base-name match.
    /// </summary>
    [Fact]
    public void FindExistingImageFilePath_MatchesTheImageOfAVersionThatOnlyExtendsTheRequestedName()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string siblingImagePath = Path.Combine(imageDirectory, "1.2.5.png");
        File.WriteAllText(siblingImagePath, "sibling");
        FileSystemModificationImageFileService service = CreateService(paths);

        string? existingImagePath = service.FindExistingImageFilePath("ShockWave", "1.2");

        existingImagePath.Should().Be(siblingImagePath);
    }

    /// <summary>
    ///     Characterizes today's behaviour: the same <c>&lt;version&gt;.*</c> wildcard makes removing "1.2" take
    ///     "1.2.5" with it. Revisit with PR-11.
    /// </summary>
    [Fact]
    public void TryDeleteImage_AlsoRemovesTheImageOfAVersionThatOnlyExtendsTheRequestedName()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string requestedImagePath = Path.Combine(imageDirectory, "1.2.png");
        string siblingImagePath = Path.Combine(imageDirectory, "1.2.5.png");
        File.WriteAllText(requestedImagePath, "requested");
        File.WriteAllText(siblingImagePath, "sibling");
        FileSystemModificationImageFileService service = CreateService(paths);

        bool deleted = service.TryDeleteImage("ShockWave", "1.2");

        deleted.Should().BeTrue();
        File.Exists(requestedImagePath).Should().BeFalse();
        File.Exists(siblingImagePath).Should().BeFalse();
    }

    /// <summary>
    ///     The wildcard is anchored on the whole requested name, so the over-match only ever runs one way: removing
    ///     "1.2" may take "1.2.5" with it, but it must never take the artwork of "1", which is a different version the
    ///     user still has installed.
    /// </summary>
    [Fact]
    public void TryDeleteImage_KeepsTheImageOfTheVersionWhoseNameTheRequestedNameExtends()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string requestedImagePath = Path.Combine(imageDirectory, "1.2.png");
        string shorterVersionImagePath = Path.Combine(imageDirectory, "1.png");
        File.WriteAllText(requestedImagePath, "requested");
        File.WriteAllText(shorterVersionImagePath, "shorter");
        FileSystemModificationImageFileService service = CreateService(paths);

        bool deleted = service.TryDeleteImage("ShockWave", "1.2");

        deleted.Should().BeTrue();
        File.Exists(requestedImagePath).Should().BeFalse();
        File.ReadAllText(shorterVersionImagePath).Should().Be("shorter");
    }

    [Fact]
    public void FindExistingImageFilePath_ReturnsNullWhenNoCachedImageMatches()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        File.WriteAllText(Path.Combine(imageDirectory, "1.2.png"), "image");
        FileSystemModificationImageFileService service = CreateService(paths);

        string? existingImagePath = service.FindExistingImageFilePath("ShockWave", "3.0");

        existingImagePath.Should().BeNull();
    }

    /// <summary>
    ///     Characterizes today's behaviour: the stale-sibling sweep before a replacement uses the same
    ///     <c>&lt;version&gt;.*</c> wildcard, so replacing "1.2" also discards the artwork of "1.2.5". Revisit with
    ///     PR-11.
    /// </summary>
    [Fact]
    public async Task ReplaceImageAsync_AlsoRemovesTheImageOfAVersionThatOnlyExtendsTheRequestedNameAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string siblingImagePath = Path.Combine(imageDirectory, "1.2.5.png");
        await File.WriteAllTextAsync(siblingImagePath, "sibling");
        string sourceImagePath = directory.CreateFile("selected.png", "new");
        FileSystemModificationImageFileService service = CreateService(paths);

        string destinationPath = await service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            CancellationToken.None);

        destinationPath.Should().Be(Path.Combine(imageDirectory, "1.2.png"));
        File.Exists(siblingImagePath).Should().BeFalse();
        (await File.ReadAllTextAsync(destinationPath)).Should().Be("new");
    }

    [Fact]
    public void FindExistingImageFilePath_ReturnsNullForMissingDirectory()
    {
        using TestDirectory directory = new();
        FileSystemModificationImageFileService service = CreateService(TestLauncherPaths.Create(directory.Path));

        string? existingImagePath = service.FindExistingImageFilePath("Missing", "1.2");

        existingImagePath.Should().BeNull();
    }

    [Fact]
    public void CountImageFiles_ReturnsZeroForMissingDirectory()
    {
        using TestDirectory directory = new();
        FileSystemModificationImageFileService service = CreateService(TestLauncherPaths.Create(directory.Path));

        int count = service.CountImageFiles("Missing");

        count.Should().Be(0);
    }

    [Fact]
    public void CountImageFiles_ReturnsImageFileCount()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        File.WriteAllText(Path.Combine(imageDirectory, "1.0.png"), "image");
        File.WriteAllText(Path.Combine(imageDirectory, "1.1.jpg"), "image");
        FileSystemModificationImageFileService service = CreateService(paths);

        int count = service.CountImageFiles("ShockWave");

        count.Should().Be(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ImageExists_ReturnsFalseForMissingPathValues(string? imagePath)
    {
        using TestDirectory directory = new();
        FileSystemModificationImageFileService service = CreateService(TestLauncherPaths.Create(directory.Path));

        bool exists = service.ImageExists(imagePath);

        exists.Should().BeFalse();
    }

    [Fact]
    public void ImageExists_ReturnsTrueForExistingFile()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        string imagePath = paths.GetModificationImageFilePath("ShockWave", "1.2.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        File.WriteAllText(imagePath, "image");
        FileSystemModificationImageFileService service = CreateService(paths);

        bool exists = service.ImageExists(imagePath);

        exists.Should().BeTrue();
    }

    [Fact]
    public void ImageExists_ReturnsFalseForExistingFileOutsideActiveCache()
    {
        using TestDirectory directory = new();
        string imagePath = Path.Combine(directory.Path, "image.png");
        File.WriteAllText(imagePath, "image");
        FileSystemModificationImageFileService service = CreateService(TestLauncherPaths.Create(directory.Path));

        bool exists = service.ImageExists(imagePath);

        exists.Should().BeFalse();
        File.Exists(imagePath).Should().BeTrue();
    }

    [Fact]
    public void TryDeleteImage_RemovesMatchingCachedImages()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
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
    public void TryDeleteImage_RejectsUnsafeCacheIdentity()
    {
        using TestDirectory directory = new();
        string outsideImagePath = directory.CreateFile("victim.png", "outside");
        FileSystemModificationImageFileService service = CreateService(TestLauncherPaths.Create(directory.Path));

        bool deleted = service.TryDeleteImage("..", "victim");

        deleted.Should().BeFalse();
        File.ReadAllText(outsideImagePath).Should().Be("outside");
    }

    [Fact]
    public void TryDeleteImage_ReturnsTrueWhenFileDoesNotExist()
    {
        using TestDirectory directory = new();
        FileSystemModificationImageFileService service = CreateService(TestLauncherPaths.Create(directory.Path));

        bool deleted = service.TryDeleteImage("ShockWave", "missing");

        deleted.Should().BeTrue();
    }

    [Fact]
    public void FindExistingImageFilePath_RejectsLinkedImageDirectory()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        string outsideImagePath = Path.Combine(outsideDirectory, "1.2.png");
        File.WriteAllText(outsideImagePath, "outside");
        ReparsePointTestSupport.CreateDirectoryJunction(
            paths.GetModificationImagesDirectory("ShockWave"),
            outsideDirectory);
        FileSystemModificationImageFileService service = CreateService(paths);

        Action act = () => service.FindExistingImageFilePath("ShockWave", "1.2");

        act.Should().Throw<InvalidDataException>();
        File.ReadAllText(outsideImagePath).Should().Be("outside");
    }

    [Fact]
    public void TryDeleteImage_DoesNotFollowLinkedImageDirectory()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        string outsideImagePath = Path.Combine(outsideDirectory, "1.2.png");
        File.WriteAllText(outsideImagePath, "outside");
        ReparsePointTestSupport.CreateDirectoryJunction(
            paths.GetModificationImagesDirectory("ShockWave"),
            outsideDirectory);
        FileSystemModificationImageFileService service = CreateService(paths);

        bool deleted = service.TryDeleteImage("ShockWave", "1.2");

        deleted.Should().BeFalse();
        File.ReadAllText(outsideImagePath).Should().Be("outside");
    }

    [Fact]
    public async Task ReplaceImageAsync_DoesNotFollowLinkedImageDirectoryAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        string outsideImagePath = Path.Combine(outsideDirectory, "1.2.jpg");
        await File.WriteAllTextAsync(outsideImagePath, "outside");
        ReparsePointTestSupport.CreateDirectoryJunction(
            paths.GetModificationImagesDirectory("ShockWave"),
            outsideDirectory);
        string sourceImagePath = directory.CreateFile("selected.png", "new");
        FileSystemModificationImageFileService service = CreateService(paths);

        Func<Task> act = () => service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(outsideImagePath)).Should().Be("outside");
        File.Exists(Path.Combine(outsideDirectory, "1.2.png")).Should().BeFalse();
    }

    [Fact]
    public async Task ReplaceImageAsync_DeletesStaleExtensionsAndCopiesSelectedImageAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string staleImagePath = Path.Combine(imageDirectory, "1.2.jpg");
        await File.WriteAllTextAsync(staleImagePath, "old");
        string sourceImagePath = Path.Combine(directory.Path, "selected.png");
        await File.WriteAllTextAsync(sourceImagePath, "new");
        FileSystemModificationImageFileService service = CreateService(paths);

        string destinationPath = await service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            CancellationToken.None);

        destinationPath.Should().Be(Path.Combine(imageDirectory, "1.2.png"));
        File.Exists(staleImagePath).Should().BeFalse();
        (await File.ReadAllTextAsync(destinationPath)).Should().Be("new");
    }

    [Fact]
    public async Task ReplaceImageAsyncNoOpsWhenSourceAlready_IsDestinationAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string existingImagePath = Path.Combine(imageDirectory, "1.2.png");
        await File.WriteAllTextAsync(existingImagePath, "same");
        FileSystemModificationImageFileService service = CreateService(paths);

        string destinationPath = await service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", existingImagePath),
            CancellationToken.None);

        destinationPath.Should().Be(existingImagePath);
        (await File.ReadAllTextAsync(existingImagePath)).Should().Be("same");
    }

    [Fact]
    public async Task ReplaceImageAsync_ThrowsForSourceWithoutExtensionAsync()
    {
        using TestDirectory directory = new();
        string sourceImagePath = Path.Combine(directory.Path, "selected");
        await File.WriteAllTextAsync(sourceImagePath, "new");
        FileSystemModificationImageFileService service = CreateService(TestLauncherPaths.Create(directory.Path));

        Func<Task> act = () => service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ReSharper disable once InconsistentNaming
    [Fact]
    public async Task ReplaceImageAsync_ThrowsIOExceptionWhenSourceCannotBeCopiedAsync()
    {
        using TestDirectory directory = new();
        string sourceImagePath = Path.Combine(directory.Path, "missing.png");
        FileSystemModificationImageFileService service = CreateService(TestLauncherPaths.Create(directory.Path));

        Func<Task> act = () => service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            CancellationToken.None);

        (await act.Should().ThrowAsync<IOException>()
                .WithMessage("Could not replace cached image '1.2' for modification 'ShockWave'."))
            .Which.InnerException.Should().BeOfType<FileNotFoundException>();
    }

    [Fact]
    public async Task ReplaceImageAsync_HonorsPreCanceledTokenAsync()
    {
        using TestDirectory directory = new();
        string sourceImagePath = Path.Combine(directory.Path, "selected.png");
        await File.WriteAllTextAsync(sourceImagePath, "new");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        FileSystemModificationImageFileService service = CreateService(TestLauncherPaths.Create(directory.Path));

        Func<Task> act = () => service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReplaceImageAsync_UsesNewGameCacheWithoutRebuildingServiceAsync()
    {
        using TestDirectory directory = new();
        (LauncherRuntimePathContext runtimePaths, LauncherPaths generalsPaths, LauncherPaths zeroHourPaths) =
            TestLauncherPaths.CreateTwoGameRuntime(directory);
        var service = new FileSystemModificationImageFileService(
            runtimePaths,
            NullLogger<FileSystemModificationImageFileService>.Instance);
        string sourceImagePath = directory.CreateFile("selected.png", "image");
        var request = new ModificationImageReplacementRequest("Shared Mod", "1.0", sourceImagePath);

        string zeroHourImage = await service.ReplaceImageAsync(request, CancellationToken.None);
        runtimePaths.SwitchActive(generalsPaths);
        string generalsImage = await service.ReplaceImageAsync(request, CancellationToken.None);

        generalsImage.Should().StartWith(generalsPaths.ImagesDirectory);
        zeroHourImage.Should().StartWith(zeroHourPaths.ImagesDirectory);
        File.Exists(generalsImage).Should().BeTrue();
        File.Exists(zeroHourImage).Should().BeTrue();
    }

    /// <summary>
    ///     Ownership of a cache folder is not settled by the folder alone: one link below it leads somewhere the
    ///     launcher never created, so the lookup refuses the folder rather than reporting a path from it.
    /// </summary>
    [Fact]
    public void FindExistingImageFilePath_RejectsImageDirectoryContainingALink()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        File.WriteAllText(Path.Combine(imageDirectory, "1.2.png"), "image");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(imageDirectory, "Linked"));
        FileSystemModificationImageFileService service = CreateService(paths);

        Action act = () => service.FindExistingImageFilePath("ShockWave", "1.2");

        act.Should().Throw<InvalidDataException>();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void CountImageFiles_RejectsImageDirectoryContainingALink()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        File.WriteAllText(Path.Combine(imageDirectory, "1.2.png"), "image");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(imageDirectory, "Linked"));
        FileSystemModificationImageFileService service = CreateService(paths);

        Action act = () => service.CountImageFiles("ShockWave");

        act.Should().Throw<InvalidDataException>();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void TryDeleteImage_KeepsCachedImagesWhenTheImageDirectoryContainsALink()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        string imagePath = Path.Combine(imageDirectory, "1.2.png");
        File.WriteAllText(imagePath, "image");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(imageDirectory, "Linked"));
        FileSystemModificationImageFileService service = CreateService(paths);

        bool deleted = service.TryDeleteImage("ShockWave", "1.2");

        deleted.Should().BeFalse();
        File.ReadAllText(imagePath).Should().Be("image");
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public async Task ReplaceImageAsync_WritesNothingWhenTheImageDirectoryContainsALinkAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(imageDirectory);
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(imageDirectory, "Linked"));
        string sourceImagePath = directory.CreateFile("selected.png", "new");
        FileSystemModificationImageFileService service = CreateService(paths);

        Func<Task> act = () => service.ReplaceImageAsync(
            new ModificationImageReplacementRequest("ShockWave", "1.2", sourceImagePath),
            CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        File.Exists(Path.Combine(imageDirectory, "1.2.png")).Should().BeFalse();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    private static FileSystemModificationImageFileService CreateService(LauncherPaths paths)
    {
        return new FileSystemModificationImageFileService(
            TestLauncherPaths.CreateRuntimePathContext(paths),
            NullLogger<FileSystemModificationImageFileService>.Instance);
    }

}
