using System.Collections;
using System.Collections.Generic;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Contracts;
using GenLauncherGO.Infrastructure.Mods.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class LauncherLocalContentReconcilerTests
{
    [Fact]
    public void ReconcileAddsUnregisteredLocalVersions()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        var launcherData = new LauncherData();
        localContentService.InstalledVersions =
        [
                new LauncherContentVersion
                {
                    Installation = new LauncherContentInstallation { Installed = true },
                    ModificationType = ModificationType.Mod,
                    Name = "Local Only",
                    Version = "1.0",
                }
        ];

        reconciler.Reconcile(launcherData, new List<LauncherContentKey>(), paths);

        launcherData.Modifications.Should().ContainSingle(mod => mod.Name == "Local Only");
    }

    [Fact]
    public void ReconcileMarksMissingRemoteVersionsUninstalledAndDeletesMissingLocalOnlyVersions()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        var remoteVersion = new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = "Remote",
            Version = "1.0",
        };
        var localOnlyVersion = new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = "Local Only",
            Version = "2.0",
        };
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(remoteVersion);
        launcherData.AddOrUpdate(localOnlyVersion);
        localContentService.InstalledVersions = [];

        reconciler.Reconcile(
            launcherData,
            new List<LauncherContentKey> { remoteVersion.ContentKey },
            paths);

        launcherData.Modifications.Should().ContainSingle(mod => mod.Name == "Remote");
        launcherData.Modifications[0].Versions.Should().ContainSingle().Which.Installation.Installed.Should().BeFalse();
        localContentService.ImageDeletionRequests.Should().ContainSingle(request =>
            request.Paths == paths &&
            request.ContentKey.Name == "Local Only" &&
            ReferenceEquals(request.Data, launcherData));
    }

    [Fact]
    public void ReconcilePreservesAddedRepositoryModWhenRemoteCatalogIsUnavailable()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService
        {
            InstalledVersions = []
        };
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        var addedVersion = new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation
            {
                ContentSourceKind = ContentSourceKind.ManagedSingleFile
            },
            ModificationType = ModificationType.Mod,
            Name = "Added",
            Version = "1.0",
        };
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(addedVersion);

        reconciler.Reconcile(launcherData, [], paths);

        launcherData.Modifications.Should().ContainSingle()
            .Which.Name.Should().Be("Added");
        localContentService.ImageDeletionRequests.Should().BeEmpty();
    }

    [Fact]
    public void ReconcileMarksStaleOriginalGameAddonUninstalled()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        var addon = new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Addon,
            ParentContentName = LauncherContentKey.OriginalGame.Name,
            Name = "Original Game Addon",
            Version = "1.0",
        };
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(addon);

        reconciler.Reconcile(launcherData, new[] { addon.ContentKey }, paths);

        launcherData.Addons.Should().ContainSingle();
        addon.Installation.Installed.Should().BeFalse();
    }

    [Fact]
    public void ReconcileMarksStaleOriginalGamePatchUninstalled()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        var patch = new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Patch,
            ParentContentName = LauncherContentKey.OriginalGame.Name,
            Name = "Original Game Patch",
            Version = "1.0",
        };
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(patch);

        reconciler.Reconcile(launcherData, new[] { patch.ContentKey }, paths);

        launcherData.Patches.Should().ContainSingle();
        patch.Installation.Installed.Should().BeFalse();
    }

    [Fact]
    public void ReconcileChecksAChildSharedByMultipleParentVersionsOnce()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        LauncherContentVersion firstParent = CreateVersion(
            ModificationType.Mod,
            "Parent",
            "1.0");
        LauncherContentVersion secondParent = CreateVersion(
            ModificationType.Mod,
            "Parent",
            "2.0");
        LauncherContentVersion child = CreateVersion(
            ModificationType.Addon,
            "Shared Child",
            "1.0",
            "Parent");
        child.Installation.Installed = true;
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
    public void ReconcileIsIndependentOfTheGloballySelectedPatch(string selectedPatchName)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherLocalContentReconciler reconciler = CreateReconciler(localContentService);
        LauncherContentVersion parent = CreateVersion(ModificationType.Mod, "Parent", "1.0");
        parent.Installation.IsSelected = true;
        LauncherContentVersion firstPatch = CreateVersion(
            ModificationType.Patch,
            "First Patch",
            "1.0",
            parent.Name);
        LauncherContentVersion secondPatch = CreateVersion(
            ModificationType.Patch,
            "Second Patch",
            "1.0",
            parent.Name);
        firstPatch.Installation.IsSelected = selectedPatchName == firstPatch.Name;
        secondPatch.Installation.IsSelected = selectedPatchName == secondPatch.Name;
        LauncherContentVersion firstAddon = CreateVersion(
            ModificationType.Addon,
            "First Addon",
            "1.0",
            firstPatch.Name);
        LauncherContentVersion secondAddon = CreateVersion(
            ModificationType.Addon,
            "Second Addon",
            "1.0",
            secondPatch.Name);
        firstAddon.Installation.Installed = true;
        secondAddon.Installation.Installed = true;
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

    private static LauncherLocalContentReconciler CreateReconciler(
        ILocalLauncherContentService localContentService)
    {
        return new LauncherLocalContentReconciler(
            localContentService,
            NullLogger<LauncherLocalContentReconciler>.Instance);
    }

    private static LauncherPaths CreatePaths(string root)
    {
        return TestLauncherPaths.Create(root);
    }

    private static LauncherContentVersion CreateVersion(
        ModificationType modificationType,
        string name,
        string version,
        string parentContentName = "")
    {
        return new LauncherContentVersion
        {
            ModificationType = modificationType,
            ParentContentName = parentContentName,
            Name = name,
            Version = version
        };
    }

    private sealed class EnumerationCountingReadOnlyCollection<T> : IReadOnlyCollection<T>
    {
        private readonly IReadOnlyCollection<T> _items;

        public EnumerationCountingReadOnlyCollection(IReadOnlyCollection<T> items)
        {
            _items = items;
        }

        public int Count => _items.Count;

        public int EnumerationCount { get; private set; }

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
