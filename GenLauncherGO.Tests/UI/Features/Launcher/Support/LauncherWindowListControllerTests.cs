using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Support;

public sealed class LauncherWindowListControllerTests
{
    [Fact]
    public void InitializeRestoresPersistedSelectionsIntoSemanticTiles()
    {
        StaTestRunner.Run(() =>
        {
            LauncherContent mod = CreateModification("ShockWave", ModificationType.Mod);
            LauncherContent patch = CreateModification("Balance", ModificationType.Patch, "ShockWave");
            LauncherContent firstAddon = CreateModification("Music", ModificationType.Addon, "ShockWave");
            LauncherContent secondAddon = CreateModification("Models", ModificationType.Addon, "ShockWave");
            FakeLauncherContentCatalog catalog = CreateCatalog(
                mods: new[] { mod },
                patches: new[] { patch },
                addons: new[] { firstAddon, secondAddon },
                selectedMod: mod,
                selectedPatch: patch,
                selectedAddons: new[] { firstAddon, secondAddon });
            ControllerFixture fixture = CreateFixture(catalog, initializeViewModel: true);

            fixture.ViewModel.SelectedModifications.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("ShockWave");
            fixture.ViewModel.SelectedPatches.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("Balance");
            fixture.ViewModel.SelectedAddons.Select(tile => tile.ContainerModification.Name)
                .Should().Equal("Music", "Models");
        });
    }

    [Fact]
    public void SemanticSelectionProjectsToCatalogOnlyAtPersistenceBoundary()
    {
        StaTestRunner.Run(() =>
        {
            LauncherContent first = CreateModification("First", ModificationType.Mod);
            LauncherContent second = CreateModification("Second", ModificationType.Mod);
            FakeLauncherContentCatalog catalog = CreateCatalog(
                mods: new[] { first, second },
                selectedMod: first);
            ControllerFixture fixture = CreateFixture(catalog, initializeViewModel: true);
            ModificationViewModel firstTile = fixture.ViewModel.ModsListSource[0];
            ModificationViewModel secondTile = fixture.ViewModel.ModsListSource[1];

            firstTile.IsSelected = false;
            secondTile.IsSelected = true;

            catalog.Data.GetSelectedMod()!.Name.Should().Be("First");

            fixture.ViewModel.SaveLauncherData();

            catalog.Data.GetSelectedMod()!.Name.Should().Be("Second");
            catalog.SaveCount.Should().Be(1);
        });
    }

    [Fact]
    public void RefreshTabsRestoresSelectionsForEachSemanticParent()
    {
        StaTestRunner.Run(() =>
        {
            LauncherContent firstMod = CreateModification("First", ModificationType.Mod);
            LauncherContent secondMod = CreateModification("Second", ModificationType.Mod);
            LauncherContent firstPatch = CreateModification("First Patch", ModificationType.Patch, "First");
            LauncherContent secondPatch = CreateModification("Second Patch", ModificationType.Patch, "Second");
            LauncherContent firstAddon = CreateModification("First Addon", ModificationType.Addon, "First");
            LauncherContent secondAddon = CreateModification("Second Addon", ModificationType.Addon, "Second");
            FakeLauncherContentCatalog catalog = CreateCatalog(
                mods: new[] { firstMod, secondMod },
                patches: new[] { firstPatch, secondPatch },
                addons: new[] { firstAddon, secondAddon },
                selectedMod: firstMod,
                selectedPatches: new[] { firstPatch, secondPatch },
                selectedAddons: new[] { firstAddon, secondAddon });
            ControllerFixture fixture = CreateFixture(catalog, initializeViewModel: true);

            fixture.ViewModel.SelectedPatches.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("First Patch");
            fixture.ViewModel.SelectedAddons.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("First Addon");

            SelectModification(fixture.ViewModel, "Second");
            fixture.Content.RefreshTabs();

            fixture.ViewModel.SelectedPatches.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("Second Patch");
            fixture.ViewModel.SelectedAddons.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("Second Addon");

            SelectModification(fixture.ViewModel, "First");
            fixture.Content.RefreshTabs();

            fixture.ViewModel.SelectedPatches.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("First Patch");
            fixture.ViewModel.SelectedAddons.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("First Addon");
        });
    }

    [Theory]
    [InlineData(ModificationType.Mod)]
    [InlineData(ModificationType.Patch)]
    [InlineData(ModificationType.Addon)]
    public void ContentReplacementPreservesSemanticSelection(ModificationType modificationType)
    {
        StaTestRunner.Run(() =>
        {
            string parent = modificationType == ModificationType.Mod ? string.Empty : LauncherContentKey.OriginalGame.Name;
            LauncherContent original = CreateModification("Content", modificationType, parent);
            FakeLauncherContentCatalog catalog = modificationType switch
            {
                ModificationType.Mod => CreateCatalog(mods: new[] { original }, selectedMod: original),
                ModificationType.Patch => CreateCatalog(
                    patches: new[] { original },
                    selectedPatches: new[] { original }),
                _ => CreateCatalog(
                    addons: new[] { original },
                    selectedAddons: new[] { original })
            };
            ControllerFixture fixture = CreateFixture(catalog, initializeViewModel: true);
            var replacementVersion = new LauncherContentVersion
            {
                Installation = new LauncherContentInstallation { Installed = true, IsSelected = true },
                Name = "Content",
                Version = "2.0",
                ModificationType = modificationType,
                ParentContentName = parent,
            };
            var replacement = new LauncherContent(replacementVersion);
            fixture.ViewModel.AddImportedContentToList(
                new LauncherManualImportResult(modificationType, replacement));

            IReadOnlyList<ModificationViewModel> target = modificationType switch
            {
                ModificationType.Patch => fixture.ViewModel.PatchesListSource,
                ModificationType.Addon => fixture.ViewModel.AddonsListSource,
                _ => fixture.ViewModel.ModsListSource
            };
            target.Should().ContainSingle();
            target[0].ContainerModification.Should().BeSameAs(replacement);
            target[0].IsSelected.Should().BeTrue();
        });
    }

    [Fact]
    public void RemovingContentClearsOnlySemanticSelection()
    {
        StaTestRunner.Run(() =>
        {
            ControllerFixture fixture = CreateFixture();
            ModificationViewModel selected = CreateTile("Selected", ModificationType.Mod);
            selected.IsSelected = true;
            fixture.ViewModel.ModsListSource.Add(selected);

            fixture.ViewModel.RemoveContentFromList(selected);

            fixture.ViewModel.ModsListSource.Should().BeEmpty();
            selected.IsSelected.Should().BeFalse();
        });
    }

    [Fact]
    public void EnsureContentSelectedUsesSingleParentAndMultipleAddonSemantics()
    {
        StaTestRunner.Run(() =>
        {
            ControllerFixture fixture = CreateFixture();
            ModificationViewModel firstMod = CreateTile("First", ModificationType.Mod);
            ModificationViewModel secondMod = CreateTile("Second", ModificationType.Mod);
            ModificationViewModel firstAddon = CreateTile("First Addon", ModificationType.Addon);
            ModificationViewModel secondAddon = CreateTile("Second Addon", ModificationType.Addon);
            fixture.ViewModel.ModsListSource.Add(firstMod);
            fixture.ViewModel.ModsListSource.Add(secondMod);
            fixture.ViewModel.AddonsListSource.Add(firstAddon);
            fixture.ViewModel.AddonsListSource.Add(secondAddon);
            firstMod.IsSelected = true;
            firstAddon.IsSelected = true;

            fixture.ViewModel.EnsureContentSelected(secondMod);
            fixture.ViewModel.EnsureContentSelected(secondAddon);

            firstMod.IsSelected.Should().BeFalse();
            secondMod.IsSelected.Should().BeTrue();
            firstAddon.IsSelected.Should().BeTrue();
            secondAddon.IsSelected.Should().BeTrue();
        });
    }

    [Fact]
    public void ReloadForActiveGameRestoresSelectionProjectedBeforeLiveSessionRefresh()
    {
        StaTestRunner.Run(() =>
        {
            LauncherContent first = CreateModification("First", ModificationType.Mod);
            LauncherContent second = CreateModification("Second", ModificationType.Mod);
            FakeLauncherContentCatalog catalog = CreateCatalog(
                mods: new[] { first, second },
                selectedMod: first);
            ControllerFixture fixture = CreateFixture(catalog, initializeViewModel: true);
            fixture.ViewModel.ModsListSource[0].IsSelected = false;
            fixture.ViewModel.ModsListSource[1].IsSelected = true;

            fixture.ViewModel.ApplySelectionToPersistenceModel();
            fixture.ViewModel.ReloadForActiveGame();

            fixture.ViewModel.SelectedModifications.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("Second");
        });
    }

    [Fact]
    public void TryClearContentSelectionUnselectsModsAndPatchesButLeavesAddonSemanticsToTheList()
    {
        StaTestRunner.Run(() =>
        {
            ControllerFixture fixture = CreateFixture();
            ModificationViewModel mod = CreateTile("Mod", ModificationType.Mod);
            ModificationViewModel patch = CreateTile("Patch", ModificationType.Patch);
            ModificationViewModel addon = CreateTile("Addon", ModificationType.Addon);
            mod.IsSelected = true;
            patch.IsSelected = true;
            addon.IsSelected = true;

            bool modCleared = fixture.ViewModel.TryClearContentSelection(mod);
            bool patchCleared = fixture.ViewModel.TryClearContentSelection(patch);
            bool addonCleared = fixture.ViewModel.TryClearContentSelection(addon);

            modCleared.Should().BeTrue();
            patchCleared.Should().BeTrue();
            addonCleared.Should().BeFalse();
            mod.IsSelected.Should().BeFalse();
            patch.IsSelected.Should().BeFalse();
            addon.IsSelected.Should().BeTrue();
        });
    }

    [Fact]
    public void SelectedVersionsFollowSemanticLaunchOrder()
    {
        StaTestRunner.Run(() =>
        {
            ControllerFixture fixture = CreateFixture();
            ModificationViewModel mod = CreateTile("Mod", ModificationType.Mod);
            ModificationViewModel patch = CreateTile("Patch", ModificationType.Patch);
            ModificationViewModel addon = CreateTile("Addon", ModificationType.Addon);
            mod.IsSelected = true;
            patch.IsSelected = true;
            addon.IsSelected = true;
            fixture.ViewModel.ModsListSource.Add(mod);
            fixture.ViewModel.PatchesListSource.Add(patch);
            fixture.ViewModel.AddonsListSource.Add(addon);

            fixture.ViewModel.GetSelectedVersionsOfAllSelectedModifications().Should().Equal(
                mod.SelectedVersion!,
                patch.SelectedVersion!,
                addon.SelectedVersion!);
        });
    }

    [Fact]
    public void ModSelectionLoadsChildrenAndAlwaysReenablesControls()
    {
        StaTestRunner.Run(async () =>
        {
            var catalog = new FakeLauncherContentCatalog();
            ControllerFixture fixture = CreateFixture(catalog);
            ModificationViewModel tile = CreateTile("New Mod", ModificationType.Mod);
            fixture.ViewModel.ModsListSource.Add(tile);
            Task selectionTask = Task.CompletedTask;
            fixture.ModsList.SelectionChanged += (_, args) =>
                selectionTask = fixture.Content.HandleModsListSelectionChangedAsync(args);

            tile.IsSelected = true;
            fixture.ModsList.SelectedItem = tile;
            await selectionTask;

            tile.IsSelected.Should().BeTrue();
            catalog.ChildManifestRequests.Should().Equal(tile.ContainerModification.ContentKey);
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void FailedModChildLoadStillReenablesControls()
    {
        StaTestRunner.Run(async () =>
        {
            var catalog = new FakeLauncherContentCatalog
            {
                ChildManifestReadHandler = (_, _) =>
                    Task.FromException(new InvalidOperationException("catalog unavailable"))
            };
            ControllerFixture fixture = CreateFixture(catalog);
            ModificationViewModel tile = CreateTile("New Mod", ModificationType.Mod);
            fixture.ViewModel.ModsListSource.Add(tile);
            Task selectionTask = Task.CompletedTask;
            fixture.ModsList.SelectionChanged += (_, args) =>
                selectionTask = fixture.Content.HandleModsListSelectionChangedAsync(args);

            tile.IsSelected = true;
            fixture.ModsList.SelectedItem = tile;
            Func<Task> act = () => selectionTask;

            await act.Should().ThrowAsync<InvalidOperationException>();
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void VersionSelectionUpdatesOnlyMatchingVersion()
    {
        StaTestRunner.Run(() =>
        {
            LauncherContent initialContent = CreateModification("Versioned", ModificationType.Mod);
            LauncherContentVersion first = initialContent.Versions[0];
            var second = new LauncherContentVersion
            {
                Installation = new LauncherContentInstallation { Installed = true },
                Name = "Versioned",
                Version = "beta",
                ModificationType = ModificationType.Mod,
            };
            var data = new LauncherData();
            data.AddOrUpdate(first);
            data.AddOrUpdate(second);
            ModificationViewModel tile = CreateTile(data.FindContent(first.ContentKey)!);
            var selection = new ModificationVersionSelection(second, "BETA", tile);
            var versions = new ComboBox { ItemsSource = new[] { selection }, SelectedItem = selection };
            ControllerFixture fixture = CreateFixture();

            fixture.Content.HandleVersionsListSelectionChanged(versions);

            first.Installation.IsSelected.Should().BeFalse();
            second.Installation.IsSelected.Should().BeTrue();
        });
    }

    private static ControllerFixture CreateFixture(
        FakeLauncherContentCatalog? catalog = null,
        bool initializeViewModel = false)
    {
        return new ControllerFixture(
            catalog ?? new FakeLauncherContentCatalog(),
            initializeViewModel);
    }

    private static ListBox CreateBoundListBox(
        string name,
        SelectionMode selectionMode,
        IEnumerable itemsSource)
    {
        return new ListBox
        {
            Name = name,
            SelectionMode = selectionMode,
            ItemsSource = itemsSource
        };
    }

    private static void SelectModification(MainWindowViewModel viewModel, string name)
    {
        foreach (ModificationViewModel modification in viewModel.ModsListSource)
        {
            modification.IsSelected = String.Equals(
                modification.ContainerModification.Name,
                name,
                StringComparison.Ordinal);
        }
    }

    private static MainWindowViewModel CreateViewModel(
        FakeLauncherContentCatalog catalog,
        LauncherRuntimeContext runtimeContext)
    {
        ILauncherPreferencesService preferences = Substitute.For<ILauncherPreferencesService>();
        preferences.Current.Returns(new LauncherPreferences());
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        TestStringLocalizer stringLocalizer = CreateStringLocalizer();
        LauncherPackageActivityService packageActivityService = new();

        return new MainWindowViewModel(
            preferences,
            new LauncherExecutableSelectionService(
                executableDiscovery,
                runtimeContext,
                preferences,
                stringLocalizer),
            catalog,
            runtimeContext,
            stringLocalizer,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            Substitute.For<IModificationImageFileService>(),
            packageActivityService,
            NullLogger<ModificationViewModel>.Instance,
            TestLauncherLaunchCoordinator.Create(
                packageActivityService,
                preferences,
                catalog,
                stringLocalizer),
            NullLogger<MainWindowViewModel>.Instance);
    }

    private static FakeLauncherContentCatalog CreateCatalog(
        IReadOnlyList<LauncherContent>? mods = null,
        IReadOnlyList<LauncherContent>? patches = null,
        IReadOnlyList<LauncherContent>? addons = null,
        LauncherContent? selectedMod = null,
        LauncherContent? selectedPatch = null,
        IReadOnlyList<LauncherContent>? selectedPatches = null,
        IReadOnlyList<LauncherContent>? selectedAddons = null)
    {
        var catalog = new FakeLauncherContentCatalog();
        foreach (LauncherContent modification in mods ?? Array.Empty<LauncherContent>())
        {
            AddContent(catalog.Data, modification, isSelected: modification.Equals(selectedMod));
        }

        string selectedParentName = selectedMod?.Name ?? LauncherContentKey.OriginalGame.Name;
        foreach (LauncherContent patch in patches ?? Array.Empty<LauncherContent>())
        {
            bool isSelected = patch.Equals(selectedPatch) ||
                              selectedPatches?.Any(selected => selected.Equals(patch)) == true;
            AddContent(catalog.Data, patch, isSelected, selectedParentName);
        }

        foreach (LauncherContent addon in addons ?? Array.Empty<LauncherContent>())
        {
            AddContent(
                catalog.Data,
                addon,
                selectedAddons?.Any(selected => selected.Equals(addon)) == true,
                selectedParentName);
        }

        return catalog;
    }

    private static void AddContent(
        LauncherData data,
        LauncherContent content,
        bool isSelected,
        string? defaultParentContentName = null)
    {
        foreach (LauncherContentVersion version in content.Versions)
        {
            version.Installation.IsSelected = isSelected;
            data.AddOrUpdate(version);
        }

        IReadOnlyList<LauncherContent> collection = content.ModificationType switch
        {
            ModificationType.Addon => data.Addons,
            ModificationType.Patch => data.Patches,
            _ => data.Modifications
        };
        LauncherContentKey key = content.ContentKey;
        if (!String.IsNullOrWhiteSpace(defaultParentContentName) &&
            content.ModificationType is ModificationType.Patch or ModificationType.Addon)
        {
            string effectiveParentContentName = String.IsNullOrWhiteSpace(content.ContentKey.ParentIdentity)
                ? defaultParentContentName
                : content.ContentKey.ParentIdentity;
            key = new LauncherContentKey(
                content.ModificationType,
                effectiveParentContentName,
                content.Name,
                string.Empty);
        }

        collection.Single(candidate => candidate.ContentKey == key).IsSelected = isSelected;
    }

    private static ModificationViewModel CreateTile(string name, ModificationType modificationType)
    {
        return CreateTile(CreateModification(name, modificationType));
    }

    private static ModificationViewModel CreateTile(LauncherContent modification)
    {
        return new ModificationViewModel(
            modification,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(colors: TestLauncherTheme.Create()),
            Substitute.For<IModificationImageFileService>(),
            CreateStringLocalizer(),
            new LauncherPackageActivityService(),
            NullLogger<ModificationViewModel>.Instance);
    }

    private static LauncherContent CreateModification(
        string name,
        ModificationType modificationType,
        string parentContentName = "")
    {
        string effectiveParentContentName =
            modificationType == ModificationType.Mod
                ? string.Empty
                : String.IsNullOrWhiteSpace(parentContentName)
                    ? LauncherContentKey.OriginalGame.Name
                    : parentContentName;
        var version = new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true, IsSelected = true },
            Name = name,
            Version = "1.0",
            ModificationType = modificationType,
            ParentContentName = effectiveParentContentName
        };
        return new LauncherContent(version);
    }

    private static LauncherRuntimeContext CreateRuntimeContext()
    {
        LauncherPaths paths = TestLauncherPaths.Create(@"C:\Games\ZeroHour");
        return new LauncherRuntimeContext(
            TestLauncherPaths.CreateRuntimePathContext(paths),
            "1.0")
        {
            Connected = false,
            Colors = TestLauncherTheme.Create()
        };
    }

    private static TestStringLocalizer CreateStringLocalizer()
    {
        return new TestStringLocalizer(new Dictionary<string, string>
        {
            ["Addons"] = "Add-ons for ",
            ["AddAddonFromFiles"] = "Add add-on for {0}",
            ["AddPatchFromFiles"] = "Add patch for {0}",
            ["LatestVersion"] = "Latest version: ",
            ["Patches"] = "Patches for ",
            ["Preparing"] = "Preparing",
            ["Update"] = "Update",
            ["UpToDate"] = "Up to date"
        });
    }

    private sealed class ControllerFixture
    {
        public ControllerFixture(
            FakeLauncherContentCatalog catalog,
            bool initializeViewModel)
        {
            Catalog = catalog;
            LauncherRuntimeContext runtimeContext = CreateRuntimeContext();
            ViewModel = CreateViewModel(catalog, runtimeContext);
            ModsList = CreateBoundListBox("ModsList", SelectionMode.Single, ViewModel.ModsListSource);
            PatchesList = CreateBoundListBox("PatchesList", SelectionMode.Single, ViewModel.PatchesListSource);
            AddonsList = CreateBoundListBox("AddonsList", SelectionMode.Multiple, ViewModel.AddonsListSource);
            Owner = new Window();
            Content = new LauncherWindowListController(
                Owner,
                ViewModel,
                runtimeContext,
                ModsList,
                PatchesList,
                AddonsList);

            if (initializeViewModel)
            {
                ViewModel.Initialize();
            }

            Content.Initialize();
        }

        public FakeLauncherContentCatalog Catalog { get; }

        public Window Owner { get; }

        public MainWindowViewModel ViewModel { get; }

        public LauncherWindowListController Content { get; }

        public ListBox ModsList { get; }

        public ListBox PatchesList { get; }

        public ListBox AddonsList { get; }
    }

}
