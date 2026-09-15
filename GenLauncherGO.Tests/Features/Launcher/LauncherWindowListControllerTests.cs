using System.Collections;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Tests.Features.Launcher;

[Collection("Avalonia")]
public sealed class LauncherWindowListControllerTests
{
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
            new FakeStringLocalizer());
    }

    private sealed class ControllerFixture
    {
        public ControllerFixture(
            FakeLauncherContentCatalog catalog,
            bool initializeViewModel)
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

            Content.Initialize();
        }

        public LauncherRuntimeContext RuntimeContext { get; }

        public MainWindowViewModel ViewModel { get; }

        public LauncherWindowListController Content { get; }

        public ListBox ModsList { get; }

        public ListBox PatchesList { get; }

        public ListBox AddonsList { get; }
    }
}
