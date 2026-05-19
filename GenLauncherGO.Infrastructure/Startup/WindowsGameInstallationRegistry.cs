using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using GenLauncherGO.Core.Startup;
using Microsoft.Win32;

namespace GenLauncherGO.Infrastructure.Startup;

/// <summary>
/// Reads the GeneralsGameCode installation registry contract in storefront priority order.
/// </summary>
internal sealed class WindowsGameInstallationRegistry : IGameInstallationRegistry
{
    private const string GeneralsKey =
        @"SOFTWARE\Electronic Arts\EA Games\Generals";
    private const string ZeroHourEaKey =
        @"SOFTWARE\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour";
    private const string ZeroHourSteamKey =
        @"SOFTWARE\Electronic Arts\EA Games\ZeroHour";
    private const string FirstDecadeKey =
        @"SOFTWARE\Electronic Arts\EA Games\Command and Conquer The First Decade";

    private static readonly (string KeyName, string ValueName)[] _generalsProbes =
    {
        // Windows value names are case-insensitive, but GeneralsGameCode declares these
        // separately for Steam and the EA App. Preserve that external contract and order.
        (GeneralsKey, "installPath"),
        (GeneralsKey, "InstallPath"),
        (FirstDecadeKey, "gr_folder"),
    };

    private static readonly (string KeyName, string ValueName)[] _zeroHourProbes =
    {
        (ZeroHourSteamKey, "installPath"),
        (ZeroHourEaKey, "InstallPath"),
        (FirstDecadeKey, "zh_folder"),
    };

    private static readonly RegistryView[] _views =
    {
        RegistryView.Registry32,
        RegistryView.Registry64,
    };

    private readonly Func<RegistryView, string, string, string?> _readValue;

    internal WindowsGameInstallationRegistry()
        : this(ReadLocalMachineValue)
    {
    }

    internal WindowsGameInstallationRegistry(
        Func<RegistryView, string, string, string?> readValue)
    {
        _readValue = readValue ?? throw new ArgumentNullException(nameof(readValue));
    }

    public IReadOnlyList<string> ReadCandidates(SupportedGame game)
    {
        IReadOnlyList<(string KeyName, string ValueName)> probes = game switch
        {
            SupportedGame.Generals => _generalsProbes,
            SupportedGame.ZeroHour => _zeroHourProbes,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "A supported game is required."),
        };

        var candidates = new List<string>();
        foreach ((string keyName, string valueName) in probes)
        {
            foreach (RegistryView view in _views)
            {
                AddCandidate(_readValue(view, keyName, valueName), candidates);
            }
        }

        return candidates;
    }

    private static string? ReadLocalMachineValue(
        RegistryView view,
        string keyName,
        string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? key = baseKey.OpenSubKey(keyName, writable: false);
            return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                as string;
        }
        catch (Exception exception) when (
            exception is IOException or SecurityException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Registry candidates are optional. Every value that is read is validated against the filesystem later.
            return null;
        }
    }

    private static void AddCandidate(string? rawCandidate, List<string> candidates)
    {
        if (rawCandidate is null)
        {
            return;
        }

        string candidate = NormalizeRegistryValue(rawCandidate);
        if (!string.IsNullOrWhiteSpace(candidate) &&
            !candidates.Exists(existing =>
                string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(candidate);
        }
    }

    private static string NormalizeRegistryValue(string value)
    {
        string candidate = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        return Path.TrimEndingDirectorySeparator(candidate);
    }
}
