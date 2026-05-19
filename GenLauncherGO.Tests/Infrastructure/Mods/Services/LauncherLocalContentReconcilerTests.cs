using System.Collections;
using System.Collections.Generic;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Contracts;
using GenLauncherGO.Infrastructure.Mods.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class LauncherLocalContentReconcilerTests
{
    [Fact]
    public void Reconcile_AddsUnregisteredLocalVersions()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        var launcherData = new LauncherData();
        localContentService.InstalledVersions =
        [
            TestLauncherContent.Version("Local Only", "1.0", installed: true)
        ];

        reconciler.Reconcile(launcherData, new List<LauncherContentKey>(), paths);

        launcherData.Modifications.Should().ContainSingle(mod => mod.Name == "Local Only");
        localContentService.EmptyPackageBackupCleanupRequests.Should().ContainSingle().Which.Should().Be(paths);
    }

    [Fact]
    public void Reconcile_MarksMissingRemoteVersionsUninstalledAndDeletesMissingLocalOnlyVersions()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        LauncherContentVersion remoteVersion = TestLauncherContent.Version("Remote", "1.0", installed: true);
        LauncherContentVersion localOnlyVersion = TestLauncherContent.Version("Local Only", "2.0", installed: true);
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(remoteVersion);
        launcherData.AddOrUpdate(localOnlyVersion);
        localContentService.InstalledVersions = [];

        reconciler.Reconcile(
            launcherData,
            new List<LauncherContentKey> { remoteVersion.ContentKey },
            paths);

        launcherData.Modifications.Should().ContainSingle().Which.Name.Should().Be("Remote");
        launcherData.Modifications[0].Versions.Should().ContainSingle().Which.Installation.Installed.Should().BeFalse();
        localContentService.ImageDeletionRequests.Should().ContainSingle(request =>
            request.Paths == paths &&
            request.ContentKey.Name == "Local Only" &&
            ReferenceEquals(request.Data, launcherData));
    }

    [Fact]
    public void Reconcile_PreservesAddedRepositoryModWhenRemoteCatalogIsUnavailable()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService
        {
            InstalledVersions = []
        };
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        LauncherContentVersion addedVersion = TestLauncherContent.Version(
            "Added",
            "1.0",
            sourceKind: ContentSourceKind.ManagedSingleFile);
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(addedVersion);

        reconciler.Reconcile(launcherData, [], paths);

        launcherData.Modifications.Should().ContainSingle()
            .Which.Name.Should().Be("Added");
        localContentService.ImageDeletionRequests.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_MarksStaleOriginalGameAddonUninstalled()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        LauncherContentVersion addon = TestLauncherContent.Version(
            "Original Game Addon",
            "1.0",
            ModificationType.Addon,
            LauncherContentKey.OriginalGame.Name,
            installed: true);
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(addon);

        reconciler.Reconcile(launcherData, new[] { addon.ContentKey }, paths);

        launcherData.Addons.Should().ContainSingle();
        addon.Installation.Installed.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_MarksStaleOriginalGamePatchUninstalled()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        LauncherContentVersion patch = TestLauncherContent.Version(
            "Original Game Patch",
            "1.0",
            ModificationType.Patch,
            LauncherContentKey.OriginalGame.Name,
            installed: true);
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(patch);

        reconciler.Reconcile(launcherData, new[] { patch.ContentKey }, paths);

        launcherData.Patches.Should().ContainSingle();
        patch.Installation.Installed.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_ChecksAChildSharedByMultipleParentVersionsOnce()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        LauncherContentVersion firstParent = TestLauncherContent.Version("Parent", "1.0");
        LauncherContentVersion secondParent = TestLauncherContent.Version("Parent", "2.0");
        LauncherContentVersion child = TestLauncherContent.Version(
            "Shared Child",
            "1.0",
            ModificationType.Addon,
            "Parent",
            installed: true);
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(firstParent);
        launcherData.AddOrUpdate(secondParent);
        launcherData.AddOrUpdate(child);
        localContentService.InstalledVersions = [firstParent, secondParent];
        var downloadedContent =
            new EnumerationCountingReadOnlyCollection<LauncherContentKey>([child.ContentKey]);

        reconciler.Reconcile(launcherData, downloadedContent, paths);

        child.Installation.Installed.Should().BeFalse();
        downloadedContent.EnumerationCount.Should().Be(1);
    }

    [Theory]
    [InlineData("First Patch")]
    [InlineData("Second Patch")]
    public void Reconcile_IsIndependentOfTheGloballySelectedPatch(string selectedPatchName)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        LauncherContentVersion parent = TestLauncherContent.Version("Parent", "1.0", isSelected: true);
        LauncherContentVersion firstPatch = TestLauncherContent.Version(
            "First Patch",
            "1.0",
            ModificationType.Patch,
            parent.Name,
            isSelected: selectedPatchName == "First Patch");
        LauncherContentVersion secondPatch = TestLauncherContent.Version(
            "Second Patch",
            "1.0",
            ModificationType.Patch,
            parent.Name,
            isSelected: selectedPatchName == "Second Patch");
        LauncherContentVersion firstAddon = TestLauncherContent.Version(
            "First Addon",
            "1.0",
            ModificationType.Addon,
            firstPatch.Name,
            installed: true);
        LauncherContentVersion secondAddon = TestLauncherContent.Version(
            "Second Addon",
            "1.0",
            ModificationType.Addon,
            secondPatch.Name,
            installed: true);
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(parent);
        launcherData.AddOrUpdate(firstPatch);
        launcherData.AddOrUpdate(secondPatch);
        launcherData.AddOrUpdate(firstAddon);
        launcherData.AddOrUpdate(secondAddon);
        localContentService.InstalledVersions = [parent, firstPatch, secondPatch];

        reconciler.Reconcile(
            launcherData,
            new[] { firstAddon.ContentKey, secondAddon.ContentKey },
            paths);

        firstAddon.Installation.Installed.Should().BeFalse();
        secondAddon.Installation.Installed.Should().BeFalse();
    }

    /// <summary>
    ///     Discarding a version drops it from the catalog as well as from disk, and asks for its cached artwork to be
    ///     removed once nothing else refers to the card. A version the user threw away must not come back as a card
    ///     entry the next time the list is drawn.
    /// </summary>
    [Fact]
    public void DiscardVersion_RemovesTheVersionFromTheCatalogAndFromDisk()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        LauncherContentVersion discardedVersion = TestLauncherContent.Version("ShockWave", "1.0", installed: true);
        LauncherContentVersion keptVersion = TestLauncherContent.Version("ShockWave", "2.0", installed: true);
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(discardedVersion);
        launcherData.AddOrUpdate(keptVersion);

        reconciler.DiscardVersion(launcherData, discardedVersion.ContentKey, paths);

        launcherData.Modifications.Should().ContainSingle()
            .Which.Versions.Should().ContainSingle()
            .Which.Version.Should().Be("2.0");
        localContentService.DeletedVersions.Should().ContainSingle(request =>
            request.Paths == paths &&
            request.ContentKey == discardedVersion.ContentKey);
        localContentService.ImageDeletionRequests.Should().ContainSingle(request =>
            request.ContentKey == discardedVersion.ContentKey &&
            ReferenceEquals(request.Data, launcherData));
    }

    private static LauncherLocalContentReconciler CreateReconciler(
        ILocalLauncherContentService localContentService)
    {
        return new LauncherLocalContentReconciler(
            localContentService,
            NullLogger<LauncherLocalContentReconciler>.Instance);
    }

    private sealed class EnumerationCountingReadOnlyCollection<T> : IReadOnlyCollection<T>
    {
        private readonly IReadOnlyCollection<T> _items;

        public EnumerationCountingReadOnlyCollection(IReadOnlyCollection<T> items)
        {
            _items = items;
        }

        public int EnumerationCount { get; private set; }

        public int Count => _items.Count;

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
