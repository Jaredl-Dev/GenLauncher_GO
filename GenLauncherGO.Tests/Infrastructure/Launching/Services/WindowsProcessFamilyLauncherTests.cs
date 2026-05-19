using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class WindowsProcessFamilyLauncherTests
{
    [Fact]
    public async Task StartAsyncTracksDurationForShortLivedProcessAsync()
    {
        WindowsProcessFamilyLauncher launcher = new(NullLogger<WindowsProcessFamilyLauncher>.Instance);
        string executableName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

        IProcessFamilyLaunchOperation operation = await launcher.StartAsync(
            executableName,
            "/c exit 0",
            Environment.CurrentDirectory,
            CancellationToken.None);
        TimeSpan duration = await operation.Completion;

        duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void ProcessFamilyTrackerTracksNestedDescendantsUntilGracePeriodExpires()
    {
        DateTime nowUtc = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10), (30, 20)),
            Snapshot((30, 20)),
            Snapshot(),
            Snapshot(),
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            rootProcessId: 10,
            captureProcessSnapshot: () => snapshots.Dequeue(),
            getUtcNow: () => nowUtc);

        tracker.IsRunning().Should().BeTrue();
        nowUtc = nowUtc.AddSeconds(1);
        tracker.IsRunning().Should().BeTrue();
        nowUtc = nowUtc.AddSeconds(1);
        tracker.IsRunning().Should().BeTrue();
        nowUtc = nowUtc.AddSeconds(6);
        tracker.IsRunning().Should().BeFalse();
        tracker.RunningDuration.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ProcessFamilyTrackerAllowsHandoffChildFromRecentlyRetiredParent()
    {
        DateTime nowUtc = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10)),
            Snapshot(),
            Snapshot((30, 20)),
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            rootProcessId: 10,
            captureProcessSnapshot: () => snapshots.Dequeue(),
            getUtcNow: () => nowUtc);

        tracker.IsRunning().Should().BeTrue();
        nowUtc = nowUtc.AddSeconds(1);
        tracker.IsRunning().Should().BeTrue();
        nowUtc = nowUtc.AddSeconds(1);
        tracker.IsRunning().Should().BeTrue();
    }

    [Fact]
    public void ProcessFamilyTrackerRejectsHandoffChildAfterParentRetirementExpires()
    {
        DateTime nowUtc = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10)),
            Snapshot(),
            Snapshot((30, 20)),
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            rootProcessId: 10,
            captureProcessSnapshot: () => snapshots.Dequeue(),
            getUtcNow: () => nowUtc);

        tracker.IsRunning().Should().BeTrue();
        nowUtc = nowUtc.AddSeconds(1);
        tracker.IsRunning().Should().BeTrue();
        nowUtc = nowUtc.AddSeconds(6);
        tracker.IsRunning().Should().BeFalse();
    }

    [Fact]
    public void ProcessFamilyTrackerStopsImmediatelyWhenRootExitsWithoutChildren()
    {
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            rootProcessId: 10,
            captureProcessSnapshot: () => Snapshot());

        bool result = tracker.IsRunning();

        result.Should().BeFalse();
    }

    [Fact]
    public void ProcessFamilyTrackerFallsBackToRootProcessWhenSnapshotsFail()
    {
        DateTime nowUtc = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        Queue<bool> rootRunningStates = new(new[] { true, false });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            rootProcessId: 10,
            captureProcessSnapshot: () => null,
            isProcessRunning: _ => rootRunningStates.Dequeue(),
            getUtcNow: () => nowUtc);

        tracker.IsRunning().Should().BeTrue();
        nowUtc = nowUtc.AddSeconds(3);
        tracker.IsRunning().Should().BeFalse();
        tracker.RunningDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ProcessFamilyTrackerForceCloseTargetsTrackedRunningFamily()
    {
        List<int> forceClosedProcessIds = new();
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            rootProcessId: 10,
            captureProcessSnapshot: () => Snapshot((10, 1), (20, 10), (30, 20), (40, 99)),
            forceCloseProcess: forceClosedProcessIds.Add);
        tracker.IsRunning().Should().BeTrue();

        tracker.ForceClose();

        forceClosedProcessIds.Should().BeEquivalentTo(new[] { 10, 20, 30 });
    }

    [Fact]
    public void ProcessFamilyTrackerUpdatesCurrentExecutableToDeepestRunningDescendant()
    {
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            NamedSnapshot((10, 1, "generalsonlinezh.exe")),
            NamedSnapshot((10, 1, "generalsonlinezh.exe"), (20, 10, "generalszh.exe")),
            NamedSnapshot(
                (10, 1, "generalsonlinezh.exe"),
                (20, 10, "generalszh.exe"),
                (30, 20, "game.dat")),
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            rootProcessId: 10,
            rootExecutableName: "generalsonlinezh.exe",
            captureProcessSnapshot: () => snapshots.Dequeue());

        tracker.CurrentExecutableName.Should().Be("generalsonlinezh.exe");
        tracker.IsRunning().Should().BeTrue();
        tracker.CurrentExecutableName.Should().Be("generalsonlinezh.exe");
        tracker.IsRunning().Should().BeTrue();
        tracker.CurrentExecutableName.Should().Be("generalszh.exe");
        tracker.IsRunning().Should().BeTrue();
        tracker.CurrentExecutableName.Should().Be("game.dat");
    }

    [Fact]
    public void ProcessFamilyTrackerStopsAfterChildHandoffExitsEvenWhenRootLauncherStillRuns()
    {
        DateTime nowUtc = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            NamedSnapshot((10, 1, "generalsonlinezh.exe"), (20, 10, "generalszh.exe")),
            NamedSnapshot((10, 1, "generalsonlinezh.exe")),
            NamedSnapshot((10, 1, "generalsonlinezh.exe")),
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            rootProcessId: 10,
            rootExecutableName: "generalsonlinezh.exe",
            captureProcessSnapshot: () => snapshots.Dequeue(),
            getUtcNow: () => nowUtc);

        tracker.IsRunning().Should().BeTrue();
        tracker.CurrentExecutableName.Should().Be("generalszh.exe");
        nowUtc = nowUtc.AddSeconds(1);
        tracker.IsRunning().Should().BeTrue();
        tracker.CurrentExecutableName.Should().Be("generalszh.exe");
        nowUtc = nowUtc.AddSeconds(6);
        tracker.IsRunning().Should().BeFalse();
    }

    private static WindowsProcessFamilyLauncher.ProcessFamilyTracker CreateTracker(
        int rootProcessId,
        Func<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> captureProcessSnapshot,
        string rootExecutableName = "",
        Func<int, bool>? isProcessRunning = null,
        Func<DateTime>? getUtcNow = null,
        Action<int>? forceCloseProcess = null)
    {
        return new WindowsProcessFamilyLauncher.ProcessFamilyTracker(
            rootProcessId,
            rootExecutableName,
            NullLogger<WindowsProcessFamilyLauncher>.Instance,
            captureProcessSnapshot,
            isProcessRunning ?? (_ => false),
            getUtcNow ?? (() => new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc)),
            TimeSpan.FromSeconds(5),
            forceCloseProcess ?? (_ => { }));
    }

    private static IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry> Snapshot(
        params (int ProcessId, int ParentProcessId)[] entries)
    {
        List<WindowsProcessFamilyLauncher.ProcessSnapshotEntry> snapshot = new();
        foreach ((int processId, int parentProcessId) in entries)
        {
            snapshot.Add(new WindowsProcessFamilyLauncher.ProcessSnapshotEntry(
                processId,
                parentProcessId));
        }

        return snapshot;
    }

    private static IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry> NamedSnapshot(
        params (int ProcessId, int ParentProcessId, string ExecutableFileName)[] entries)
    {
        List<WindowsProcessFamilyLauncher.ProcessSnapshotEntry> snapshot = new();
        foreach ((int processId, int parentProcessId, string executableFileName) in entries)
        {
            snapshot.Add(new WindowsProcessFamilyLauncher.ProcessSnapshotEntry(
                processId,
                parentProcessId,
                executableFileName));
        }

        return snapshot;
    }
}
