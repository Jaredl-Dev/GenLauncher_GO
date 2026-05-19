using System;
using System.Collections.Generic;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     The localized text tests assert against, grouped by the feature that needs it.
/// </summary>
/// <remarks>
///     Each key carries one value across every set, so two tests can never assert different text for the same
///     resource. <see cref="Launch" /> includes <see cref="Integrity" /> and <see cref="Launcher" /> includes
///     <see cref="Launch" />, because launching runs integrity review and the launcher window runs both.
///     <see cref="Settings" /> stands alone: it is the executable manager's vocabulary, which shares only display
///     names with the rest.
/// </remarks>
internal static class TestLocalizedStrings
{
    public static IReadOnlyDictionary<string, string> Integrity { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AbsorbAndLaunch"] = "Absorb and launch",
            ["CancelLaunch"] = "Cancel launch",
            ["DownloadInProgress"] = "Downloaded {0} of {1}",
            ["FixAbsorbAndLaunch"] = "Fix, absorb, and launch",
            ["FixAndLaunch"] = "Fix and launch",
            ["IntegrityAbsorbGroup"] = "Absorb",
            ["IntegrityBlockGroup"] = "Blocked",
            ["IntegrityBlockedDescription"] = "Remove blocked entries or restore content",
            ["IntegrityCacheSuffix"] = " cache",
            ["IntegrityDeleteGroup"] = "Delete",
            ["IntegrityDialogTitle"] = "Integrity",
            ["IntegrityIssueEmptyDirectoryLabel"] = "Empty folder",
            ["IntegrityIssueGroupSummaryMultiple"] = "{0} changes",
            ["IntegrityIssueGroupSummarySingle"] = "{0} change",
            ["IntegrityIssueMissingFileLabel"] = "Missing",
            ["IntegrityIssueModifiedFileLabel"] = "Modified",
            ["IntegrityIssueUnexpectedFileLabel"] = "Added",
            ["IntegrityIssueUnsafeLinkLabel"] = "Unsafe link",
            ["IntegrityIssueUntrackedLabel"] = "Untracked",
            ["IntegrityIssueVerificationErrorLabel"] = "Verification error",
            ["IntegrityLegacyDescription"] = "Trust {0}",
            ["IntegrityManagedDescription"] = "Repair {0}",
            ["IntegrityManualDescription"] = "Absorb {0}",
            ["IntegrityMixedDescription"] = "Repair {0}; absorb {1}",
            ["IntegrityRedownloadGroup"] = "Redownload",
            ["IntegrityRepairGroup"] = "Repair",
            ["IntegrityRepeatedFailure"] = "Repeated failure",
            ["IntegrityTrustGroup"] = "Trust",
            ["LaunchVerificationRunning"] = "Verification running",
            ["Preparing"] = "Preparing",
            ["TrustAsManual"] = "Trust as manual",
            ["UnpackingPreparing"] = "Unpacking"
        };

    public static IReadOnlyDictionary<string, string> Launch { get; } = Merge(
        Integrity,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AdvertisingCannotLaunch"] = "Advertisements cannot be launched.",
            ["AdvertisingDonationAlerts"] = "Donate",
            ["Cancel"] = "Cancel",
            ["CancelDownloadAction"] = "Cancel Download",
            ["Canceled"] = "Canceled",
            ["Compatibility"] = "Compatibility",
            ["Delete"] = "Delete",
            ["DeploymentRecoveryFailed"] = "Deployment recovery failed.",
            ["Deprecated"] = "{0} is deprecated",
            ["Error"] = "Error: ",
            ["FilesCorrupted"] = "Files corrupted",
            ["GameRunning"] = "Game running",
            ["GameSwitchBlockedDetails"] = "Wait for active work to finish.",
            ["GameSwitchBlockedTitle"] = "Game switch blocked",
            ["GameSwitchFailedDetails"] = "Could not switch: {0}",
            ["GameSwitchFailedTitle"] = "Game switch failed",
            ["Install"] = "Install",
            ["InstallInProgress"] = "{0} install running",
            ["InstallationPathUnavailable"] = "The installation path is unavailable.",
            ["LaunchAborted"] = "Launch aborted",
            ["LaunchCloseBlockedDetails"] = "Wait for launch.",
            ["LaunchCloseBlockedTitle"] = "Launch in progress",
            ["LatestVersion"] = "Latest version: ",
            ["ModificationsWithUpdate"] = "Updates available",
            ["NotInstalled"] = "{0} is not installed",
            ["PackageActivityInProgress"] = "Package activity",
            ["PackageActivityInProgressDetails"] = "Package activity details",
            ["Pause"] = "Pause",
            ["SettingsSaveFailedDetails"] = "Preferences could not be saved.",
            ["Reinstall"] = "Reinstall",
            ["RemoveFromList"] = "Remove from list",
            ["Resume"] = "Resume",
            ["UnexpectedErrorDetails"] = "Try again",
            ["UnexpectedErrorTitle"] = "Unexpected error",
            ["UninstalledUpdate"] = "{0} update is not installed",
            ["Update"] = "Update",
            ["UpToDate"] = "Up to date",
            ["WorldBuilderRunning"] = "World Builder running"
        });

    public static IReadOnlyDictionary<string, string> Launcher { get; } = Merge(
        Launch,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AddAddonFromFiles"] = "Add addon for {0}",
            ["AddPatchFromFiles"] = "Add patch for {0}",
            ["Addons"] = "Add-ons for ",
            ["CancelDownload"] = "Cancel download",
            ["CancelDownloadDetails"] = "Cancel {0} and delete content downloaded so far",
            ["ChangeToFullScreen"] = "Change to full screen",
            ["ChangeToNormalStart"] = "Change to normal start",
            ["ChangeToQuickStart"] = "Change to quick start",
            ["ChangeToWindowed"] = "Change to windowed",
            ["CloseAnyway"] = "Close anyway",
            ["ClosePackageActivityDetails"] = "Close {0}?",
            ["CommunityGameClientDisplayName"] = "TheSuperHackers",
            ["CurrentVersion"] = "Current version: ",
            ["Discord"] = "Discord",
            ["ExecutableUnavailable"] = "Executable unavailable",
            ["FinishProcess"] = "Finish process",
            ["ForceQuitRunningProcess"] = "Force quit",
            ["ForceQuitRunningProcessConfirmationDetails"] = "Force quit {0}?",
            ["ForceQuitRunningProcessConfirmationTitle"] = "Force quit?",
            ["GameIsStillRunning"] = "Game running",
            ["GeneralsOnlineGameClientDisplayName"] = "GeneralsOnline",
            ["GeneralsShortName"] = "Generals",
            ["GenPatcherRecommendationMessage"] =
                "It is strongly recommended to use GenPatcher when playing the retail game.",
            ["GenPatcherRecommendationTitle"] = "GenPatcher recommendation",
            ["Image"] = "Image",
            ["ModDb"] = "Mod DB",
            ["Patches"] = "Patches for ",
            ["Remove"] = "Remove",
            ["RemoveContent"] = "Remove content?",
            ["RemoveContentDetails"] = "Remove {0}",
            ["RemoveFromListConfirmation"] = "Remove from list?",
            ["RemoveFromListDetails"] = "Remove {0} from list",
            ["RestartBlockedActiveOperation"] = "Finish {0} before restarting.",
            ["RestartBlockedTitle"] = "Restart unavailable",
            ["RetailGameClientDisplayName"] = "Retail",
            ["RetailWorldBuilder"] = "Retail",
            ["RunningProcessCloseBlockedDetails"] = "Close process first",
            ["RunningProcessCloseBlockedTitle"] = "Process running",
            ["RunningProcessStatus"] = "Running {0}",
            ["RunningProcessUnknown"] = "Unknown process",
            ["SetImage"] = "Set image",
            ["SuperHackersWorldBuilder"] = "TheSuperHackers",
            ["ThankYou"] = "Thank you",
            ["VisitGenPatcherDownloadPage"] = "Visit GenPatcher download page",
            ["Yes"] = "Yes",
            ["ZeroHourShortName"] = "Zero Hour"
        });

    public static IReadOnlyDictionary<string, string> Settings { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AddExecutable"] = "Add executable",
            ["BuiltInExecutables"] = "Built-in executables",
            ["CommunityGameClientDisplayName"] = "TheSuperHackers",
            ["CustomExecutables"] = "Custom executables",
            ["EditExecutable"] = "Edit executable",
            ["ExecutableDetailsRequired"] = "Details required",
            ["ExecutableFileAlreadyExists"] = "Duplicate file",
            ["ExecutableMustBeInGameRoot"] = "Must be in game root",
            ["ExecutableNameAlreadyExists"] = "Duplicate name",
            ["ExecutableUnavailable"] = "Unavailable",
            ["GeneralsOnlineGameClientDisplayName"] = "GeneralsOnline",
            ["ManageGameClients"] = "Manage game clients",
            ["ManageWorldBuilders"] = "Manage World Builders",
            ["RetailGameClientDisplayName"] = "Retail",
            ["RetailWorldBuilder"] = "Retail",
            ["SuperHackersWorldBuilder"] = "TheSuperHackers"
        };

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> baseSet,
        Dictionary<string, string> additions)
    {
        var merged = new Dictionary<string, string>(baseSet, StringComparer.Ordinal);
        foreach ((string key, string value) in additions)
        {
            merged[key] = value;
        }

        return merged;
    }
}
