using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Testing;

internal static class TestLauncherLaunchCoordinator
{
    public static LauncherLaunchCoordinator Create(
        LauncherPackageActivityService? packageActivityService = null,
        ILauncherPreferencesService? preferencesService = null,
        ILauncherContentCatalog? catalog = null,
        ILauncherStringLocalizer? stringLocalizer = null,
        ILaunchPreparationService? preparationService = null,
        IGameProcessLauncher? processLauncher = null,
        ILauncherDialogService? dialogService = null,
        ILaunchContentIntegrityResolutionService? integrityResolutionService = null)
    {
        LauncherPackageActivityService resolvedPackageActivityService = packageActivityService ?? new();
        ILauncherPreferencesService resolvedPreferencesService =
            preferencesService ?? Substitute.For<ILauncherPreferencesService>();
        if (preferencesService == null)
        {
            resolvedPreferencesService.Current.Returns(new LauncherPreferences());
        }

        ILauncherStringLocalizer resolvedStringLocalizer = stringLocalizer ??
            new TestStringLocalizer(new Dictionary<string, string>
            {
                ["FilesCorrupted"] = "Files corrupted",
                ["GameRunning"] = "Game running",
                ["LaunchAborted"] = "Launch aborted",
                ["LaunchVerificationRunning"] = "Verification running",
                ["Reinstall"] = "Reinstall",
                ["WorldBuilderRunning"] = "World Builder running",
            });
        ILauncherDialogService resolvedDialogService =
            dialogService ?? Substitute.For<ILauncherDialogService>();
        ILaunchPreparationService resolvedPreparationService =
            preparationService ?? Substitute.For<ILaunchPreparationService>();
        if (preparationService == null)
        {
            resolvedPreparationService.Prepare(
                    Arg.Any<LaunchPreparationRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);
            resolvedPreparationService.Cleanup(
                    Arg.Any<GenLauncherGO.Core.Startup.LauncherPaths>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);
        }

        IGameProcessLauncher resolvedProcessLauncher =
            processLauncher ?? Substitute.For<IGameProcessLauncher>();
        if (processLauncher == null)
        {
            IGameProcessLaunchOperation completedProcessOperation = CreateCompletedProcessOperation();
            resolvedProcessLauncher.StartAsync(
                    Arg.Any<GameLaunchRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(completedProcessOperation));
        }

        ILaunchContentIntegrityResolutionService resolutionService =
            integrityResolutionService ?? Substitute.For<ILaunchContentIntegrityResolutionService>();
        if (integrityResolutionService == null)
        {
            resolutionService.VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(new LaunchContentIntegrityVerificationResult(
                    new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>()),
                    Array.Empty<LaunchContentIntegrityTargetContext>()));
        }
        LauncherPaths paths = TestLauncherPaths.Create();
        LauncherRuntimePathContext runtimePaths = TestLauncherPaths.CreateRuntimePathContext(paths);

        return new LauncherLaunchCoordinator(
            resolvedPreferencesService,
            resolvedPreparationService,
            resolvedProcessLauncher,
            new LaunchContentIntegrityCoordinator(
                resolutionService,
                catalog ?? new FakeLauncherContentCatalog(),
                runtimePaths,
                resolvedPackageActivityService,
                resolvedStringLocalizer,
                resolvedDialogService,
                NullLogger<LaunchContentIntegrityCoordinator>.Instance),
            resolvedPackageActivityService,
            runtimePaths,
            resolvedStringLocalizer,
            resolvedDialogService,
            NullLogger<LauncherLaunchCoordinator>.Instance);
    }

    private static IGameProcessLaunchOperation CreateCompletedProcessOperation()
    {
        IGameProcessLaunchOperation operation = Substitute.For<IGameProcessLaunchOperation>();
        operation.CurrentExecutableName.Returns("generals.exe");
        operation.Completion.Returns(Task.FromResult(true));
        return operation;
    }
}
