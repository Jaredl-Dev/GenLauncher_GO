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
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Launcher.Views;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Mods.Views;
using GenLauncherGO.Features.Settings.Views;
using GenLauncherGO.Features.Startup.Views;
using GenLauncherGO.Shared.Controls;
using GenLauncherGO.Shared.Dialogs;

namespace GenLauncherGO.Tests.Shared;

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
    public void InfoActionWithNamedSecondaryChoice_ShowsBothRequestedActions()
    {
        StaTestRunner.Run(() =>
        {
            InfoWindow dialog = new(
                new LauncherInfoDialogRequest("Update ready", "Version 1.2.0"),
                InfoDialogKind.InfoAction,
                cancelText: "Later",
                actionText: "Restart now");

            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                dialog.GetLogicalDescendants()
                    .OfType<Button>()
                    .Where(button => button.IsVisible)
                    .Select(button => button.Content)
                    .Should()
                    .BeEquivalentTo(new object[] { "Later", "Restart now" });
            }
            finally
            {
                dialog.Close();
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
