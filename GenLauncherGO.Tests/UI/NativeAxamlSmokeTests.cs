using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Views;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Mods.Views;
using GenLauncherGO.UI.Features.Settings.Views;
using GenLauncherGO.UI.Features.Startup.Views;
using GenLauncherGO.UI.Shared.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI;

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
                new LauncherLoadingIndicator(),
            ];

            foreach (ContentControl root in roots)
            {
                root.Content.Should().NotBeNull(
                    because: $"{root.GetType().Name} should load its compiled AXAML");
            }
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
                    isBuiltIn: false),
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                ListBoxItem[] rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                rows.Should().HaveCount(2);
                rows[0].GetVisualDescendants().OfType<Button>().Should()
                    .NotBeEmpty().And.OnlyContain(button => !button.IsVisible);
                rows[1].GetVisualDescendants().OfType<Button>().Should()
                    .NotBeEmpty().And.OnlyContain(button => button.IsVisible);

                TextBlock missingIndicator = rows[1].GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Classes.Contains("missing-executable-indicator"));
                missingIndicator.IsVisible.Should().BeTrue();
                ToolTip.GetTip(missingIndicator).Should().NotBeNull();
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

    private static ModificationViewModel CreateTile(ModificationType modificationType)
    {
        var version = new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation
            {
                ContentSourceKind = ContentSourceKind.Manual,
            },
            Name = "Content",
            Version = "0.3",
            ModificationType = modificationType,
            NewsLink = "https://example.test/news",
            NetworkInfo = "https://example.test/network",
            SupportLink = "https://example.test/support",
            ModDBLink = "https://example.test/moddb",
        };

        return new ModificationViewModel(
            new LauncherContent(version),
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(colors: TestLauncherTheme.Create()),
            Substitute.For<IModificationImageFileService>(),
            new TestStringLocalizer(),
            new LauncherPackageActivityService(),
            NullLogger<ModificationViewModel>.Instance);
    }

    private static TControl FindDescendant<TControl>(Control root, string name)
        where TControl : Control
    {
        return root.GetVisualDescendants()
            .OfType<TControl>()
            .Single(control => control.Name == name);
    }
}
