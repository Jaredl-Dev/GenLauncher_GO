using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Startup;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Support;

[Collection("Avalonia")]
public sealed class LauncherWindowListControllerTests
{
    [Fact]
    public void Initialize_RestoresPersistedSelectionsIntoSemanticTiles()
    {
        StaTestRunner.Run(() =>
        {
            FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
                .WithMod("ShockWave")
                .WithPatch("ShockWave", "Balance")
                .WithAddon("ShockWave", "Music")
                .WithAddon("ShockWave", "Models")
                .Selected("ShockWave")
                .Selected("Balance", ModificationType.Patch, "ShockWave")
                .Selected("Music", ModificationType.Addon, "ShockWave")
                .Selected("Models", ModificationType.Addon, "ShockWave")
                .Build();

            ControllerFixture fixture = CreateFixture(catalog, true);

            fixture.ViewModel.SelectedModifications.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("ShockWave");
            fixture.ViewModel.SelectedPatches.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("Balance");
            fixture.ViewModel.SelectedAddons.Select(tile => tile.ContainerModification.Name)
                .Should().Equal("Music", "Models");
        });
    }

    [Fact]
    public void Initialize_NormalizesSingleChoiceSelectionsAndKeepsAllAddons()
    {
        StaTestRunner.Run(() =>
        {
            FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
                .WithMod("First")
                .WithMod("Second")
                .WithPatch("First", "First Patch")
                .WithPatch("First", "Second Patch")
                .WithAddon("First", "First Addon")
                .WithAddon("First", "Second Addon")
                .Selected("First")
                .Selected("First Patch", ModificationType.Patch, "First")
                .Selected("Second Patch", ModificationType.Patch, "First")
                .Selected("First Addon", ModificationType.Addon, "First")
                .Selected("Second Addon", ModificationType.Addon, "First")
                .Build();
            catalog.Data.Modifications[1].IsSelected = true;

            ControllerFixture fixture = CreateFixture(catalog, true);

            fixture.ViewModel.SelectedModifications.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("First");
            fixture.ViewModel.SelectedPatches.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("First Patch");
            fixture.ViewModel.SelectedAddons.Select(addon => addon.ContainerModification.Name)
                .Should().Equal("First Addon", "Second Addon");
        });
    }

    [Fact]
    public void SemanticSelection_ProjectsToCatalogOnlyAtPersistenceBoundary()
    {
        StaTestRunner.Run(() =>
        {
            FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
                .WithMod("First")
                .WithMod("Second")
                .Selected("First")
                .Build();
            ControllerFixture fixture = CreateFixture(catalog, true);
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
    public void RefreshTabs_RestoresSelectionsForEachSemanticParent()
    {
        StaTestRunner.Run(() =>
        {
            FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
                .WithMod("First")
                .WithMod("Second")
                .WithPatch("First", "First Patch")
                .WithPatch("Second", "Second Patch")
                .WithAddon("First", "First Addon")
                .WithAddon("Second", "Second Addon")
                .Selected("First")
                .Selected("First Patch", ModificationType.Patch, "First")
                .Selected("Second Patch", ModificationType.Patch, "Second")
                .Selected("First Addon", ModificationType.Addon, "First")
                .Selected("Second Addon", ModificationType.Addon, "Second")
                .Build();
            ControllerFixture fixture = CreateFixture(catalog, true);

            fixture.ViewModel.SelectedPatches.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("First Patch");
            fixture.ViewModel.SelectedAddons.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("First Addon");

            SelectModification(fixture.ViewModel, "Second");
            fixture.Content.RefreshTabs();
            fixture.ViewModel.SaveLauncherData();

            fixture.ViewModel.SelectedPatches.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("Second Patch");
            fixture.ViewModel.SelectedAddons.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("Second Addon");
            FindContent(catalog.Data.Patches, "First Patch").IsSelected.Should().BeTrue();
            FindContent(catalog.Data.Patches, "Second Patch").IsSelected.Should().BeTrue();

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
    public void ContentReplacement_PreservesSemanticSelection(ModificationType modificationType)
    {
        StaTestRunner.Run(() =>
        {
            string parent = modificationType == ModificationType.Mod
                ? string.Empty
                : LauncherContentKey.OriginalGame.Name;
            FakeLauncherContentCatalog catalog = modificationType switch
            {
                ModificationType.Mod => TestLauncherContent.Catalog()
                    .WithMod("Content")
                    .Selected("Content")
                    .Build(),
                ModificationType.Patch => TestLauncherContent.Catalog()
                    .WithPatch(parent, "Content")
                    .Selected("Content", ModificationType.Patch, parent)
                    .Build(),
                _ => TestLauncherContent.Catalog()
                    .WithAddon(parent, "Content")
                    .Selected("Content", ModificationType.Addon, parent)
                    .Build()
            };
            ControllerFixture fixture = CreateFixture(catalog, true);
            LauncherContent replacement = TestLauncherContent.From(TestLauncherContent.Version(
                "Content",
                "2.0",
                modificationType,
                parent,
                true,
                true));

            fixture.ViewModel.AddImportedContentToList(replacement);

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
    public void RemovingContent_ClearsOnlySemanticSelection()
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
    public void SelectContent_UsesSingleModAndPatchAndMultipleAddonSemantics()
    {
        StaTestRunner.Run(() =>
        {
            ControllerFixture fixture = CreateFixture();
            ModificationViewModel firstMod = CreateTile("First", ModificationType.Mod);
            ModificationViewModel secondMod = CreateTile("Second", ModificationType.Mod);
            ModificationViewModel firstPatch = CreateTile("First Patch", ModificationType.Patch);
            ModificationViewModel secondPatch = CreateTile("Second Patch", ModificationType.Patch);
            ModificationViewModel firstAddon = CreateTile("First Addon", ModificationType.Addon);
            ModificationViewModel secondAddon = CreateTile("Second Addon", ModificationType.Addon);
            fixture.ViewModel.ModsListSource.Add(firstMod);
            fixture.ViewModel.ModsListSource.Add(secondMod);
            fixture.ViewModel.PatchesListSource.Add(firstPatch);
            fixture.ViewModel.PatchesListSource.Add(secondPatch);
            fixture.ViewModel.AddonsListSource.Add(firstAddon);
            fixture.ViewModel.AddonsListSource.Add(secondAddon);
            firstMod.IsSelected = true;
            firstPatch.IsSelected = true;
            firstAddon.IsSelected = true;

            fixture.ViewModel.SelectContent(secondMod);
            fixture.ViewModel.SelectContent(secondPatch);
            fixture.ViewModel.SelectContent(secondAddon);

            firstMod.IsSelected.Should().BeFalse();
            secondMod.IsSelected.Should().BeTrue();
            firstPatch.IsSelected.Should().BeFalse();
            secondPatch.IsSelected.Should().BeTrue();
            firstAddon.IsSelected.Should().BeTrue();
            secondAddon.IsSelected.Should().BeTrue();
        });
    }

    [Fact]
    public void ReloadForActiveGame_RestoresSelectionProjectedBeforeLiveSessionRefresh()
    {
        StaTestRunner.Run(() =>
        {
            FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
                .WithMod("First")
                .WithMod("Second")
                .Selected("First")
                .Build();
            ControllerFixture fixture = CreateFixture(catalog, true);
            fixture.ViewModel.ModsListSource[0].IsSelected = false;
            fixture.ViewModel.ModsListSource[1].IsSelected = true;

            fixture.ViewModel.ApplySelectionToPersistenceModel();
            fixture.ViewModel.ReloadForActiveGame();

            fixture.ViewModel.SelectedModifications.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("Second");
        });
    }

    [Fact]
    public void SelectedVersions_FollowSemanticLaunchOrderAndSkipSuspendedDownloads()
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
            patch.SelectedVersion!.Installation.DownloadSuspended = true;
            fixture.ViewModel.ModsListSource.Add(mod);
            fixture.ViewModel.PatchesListSource.Add(patch);
            fixture.ViewModel.AddonsListSource.Add(addon);

            fixture.ViewModel.GetSelectedVersionsOfAllSelectedModifications().Should().Equal(
                mod.SelectedVersion!,
                addon.SelectedVersion!);
        });
    }

    [Fact]
    public void ModSelection_LoadsChildrenAndAlwaysReenablesControls()
    {
        StaTestRunner.Run(async () =>
        {
            var catalog = new FakeLauncherContentCatalog();
            ControllerFixture fixture = CreateFixture(catalog);
            bool controlsEnabledDuringFetch = true;
            bool loadingIndicatorVisibleDuringFetch = false;
            catalog.ChildManifestReadHandler = (_, _) =>
            {
                controlsEnabledDuringFetch = fixture.ViewModel.MainControlsEnabled;
                loadingIndicatorVisibleDuringFetch = fixture.ViewModel.IsLoadingIndicatorVisible;
                return Task.CompletedTask;
            };
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
            controlsEnabledDuringFetch.Should().BeFalse();
            loadingIndicatorVisibleDuringFetch.Should().BeTrue();
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
            fixture.ViewModel.IsLoadingIndicatorVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void HandleModsListSelectionChangedAsync_BeforeInitialize_DoesNotLoadChildContent()
    {
        StaTestRunner.Run(async () =>
        {
            var catalog = new FakeLauncherContentCatalog();
            ControllerFixture fixture = CreateFixture(catalog, initializeController: false);
            ModificationViewModel tile = CreateTile("New Mod", ModificationType.Mod);
            fixture.ViewModel.ModsListSource.Add(tile);

            await fixture.Content.HandleModsListSelectionChangedAsync(
                CreateSelectionChange(fixture.ModsList, tile));

            catalog.ChildManifestRequests.Should().BeEmpty();
        });
    }

    [Fact]
    public void HandleModsListSelectionChangedAsync_WhenReplacedByTheSameContent_DoesNotReloadChildContent()
    {
        StaTestRunner.Run(async () =>
        {
            var catalog = new FakeLauncherContentCatalog();
            ControllerFixture fixture = CreateFixture(catalog);
            ModificationViewModel tile = CreateTile("New Mod", ModificationType.Mod);
            ModificationViewModel replacement = CreateTile("New Mod", ModificationType.Mod);
            fixture.ViewModel.ModsListSource.Add(tile);
            await fixture.Content.HandleModsListSelectionChangedAsync(
                CreateSelectionChange(fixture.ModsList, tile));

            await fixture.Content.HandleModsListSelectionChangedAsync(
                CreateSelectionChange(fixture.ModsList, replacement, tile));

            catalog.ChildManifestRequests.Should().Equal(tile.ContainerModification.ContentKey);
        });
    }

    [Fact]
    public void HandleChildContentListSelectionChanged_FromPatchesList_ReplacesTheSelectedPatch()
    {
        StaTestRunner.Run(() =>
        {
            FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
                .WithMod("ShockWave")
                .WithPatch("ShockWave", "First Patch")
                .WithPatch("ShockWave", "Second Patch")
                .Selected("ShockWave")
                .Selected("First Patch", ModificationType.Patch, "ShockWave")
                .Build();
            ControllerFixture fixture = CreateFixture(catalog, true);
            ModificationViewModel secondPatch = FindTile(fixture.ViewModel.PatchesListSource, "Second Patch");

            fixture.Content.HandleChildContentListSelectionChanged(
                CreateSelectionChange(fixture.PatchesList, secondPatch));

            fixture.ViewModel.SelectedPatches.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("Second Patch");
        });
    }

    [Fact]
    public void HandleChildContentListSelectionChanged_FromPatchesList_RebuildsAddonsForTheNewPatch()
    {
        StaTestRunner.Run(() =>
        {
            FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
                .WithMod("ShockWave")
                .WithPatch("ShockWave", "First Patch")
                .WithPatch("ShockWave", "Second Patch")
                .WithAddon("First Patch", "First Extras")
                .WithAddon("Second Patch", "Second Extras")
                .Selected("ShockWave")
                .Selected("First Patch", ModificationType.Patch, "ShockWave")
                .Build();
            ControllerFixture fixture = CreateFixture(catalog, true);
            ModificationViewModel secondPatch = FindTile(fixture.ViewModel.PatchesListSource, "Second Patch");

            fixture.Content.HandleChildContentListSelectionChanged(
                CreateSelectionChange(fixture.PatchesList, secondPatch));

            fixture.ViewModel.AddonsListSource.Select(addon => addon.ContainerModification.Name)
                .Should().Equal("Second Extras");
        });
    }

    [Fact]
    public void HandleChildContentListSelectionChanged_FromAddonsList_AddsToTheExistingSelection()
    {
        StaTestRunner.Run(() =>
        {
            FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
                .WithMod("ShockWave")
                .WithAddon("ShockWave", "Music")
                .WithAddon("ShockWave", "Models")
                .Selected("ShockWave")
                .Selected("Music", ModificationType.Addon, "ShockWave")
                .Build();
            ControllerFixture fixture = CreateFixture(catalog, true);
            ModificationViewModel models = FindTile(fixture.ViewModel.AddonsListSource, "Models");

            fixture.Content.HandleChildContentListSelectionChanged(
                CreateSelectionChange(fixture.AddonsList, models));

            fixture.ViewModel.SelectedAddons.Select(addon => addon.ContainerModification.Name)
                .Should().Equal("Music", "Models");
        });
    }

    [Fact]
    public void HandleChildContentListSelectionChanged_FromAnotherList_LeavesChildSelectionUntouched()
    {
        StaTestRunner.Run(() =>
        {
            FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
                .WithMod("ShockWave")
                .WithPatch("ShockWave", "First Patch")
                .WithPatch("ShockWave", "Second Patch")
                .Selected("ShockWave")
                .Selected("First Patch", ModificationType.Patch, "ShockWave")
                .Build();
            ControllerFixture fixture = CreateFixture(catalog, true);
            ModificationViewModel secondPatch = FindTile(fixture.ViewModel.PatchesListSource, "Second Patch");

            fixture.Content.HandleChildContentListSelectionChanged(
                CreateSelectionChange(fixture.ModsList, secondPatch));

            fixture.ViewModel.SelectedPatches.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("First Patch");
        });
    }

    [Fact]
    public void ModSelection_WhenDynamicThemeChanges_AppliesSelectionAndRestoresDefaultOnClear()
    {
        StaTestRunner.Run(async () =>
        {
            ControllerFixture fixture = CreateFixture();
            Color defaultActiveColor = fixture.RuntimeContext.Colors.GenLauncherActiveColor.Color;
            ModificationViewModel themed = CreateTile(TestLauncherContent.From(TestLauncherContent.Version(
                "Themed",
                installed: true,
                isSelected: true,
                theme: new LauncherContentTheme { GenLauncherActiveColor = "#FF123456" })));
            themed.IsSelected = false;
            fixture.ViewModel.ModsListSource.Add(themed);
            Task selectionTask = Task.CompletedTask;
            fixture.ModsList.SelectionChanged += (_, args) =>
                selectionTask = fixture.Content.HandleModsListSelectionChangedAsync(args);

            fixture.ModsList.SelectedItem = themed;
            await selectionTask;

            themed.IsSelected.Should().BeTrue();
            fixture.RuntimeContext.Colors.GenLauncherActiveColor.Color.Should().Be(Color.Parse("#FF123456"));
            Application.Current!.Resources["GenLauncherActiveColor"].Should().BeSameAs(
                fixture.RuntimeContext.Colors.GenLauncherActiveColor);

            fixture.ModsList.SelectedItem = null;
            await selectionTask;

            themed.IsSelected.Should().BeFalse();
            fixture.RuntimeContext.Colors.GenLauncherActiveColor.Color.Should().Be(defaultActiveColor);
            Application.Current.Resources["GenLauncherActiveColor"].Should().BeSameAs(
                fixture.RuntimeContext.Colors.GenLauncherActiveColor);
        });
    }

    [Fact]
    public void FailedModChildLoadStill_ReenablesControls()
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
    public void VersionSelection_UpdatesOnlyMatchingVersion()
    {
        StaTestRunner.Run(() =>
        {
            LauncherContentVersion first = TestLauncherContent.Version(
                "Versioned",
                installed: true,
                isSelected: true);
            LauncherContentVersion second = TestLauncherContent.Version(
                "Versioned",
                "beta",
                installed: true);
            ModificationViewModel tile = CreateTile(TestLauncherContent.From(first, second));
            var selection = new ModificationVersionSelection(second, tile);
            var versions = new ComboBox { ItemsSource = new[] { selection }, SelectedItem = selection };
            ControllerFixture fixture = CreateFixture();

            fixture.Content.HandleVersionsListSelectionChanged(versions);

            first.Installation.IsSelected.Should().BeFalse();
            second.Installation.IsSelected.Should().BeTrue();
        });
    }

    private static ControllerFixture CreateFixture(
        FakeLauncherContentCatalog? catalog = null,
        bool initializeViewModel = false,
        bool initializeController = true)
    {
        return new ControllerFixture(
            catalog ?? new FakeLauncherContentCatalog(),
            initializeViewModel,
            initializeController);
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

    private static SelectionChangedEventArgs CreateSelectionChange(
        ListBox source,
        ModificationViewModel addedItem,
        ModificationViewModel? removedItem = null)
    {
        ModificationViewModel[] removedItems = removedItem == null ? [] : [removedItem];
        ModificationViewModel[] addedItems = [addedItem];

        return new SelectionChangedEventArgs(
            SelectingItemsControl.SelectionChangedEvent,
            removedItems,
            addedItems)
        {
            Source = source
        };
    }

    private static LauncherContent FindContent(IReadOnlyList<LauncherContent> content, string name)
    {
        return content.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
    }

    private static ModificationViewModel FindTile(
        IReadOnlyList<ModificationViewModel> tiles,
        string name)
    {
        return tiles.Single(tile =>
            string.Equals(tile.ContainerModification.Name, name, StringComparison.Ordinal));
    }

    private static void SelectModification(MainWindowViewModel viewModel, string name)
    {
        foreach (ModificationViewModel modification in viewModel.ModsListSource)
        {
            modification.IsSelected = string.Equals(
                modification.ContainerModification.Name,
                name,
                StringComparison.Ordinal);
        }
    }

    private static ModificationViewModel CreateTile(string name, ModificationType modificationType)
    {
        string parentContentName = modificationType == ModificationType.Mod
            ? string.Empty
            : LauncherContentKey.OriginalGame.Name;

        return CreateTile(TestLauncherContent.From(TestLauncherContent.Version(
            name,
            type: modificationType,
            parentContentName: parentContentName,
            installed: true,
            isSelected: true)));
    }

    private static ModificationViewModel CreateTile(LauncherContent modification)
    {
        return TestModificationTile.Create(
            modification,
            FakeStringLocalizer.Create(TestLocalizedStrings.Launcher));
    }

    private sealed class ControllerFixture
    {
        public ControllerFixture(
            FakeLauncherContentCatalog catalog,
            bool initializeViewModel,
            bool initializeController)
        {
            LauncherRuntimeContext runtimeContext = TestLauncherRuntimeContext.Create();
            RuntimeContext = runtimeContext;
            ViewModel = TestMainWindowViewModel.Create(catalog, runtimeContext: runtimeContext);
            ModsList = CreateBoundListBox("ModsList", SelectionMode.Single, ViewModel.ModsListSource);
            PatchesList = CreateBoundListBox("PatchesList", SelectionMode.Single, ViewModel.PatchesListSource);
            AddonsList = CreateBoundListBox(
                "AddonsList",
                SelectionMode.Multiple | SelectionMode.Toggle,
                ViewModel.AddonsListSource);
            Content = new LauncherWindowListController(
                ViewModel,
                runtimeContext,
                ModsList,
                PatchesList,
                AddonsList);

            if (initializeViewModel)
            {
                ViewModel.Initialize();
            }

            if (initializeController)
            {
                Content.Initialize();
            }
        }

        public LauncherRuntimeContext RuntimeContext { get; }

        public MainWindowViewModel ViewModel { get; }

        public LauncherWindowListController Content { get; }

        public ListBox ModsList { get; }

        public ListBox PatchesList { get; }

        public ListBox AddonsList { get; }
    }
}
