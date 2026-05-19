using System;
using System.Globalization;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Startup;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Mods.ViewModels;

/// <summary>
///     Loads Avalonia image sources for one modification tile.
/// </summary>
internal sealed class ModificationTileImageProvider
{
    private readonly ModificationImageSourceFactory _imageSourceFactory;

    private readonly LauncherRuntimeContext _launcherContext;

    private readonly ILogger _logger;

    private readonly IModificationImageFileService _modificationImageFileService;

    private int _advertisingImageIndex = -1;

    public ModificationTileImageProvider(
        ModificationImageSourceFactory imageSourceFactory,
        LauncherRuntimeContext launcherContext,
        IModificationImageFileService modificationImageFileService,
        ILogger logger)
    {
        _imageSourceFactory = imageSourceFactory ?? throw new ArgumentNullException(nameof(imageSourceFactory));
        _launcherContext = launcherContext ?? throw new ArgumentNullException(nameof(launcherContext));
        _modificationImageFileService = modificationImageFileService ??
                                        throw new ArgumentNullException(nameof(modificationImageFileService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Loads the cached shell artwork a version published with its palette, if it published one and it downloaded.
    /// </summary>
    /// <remarks>
    ///     Returns <see langword="null" /> when the modification declared no artwork or the download has not landed yet,
    ///     which the caller treats as "keep the active game's artwork" rather than as a failure to theme.
    /// </remarks>
    public IImageBrush? LoadThemeBackground(LauncherContent modification, LauncherContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ArgumentNullException.ThrowIfNull(version);

        if (version.Theme is null || version.Theme.GenLauncherBackgroundImageLink.Length == 0)
        {
            return null;
        }

        string? imagePath = _modificationImageFileService.FindExistingImageFilePath(
            modification.Name,
            LauncherContentTheme.ResolveBackgroundImageBaseName(version.Version));
        Bitmap? background = _imageSourceFactory.LoadFileImage(imagePath, false);
        if (background is null)
        {
            return null;
        }

        return (IImageBrush)new ImageBrush(background)
        {
            Stretch = Stretch.Fill
        }.ToImmutable();
    }

    /// <summary>
    ///     Loads the grayscale image for a modification tile.
    /// </summary>
    public IImage? LoadGrayscaleImage(
        LauncherContent modification,
        LauncherContentVersion latestVersion,
        bool localMod)
    {
        if (modification.ModificationType == ModificationType.Mod)
        {
            return LoadImage(
                GetModificationImageFileName(modification, latestVersion, localMod),
                modification.Name,
                latestVersion.Version,
                true,
                true);
        }

        if (modification.ModificationType == ModificationType.Advertising)
        {
            return LoadAdvertisingImage(modification);
        }

        return null;
    }

    /// <summary>
    ///     Loads the color image for a selected modification tile.
    /// </summary>
    public IImage? LoadColorImage(
        LauncherContent modification,
        LauncherContentVersion latestVersion,
        bool localMod)
    {
        if (modification.ModificationType != ModificationType.Mod)
        {
            return null;
        }

        return LoadImage(
            GetModificationImageFileName(modification, latestVersion, localMod),
            modification.Name,
            latestVersion.Version,
            false,
            true);
    }

    private IImage? LoadAdvertisingImage(LauncherContent modification)
    {
        string folderName = modification.Name.Trim(Path.GetInvalidFileNameChars());

        int filesCount = _modificationImageFileService.CountImageFiles(folderName);
        if (filesCount <= 0)
        {
            return null;
        }

        if (_advertisingImageIndex == -1)
        {
            var random = new Random();
            int value = random.Next(0, 30);
            _advertisingImageIndex = value == 0
                ? random.Next(filesCount / 2, filesCount)
                : random.Next(0, filesCount / 2);
        }

        string imageBaseName = _advertisingImageIndex.ToString(CultureInfo.InvariantCulture);
        string? imageFileName = _modificationImageFileService.FindExistingImageFilePath(folderName, imageBaseName);

        return LoadImage(
            imageFileName,
            folderName,
            imageBaseName,
            false,
            false);
    }

    private string? GetModificationImageFileName(
        LauncherContent modification,
        LauncherContentVersion latestVersion,
        bool localMod)
    {
        string? imageFileName = _modificationImageFileService.FindExistingImageFilePath(
            modification.Name,
            latestVersion.Version);

        if (localMod && !_modificationImageFileService.ImageExists(imageFileName))
        {
            return null;
        }

        return imageFileName;
    }

    /// <summary>
    ///     Loads a cached or default image for a modification tile.
    /// </summary>
    private IImage? LoadImage(
        string? path,
        string modificationName,
        string imageBaseName,
        bool grayscale,
        bool useDefaultWhenMissing)
    {
        try
        {
            Bitmap? image = _modificationImageFileService.ImageExists(path)
                ? _imageSourceFactory.LoadFileImage(path, grayscale)
                : null;

            if (image == null && useDefaultWhenMissing)
            {
                image = _imageSourceFactory.LoadDefaultImage(
                    _launcherContext.CurrentlyManagedGame,
                    grayscale);
            }

            return image;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not load modification image {ImageFileName}; attempting to remove the cached image.",
                Path.GetFileName(path));

            return TryRemoveInvalidImage(
                path,
                modificationName,
                imageBaseName,
                grayscale,
                useDefaultWhenMissing);
        }
    }

    /// <summary>
    ///     Removes an invalid cached image and falls back to the default image when appropriate.
    /// </summary>
    private IImage? TryRemoveInvalidImage(
        string? path,
        string modificationName,
        string imageBaseName,
        bool grayscale,
        bool useDefaultWhenMissing)
    {
        try
        {
            _modificationImageFileService.TryDeleteImage(modificationName, imageBaseName);

            return useDefaultWhenMissing
                ? _imageSourceFactory.LoadDefaultImage(_launcherContext.CurrentlyManagedGame, grayscale)
                : null;
        }
        catch (Exception deleteException)
        {
            _logger.LogWarning(
                deleteException,
                "Could not remove invalid modification image {ImageFileName}.",
                Path.GetFileName(path));
            return null;
        }
    }
}
