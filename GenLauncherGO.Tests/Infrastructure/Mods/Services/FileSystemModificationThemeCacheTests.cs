using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Services;
using GenLauncherGO.Infrastructure.Persistence.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class FileSystemModificationThemeCacheTests
{
    [Fact]
    public void CachedPalette_SurvivesAReadBack()
    {
        using TestDirectory directory = new();
        FileSystemModificationThemeCache cache = CreateCache(directory, out _);
        LauncherContentTheme theme = new()
        {
            GenLauncherBorderColor = "#00e3ff",
            GenLauncherInactiveBorder = "DarkGray",
            GenLauncherActiveColor = "#baff0c",
            GenLauncherBackgroundImageLink = "https://cdn.example.test/contra.png"
        };

        cache.Save("Contra", "1.0", theme);

        LauncherContentTheme? loaded = cache.Load("Contra", "1.0");
        loaded.Should().NotBeNull();
        loaded!.GenLauncherBorderColor.Should().Be("#00e3ff");
        loaded.GenLauncherInactiveBorder.Should().Be("DarkGray");
        loaded.GenLauncherActiveColor.Should().Be("#baff0c");
        loaded.GenLauncherBackgroundImageLink.Should().Be("https://cdn.example.test/contra.png");
        loaded.GenLauncherDarkFillColor.Should().BeEmpty();
    }

    [Fact]
    public void UncachedPalette_LoadsAsNothing()
    {
        using TestDirectory directory = new();
        FileSystemModificationThemeCache cache = CreateCache(directory, out _);

        cache.Load("Contra", "1.0").Should().BeNull();
    }

    /// <summary>
    ///     The palette is cached inside the modification's own image folder so that removing the content card, which
    ///     deletes that folder, takes the palette with it rather than leaving an orphan behind. It is written as a
    ///     YAML document under the shared palette cache name, which is the name a later start looks for: a different
    ///     name orphans every palette already cached on disk instead of reading it back.
    /// </summary>
    [Fact]
    public void CachedPalette_LivesInTheModificationImageCacheFolder()
    {
        using TestDirectory directory = new();
        FileSystemModificationThemeCache cache = CreateCache(directory, out LauncherPaths paths);

        cache.Save("Contra", "1.0", new LauncherContentTheme { GenLauncherActiveColor = "#baff0c" });

        string imageFolder = paths.GetModificationImagesDirectory("Contra");
        Directory.EnumerateFiles(imageFolder).Should().ContainSingle()
            .Which.Should().Be(Path.Combine(
                imageFolder,
                LauncherContentTheme.ResolveCacheBaseName("1.0") + ".yaml"));
    }

    /// <summary>
    ///     Content with no usable identity has no cache folder of its own, so the palette is dropped rather than
    ///     written somewhere another modification would later read it back from.
    /// </summary>
    [Theory]
    [InlineData("", "1.0")]
    [InlineData("Contra", " ")]
    public void Save_KeepsNothingForBlankContentIdentity(string modificationName, string version)
    {
        using TestDirectory directory = new();
        FileSystemModificationThemeCache cache = CreateCache(directory, out LauncherPaths paths);

        cache.Save(modificationName, version, new LauncherContentTheme { GenLauncherActiveColor = "#baff0c" });

        cache.Load(modificationName, version).Should().BeNull();
        Directory.EnumerateFileSystemEntries(paths.ImagesDirectory).Should().BeEmpty();
    }

    [Fact]
    public void Save_KeepsNothingWhenTheImageFolderIsALink()
    {
        using TestDirectory directory = new();
        FileSystemModificationThemeCache cache = CreateCache(directory, out LauncherPaths paths);
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            paths.GetModificationImagesDirectory("Contra"));

        cache.Save("Contra", "1.0", new LauncherContentTheme { GenLauncherActiveColor = "#baff0c" });

        cache.Load("Contra", "1.0").Should().BeNull();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
        Directory.EnumerateFileSystemEntries(junction.TargetDirectory).Should().ContainSingle();
    }

    /// <summary>
    ///     A palette that cannot be read is absent, not empty: handing back an all-blank palette would re-skin the
    ///     shell with nothing instead of leaving the active game's own colours in place.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("GenLauncherActiveColor: [unterminated")]
    public void Load_ReturnsNothingForAnUnreadableCachedDocument(string cachedDocument)
    {
        using TestDirectory directory = new();
        FileSystemModificationThemeCache cache = CreateCache(directory, out LauncherPaths paths);
        string documentPath = paths.GetModificationImageFilePath(
            "Contra",
            LauncherContentTheme.ResolveCacheBaseName("1.0") + ".yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
        File.WriteAllText(documentPath, cachedDocument);

        LauncherContentTheme? loaded = cache.Load("Contra", "1.0");

        loaded.Should().BeNull();
    }

    private static FileSystemModificationThemeCache CreateCache(
        TestDirectory directory,
        out LauncherPaths paths)
    {
        paths = TestLauncherPaths.Create(directory);
        return new FileSystemModificationThemeCache(
            TestLauncherPaths.CreateRuntimePathContext(paths),
            new AtomicFileWriter(),
            NullLogger<YamlDocumentStore<LauncherContentTheme>>.Instance,
            NullLogger<FileSystemModificationThemeCache>.Instance);
    }
}
