using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenLauncherGO.Core.Startup;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Mods;

/// <summary>
///     Creates Avalonia bitmaps for modification tiles without extracting generated image variants to disk.
/// </summary>
internal sealed class ModificationImageSourceFactory
{
    private const string DefaultImageResourceNamePrefix = "GenLauncherGO.UI.Features.Mods.Resources.";

    private readonly ConcurrentDictionary<string, Bitmap>
        _colorImageCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Bitmap> _grayscaleImageCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<ModificationImageSourceFactory> _logger;

    public ModificationImageSourceFactory(ILogger<ModificationImageSourceFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Loads the default unknown modification image for the currently managed game.
    /// </summary>
    public Bitmap LoadDefaultImage(SupportedGame supportedGame, bool grayscale)
    {
        string resourceName = DefaultImageResourceNamePrefix + (supportedGame == SupportedGame.ZeroHour
            ? "UserAddedModBannerZeroHour.jpg"
            : "UserAddedModBannerGenerals.jpg");

        return LoadResourceImage(resourceName, grayscale);
    }

    /// <summary>
    ///     Loads a modification image from disk and optionally converts it to grayscale.
    /// </summary>
    /// <remarks>
    ///     This method reads the file into memory before returning so callers can safely replace or delete the source file
    ///     after a successful load.
    /// </remarks>
    public Bitmap? LoadFileImage(string? path, bool grayscale)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        FileInfo fileInfo = new(path);
        string? cacheKey = null;

        try
        {
            cacheKey = CreateFileCacheKey(fileInfo);
            Bitmap colorImage = GetOrLoadImage(cacheKey, () => DecodeFileImage(fileInfo.FullName));
            return grayscale ? GetOrCreateGrayscaleImage(cacheKey, colorImage) : colorImage;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException
                                              or UnauthorizedAccessException)
        {
            if (cacheKey != null)
            {
                RemoveCachedImages(cacheKey);
            }

            _logger.LogWarning(exception, "Failed to load modification image {ImageFileName}.", fileInfo.Name);
            throw;
        }
    }

    /// <summary>
    ///     Creates a cache key that changes when a file is replaced or edited.
    /// </summary>
    private static string CreateFileCacheKey(FileInfo fileInfo)
    {
        fileInfo.Refresh();
        return string.Concat(
            "file:",
            fileInfo.FullName,
            ":",
            fileInfo.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    private static Bitmap DecodeFileImage(string path)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return DecodeStreamImage(stream);
    }

    private Bitmap LoadResourceImage(string resourceName, bool grayscale)
    {
        string cacheKey = "resource:" + resourceName;

        try
        {
            Bitmap colorImage = GetOrLoadImage(cacheKey, () => DecodeResourceImage(resourceName));
            return grayscale ? GetOrCreateGrayscaleImage(cacheKey, colorImage) : colorImage;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            RemoveCachedImages(cacheKey);
            _logger.LogError(exception, "Failed to load modification image resource {ImageResourceName}.",
                resourceName);
            throw;
        }
    }

    private static Bitmap DecodeResourceImage(string resourceName)
    {
        Stream stream = typeof(ModificationImageSourceFactory).Assembly.GetManifestResourceStream(resourceName)
                        ?? throw new IOException("The modification image resource was not found.");

        using (stream)
        {
            return DecodeStreamImage(stream);
        }
    }

    private static Bitmap DecodeStreamImage(Stream stream)
    {
        return new Bitmap(stream);
    }

    private Bitmap GetOrLoadImage(string cacheKey, Func<Bitmap> imageFactory)
    {
        return _colorImageCache.GetOrAdd(cacheKey, _ => imageFactory());
    }

    private Bitmap GetOrCreateGrayscaleImage(string cacheKey, Bitmap source)
    {
        return _grayscaleImageCache.GetOrAdd(cacheKey, _ => CreateGrayscaleImage(source));
    }

    /// <summary>
    ///     Converts a decoded bitmap to grayscale while preserving its alpha channel.
    /// </summary>
    private static Bitmap CreateGrayscaleImage(Bitmap source)
    {
        WriteableBitmap grayscale = new(
            source.PixelSize,
            source.Dpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using (ILockedFramebuffer framebuffer = grayscale.Lock())
        {
            source.CopyPixels(framebuffer);

            int redOffset;
            int blueOffset;
            if (framebuffer.Format == PixelFormat.Bgra8888)
            {
                redOffset = 2;
                blueOffset = 0;
            }
            else if (framebuffer.Format == PixelFormat.Rgba8888)
            {
                redOffset = 0;
                blueOffset = 2;
            }
            else
            {
                throw new NotSupportedException(
                    "The writable bitmap did not expose a supported 32-bit pixel format.");
            }

            byte[] row = new byte[framebuffer.RowBytes];
            for (int y = 0; y < framebuffer.Size.Height; y++)
            {
                nint rowAddress = framebuffer.Address + checked(y * framebuffer.RowBytes);
                Marshal.Copy(rowAddress, row, 0, row.Length);
                for (int x = 0; x < framebuffer.Size.Width; x++)
                {
                    int pixelOffset = x * 4;
                    int luminance =
                        row[pixelOffset + redOffset] * 77 +
                        row[pixelOffset + 1] * 150 +
                        row[pixelOffset + blueOffset] * 29;
                    byte gray = (byte)((luminance + 128) >> 8);
                    row[pixelOffset] = gray;
                    row[pixelOffset + 1] = gray;
                    row[pixelOffset + 2] = gray;
                }

                Marshal.Copy(row, 0, rowAddress, row.Length);
            }
        }

        return grayscale;
    }

    private void RemoveCachedImages(string cacheKey)
    {
        _colorImageCache.TryRemove(cacheKey, out _);
        _grayscaleImageCache.TryRemove(cacheKey, out _);
    }
}
