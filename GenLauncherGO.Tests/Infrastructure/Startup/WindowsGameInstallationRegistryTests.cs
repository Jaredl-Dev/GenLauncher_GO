using System.Collections.Generic;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Startup;
using Microsoft.Win32;

namespace GenLauncherGO.Tests.Infrastructure.Startup;

public sealed class WindowsGameInstallationRegistryTests
{
    private const string GeneralsKey =
        @"SOFTWARE\Electronic Arts\EA Games\Generals";
    private const string ZeroHourEaKey =
        @"SOFTWARE\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour";
    private const string ZeroHourSteamKey =
        @"SOFTWARE\Electronic Arts\EA Games\ZeroHour";
    private const string FirstDecadeKey =
        @"SOFTWARE\Electronic Arts\EA Games\Command and Conquer The First Decade";

    [Fact]
    public void ReadCandidatesQueriesGeneralsSourcesInSteamEaRetailOrder()
    {
        var reads = new List<(RegistryView View, string KeyName, string ValueName)>();
        int candidateNumber = 0;
        var registry = new WindowsGameInstallationRegistry((view, keyName, valueName) =>
        {
            reads.Add((view, keyName, valueName));
            candidateNumber++;
            return $@"C:\Candidate{candidateNumber}";
        });

        IReadOnlyList<string> candidates = registry.ReadCandidates(SupportedGame.Generals);

        reads.Should().Equal(
            (RegistryView.Registry32, GeneralsKey, "installPath"),
            (RegistryView.Registry64, GeneralsKey, "installPath"),
            (RegistryView.Registry32, GeneralsKey, "InstallPath"),
            (RegistryView.Registry64, GeneralsKey, "InstallPath"),
            (RegistryView.Registry32, FirstDecadeKey, "gr_folder"),
            (RegistryView.Registry64, FirstDecadeKey, "gr_folder"));
        candidates.Should().Equal(
            @"C:\Candidate1",
            @"C:\Candidate2",
            @"C:\Candidate3",
            @"C:\Candidate4",
            @"C:\Candidate5",
            @"C:\Candidate6");
    }

    [Fact]
    public void ReadCandidatesQueriesZeroHourSourcesInSteamEaRetailOrder()
    {
        var reads = new List<(RegistryView View, string KeyName, string ValueName)>();
        int candidateNumber = 0;
        var registry = new WindowsGameInstallationRegistry((view, keyName, valueName) =>
        {
            reads.Add((view, keyName, valueName));
            candidateNumber++;
            return $@"C:\Candidate{candidateNumber}";
        });

        IReadOnlyList<string> candidates = registry.ReadCandidates(SupportedGame.ZeroHour);

        reads.Should().Equal(
            (RegistryView.Registry32, ZeroHourSteamKey, "installPath"),
            (RegistryView.Registry64, ZeroHourSteamKey, "installPath"),
            (RegistryView.Registry32, ZeroHourEaKey, "InstallPath"),
            (RegistryView.Registry64, ZeroHourEaKey, "InstallPath"),
            (RegistryView.Registry32, FirstDecadeKey, "zh_folder"),
            (RegistryView.Registry64, FirstDecadeKey, "zh_folder"));
        candidates.Should().Equal(
            @"C:\Candidate1",
            @"C:\Candidate2",
            @"C:\Candidate3",
            @"C:\Candidate4",
            @"C:\Candidate5",
            @"C:\Candidate6");
    }

    [Fact]
    public void ReadCandidatesDeduplicatesEquivalentPathsWithoutChangingPriority()
    {
        string[] values =
        {
            "\"C:\\Steam\\Zero Hour\\\"",
            @"C:\Steam\Zero Hour",
            @"C:\EA\Zero Hour",
            @"c:\ea\zero hour",
            @"C:\Retail\Zero Hour",
            string.Empty,
        };
        int readIndex = 0;
        var registry = new WindowsGameInstallationRegistry((_, _, _) => values[readIndex++]);

        IReadOnlyList<string> candidates = registry.ReadCandidates(SupportedGame.ZeroHour);

        candidates.Should().Equal(
            @"C:\Steam\Zero Hour",
            @"C:\EA\Zero Hour",
            @"C:\Retail\Zero Hour");
    }
}
