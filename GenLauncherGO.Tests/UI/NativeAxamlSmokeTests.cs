using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Views;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Mods.Views;
using GenLauncherGO.UI.Features.Settings.Views;
using GenLauncherGO.UI.Features.Startup.Views;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.Tests.UI;

[Collection("Avalonia")]
public sealed class NativeAxamlSmokeTests
{
    [Fact]
    public void NativeAxamlRoots_LoadCompiledMarkup()
    {
        StaTestRunner.Run(() =>
        {
            ContentControl[] roots =
            [
                new IntegrityReviewDialog(),
                new MainWindow(),
                new AddModificationWindow(),
                new InfoWindow(),
                new ManualAddModificationWindow(),
                new LauncherSettingsWindow(),
                new LauncherExecutableManagementWindow(),
                new LauncherExecutableEditorWindow(),
                new InitWindow(),
                new LauncherGameSelectionWindow(),
                new LauncherLocationWarningWindow(),
                new LauncherSetupWindow(),
                new LauncherLoadingIndicator()
            ];

            foreach (ContentControl root in roots)
            {
                root.Content.Should().NotBeNull(
                    $"{root.GetType().Name} should load its compiled AXAML");
            }
        });
    }

    [Fact]
    public void MainContentLists_ExposeTheirSelectionCardinality()
    {
        StaTestRunner.Run(() =>
        {
            MainWindow window = new();

            window.FindControl<ListBox>("ModsList")!.SelectionMode.Should().Be(SelectionMode.Single);
            window.FindControl<ListBox>("PatchesList")!.SelectionMode.Should().Be(SelectionMode.Single);
            window.FindControl<ListBox>("AddonsList")!.SelectionMode.Should()
                .Be(SelectionMode.Multiple | SelectionMode.Toggle);
        });
    }

    [Fact]
    public void ContentListRowClick_SelectsTheRow()
    {
        StaTestRunner.Run(() =>
        {
            Border row = new() { Width = 200, Height = 40, Background = Brushes.Transparent };
            LauncherContentListBox list = new()
            {
                Width = 240,
                Height = 120,
                SelectionMode = SelectionMode.Single,
                ItemsSource = new[] { row }
            };
            MainWindow window = new() { Content = list };

            try
            {
                window.Show();
                window.UpdateLayout();

                Click(window, row);

                list.SelectedItem.Should().BeSameAs(row);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    ///     A tile's own buttons sit inside the row that owns them, so pressing one has to act on the button rather
    ///     than also move the selection out from under whatever the user was working with.
    /// </summary>
    [Fact]
    public void ContentListInteractiveChildClick_LeavesTheSelectionAlone()
    {
        StaTestRunner.Run(() =>
        {
            Button action = new() { Width = 80, Height = 24, Content = "Action" };
            Border row = new()
            {
                Width = 200,
                Height = 40,
                Background = Brushes.Transparent,
                Child = action
            };
            LauncherContentListBox list = new()
            {
                Width = 240,
                Height = 120,
                SelectionMode = SelectionMode.Single,
                ItemsSource = new[] { row }
            };
            MainWindow window = new() { Content = list };

            try
            {
                window.Show();
                window.UpdateLayout();

                Click(window, action);

                list.SelectedItem.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void VersionSelector_PointerReleaseOpensDropDownWithoutBindingReentry()
    {
        StaTestRunner.Run(() =>
        {
            MainWindow window = new();
            ComboBox selector = new()
            {
                Width = 160,
                Height = 30,
                ItemsSource = new[] { "1.0", "2.0" },
                SelectedIndex = 0
            };
            selector.Classes.Add("tile-version-selector");
            window.Content = selector;

            try
            {
                window.Show();
                window.UpdateLayout();

                selector.Theme.Should().BeSameAs(window.Resources["TileVersionSelectorTheme"]);
                Click(window, selector);

                selector.IsDropDownOpen.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void VersionSelectorOpen_KeepsOwningListSelection()
    {
        StaTestRunner.Run(() =>
        {
            using var row = VersionSelectorRow.Show();

            row.OpenVersionSelector();

            row.Selector.IsDropDownOpen.Should().BeTrue();
            row.List.SelectedItem.Should().BeSameAs(row.SelectedRow);
        });
    }

    /// <summary>
    ///     Deleting a version clears the popup entry's data context while the pointer is still down on it, which
    ///     used to reach the owning list as a click on an unrecognized source and clear the tile's selection.
    /// </summary>
    [Fact]
    public void VersionDeleteRelease_AfterItsDataContextIsCleared_KeepsOwningListSelection()
    {
        StaTestRunner.Run(() =>
        {
            using var row = VersionSelectorRow.Show();
            row.OpenVersionSelector();
            Button deleteButton = row.VersionDeleteButton();
            TopLevel popupRoot = TopLevel.GetTopLevel(deleteButton)!;
            Point clickPoint = CentreOf(deleteButton, popupRoot);
            bool clicked = false;
            deleteButton.Click += (_, _) => clicked = true;
            popupRoot.MouseDown(clickPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            deleteButton.DataContext = null;

            popupRoot.MouseUp(clickPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            clicked.Should().BeTrue();
            row.List.SelectedItem.Should().BeSameAs(row.SelectedRow);
        });
    }

    [Fact]
    public void ExecutableManager_ShowsEditingActionsOnlyForCustomEntries()
    {
        StaTestRunner.Run(() =>
        {
            LauncherExecutableManagementWindow window = new();
            ListBox list = window.FindControl<ListBox>("ExecutableEntries")!;
            list.ItemsSource = new[]
            {
                new ExecutableOption(
                    "Built in",
                    "built-in.exe",
                    isAvailable: true,
                    isBuiltIn: true),
                new ExecutableOption(
                    "Custom",
                    "missing.exe",
                    isAvailable: false,
                    isBuiltIn: false)
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                ListBoxItem[] rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                rows.Should().HaveCount(2);
                ButtonWithClass(rows[0], "row-edit").IsVisible.Should().BeFalse();
                ButtonWithClass(rows[0], "dialog-action").IsVisible.Should().BeFalse();
                ButtonWithClass(rows[1], "row-edit").IsVisible.Should().BeTrue();
                ButtonWithClass(rows[1], "dialog-action").IsVisible.Should().BeTrue();

                TextBlock missingIndicator = rows[1].GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Classes.Contains("missing-executable-indicator"));
                missingIndicator.IsVisible.Should().BeTrue();
                ToolTip.GetTip(missingIndicator).Should()
                    .Be(new AvaloniaLauncherStringLocalizer()["ExecutableUnavailable"]);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ModificationTiles_ShowActionsForSelectionAndAdvertisingStates()
    {
        StaTestRunner.Run(() =>
        {
            MainWindow window = new();
            var template = (IDataTemplate)window.Resources["ModificationTemplate"]!;

            ModificationViewModel modification = CreateTile(ModificationType.Mod);
            Control modificationTile = template.Build(modification)!;
            modificationTile.DataContext = modification;
            window.Content = modificationTile;

            FindDescendant<UpdateButton>(modificationTile, "ModificationUpdateButton")
                .IsVisible.Should().BeTrue();
            Grid secondaryActions =
                FindDescendant<Grid>(modificationTile, "ModificationSecondaryActions");
            secondaryActions.IsVisible.Should().BeFalse();

            modification.IsSelected = true;

            secondaryActions.IsVisible.Should().BeTrue();

            ModificationViewModel advertising = CreateTile(ModificationType.Advertising);
            Control advertisingTile = template.Build(advertising)!;
            advertisingTile.DataContext = advertising;
            window.Content = advertisingTile;

            FindDescendant<UpdateButton>(advertisingTile, "ModificationUpdateButton")
                .IsVisible.Should().BeFalse();
            FindDescendant<Grid>(advertisingTile, "ModificationSecondaryActions")
                .IsVisible.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(ModificationType.Patch)]
    [InlineData(ModificationType.Addon)]
    public void CompactContentTiles_UseTheSharedTemplateWithoutArtworkOrFullActions(
        ModificationType modificationType)
    {
        StaTestRunner.Run(() =>
        {
            MainWindow window = new();
            var template = (IDataTemplate)window.Resources["ModificationTemplate"]!;
            ModificationViewModel modification = CreateTile(modificationType);
            Control tile = template.Build(modification)!;
            tile.DataContext = modification;
            window.Content = tile;

            tile.Classes.Should().Contain("compact");
            FindDescendant<Grid>(tile, "ModificationButtonRow").IsVisible.Should().BeFalse();
            FindDescendant<UpdateButton>(tile, "CompactUpdateButton").IsVisible.Should().BeTrue();
            FindDescendant<Grid>(tile, "ModificationArtwork").IsVisible.Should().BeFalse();

            modification.SetDragAndDropMod();

            FindDescendant<Grid>(tile, "ModificationDragOverlayHost").IsVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void ModificationDragOverlay_BecomesVisibleWhileTileIsBeingDragged()
    {
        StaTestRunner.Run(() =>
        {
            MainWindow window = new();
            var template = (IDataTemplate)window.Resources["ModificationTemplate"]!;
            ModificationViewModel modification = CreateTile(ModificationType.Mod);
            Control modificationTile = template.Build(modification)!;
            modificationTile.DataContext = modification;
            window.Content = modificationTile;

            Border sourceOverlay = FindDescendant<Border>(
                modificationTile,
                "ModificationDragOverlay");
            sourceOverlay.IsVisible.Should().BeFalse();

            modification.SetDragAndDropMod();

            sourceOverlay.IsVisible.Should().BeTrue();
        });
    }

    private static ModificationViewModel CreateTile(ModificationType modificationType)
    {
        LauncherContentVersion version = new(new LauncherContentInstallation
        {
            ContentSourceKind = ContentSourceKind.Manual
        })
        {
            Name = "Content",
            Version = "0.3",
            ModificationType = modificationType,
            NewsLink = "https://example.test/news",
            NetworkInfo = "https://example.test/network",
            SupportLink = "https://example.test/support",
            ModDBLink = "https://example.test/moddb"
        };

        return TestModificationTile.Create(
            new LauncherContent(version),
            colors: TestLauncherTheme.Create());
    }

    private static Button ButtonWithClass(Visual row, string className)
    {
        return row.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Classes.Contains(className));
    }

    private static TControl FindDescendant<TControl>(Control root, string name)
        where TControl : Control
    {
        return root.GetVisualDescendants()
            .OfType<TControl>()
            .Single(control => control.Name == name);
    }

    private static Point CentreOf(Visual target, Visual coordinateSpace)
    {
        Point centre = new(target.Bounds.Width / 2, target.Bounds.Height / 2);
        return target.TranslatePoint(centre, coordinateSpace)!.Value;
    }

    private static void Click(TopLevel root, Visual target)
    {
        Point clickPoint = CentreOf(target, root);
        root.MouseDown(clickPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        root.MouseUp(clickPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Composes a content row the way the main window does: a selected row whose action area carries a version
    ///     selector, whose drop-down content lives outside the list's own visual tree.
    /// </summary>
    private sealed class VersionSelectorRow : IDisposable
    {
        private readonly MainWindow _window;

        private VersionSelectorRow(
            MainWindow window,
            LauncherContentListBox list,
            Grid selectedRow,
            ComboBox selector)
        {
            _window = window;
            List = list;
            SelectedRow = selectedRow;
            Selector = selector;
        }

        public LauncherContentListBox List { get; }

        public Grid SelectedRow { get; }

        public ComboBox Selector { get; }

        public static VersionSelectorRow Show()
        {
            MainWindow window = new();
            ModificationViewModel modification = CreateTile(ModificationType.Addon);
            ModificationVersionSelection versionSelection = new(
                modification.LatestVersion,
                modification);
            ComboBox selector = new()
            {
                Width = 200,
                Height = 30,
                ItemsSource = new[] { versionSelection },
                SelectedItem = versionSelection,
                ItemTemplate = (IDataTemplate)window.Resources["VersionTemplate"]!
            };
            selector.Classes.Add("tile-version-selector");
            Grid selectedRow = new() { Children = { selector } };
            LauncherContentListBox list = new()
            {
                Width = 240,
                Height = 120,
                SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
                ItemsSource = new[] { selectedRow },
                SelectedItem = selectedRow
            };
            window.Content = list;
            window.Show();
            window.UpdateLayout();

            return new VersionSelectorRow(window, list, selectedRow, selector);
        }

        public void OpenVersionSelector()
        {
            Click(_window, Selector);
        }

        public Button VersionDeleteButton()
        {
            return Selector.GetLogicalDescendants()
                .OfType<Button>()
                .Single(button => button.DataContext is ModificationVersionSelection);
        }

        public void Dispose()
        {
            _window.Close();
        }
    }
}
