using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class WindowsProcessFamilyLauncherTests
{
    /// <summary>
    ///     A descendant that outlives every wait in these tests, so a launch only ends because the launcher stopped it.
    /// </summary>
    private const string LongRunningChildArguments = "/d /c ping.exe -n 60 127.0.0.1 >nul";

    private const string ChildExecutableFileName = "ping.exe";

    [Fact]
    public async Task StartAsync_UsesArgumentsAndWorkingDirectoryBeforeCompletingAsync()
    {
        using TestDirectory directory = new();
        WindowsProcessFamilyLauncher launcher = new(NullLogger<WindowsProcessFamilyLauncher>.Instance);
        string executableName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        const string MarkerFileName = "launcher-marker.txt";

        IProcessFamilyLaunchOperation operation = await launcher.StartAsync(
            executableName,
            $"/d /c echo launched>{MarkerFileName}",
            directory.Path,
            CancellationToken.None);
        await operation.Completion.WaitAsync(TestTimeouts.Wait);

        File.ReadAllText(directory.GetPath(MarkerFileName)).Trim().Should().Be("launched");
    }

    [Fact]
    public void ProcessFamilyTracker_StopsImmediatelyWhenStableNestedDescendantExits()
    {
        ManualTimeProvider clock = new();
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10), (30, 20)),
            Snapshot((30, 20)),
            Snapshot()
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            timeProvider: clock);

        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.IsRunning().Should().BeFalse();
        tracker.RunningDuration.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ProcessFamilyTracker_AllowsHandoffChildFromRecentlyRetiredParent()
    {
        ManualTimeProvider clock = new();
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10)),
            Snapshot(),
            Snapshot((30, 20))
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            timeProvider: clock);

        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(250));
        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(250));
        tracker.IsRunning().Should().BeTrue();
    }

    [Fact]
    public void ProcessFamilyTracker_RejectsHandoffChildAfterParentRetirementExpires()
    {
        ManualTimeProvider clock = new();
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10)),
            Snapshot(),
            Snapshot((30, 20))
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            timeProvider: clock);

        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.IsRunning().Should().BeFalse();
    }

    [Fact]
    public void ProcessFamilyTracker_StopsImmediatelyWhenRootExitsWithoutChildren()
    {
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            () => Snapshot());

        bool result = tracker.IsRunning();

        result.Should().BeFalse();
    }

    [Fact]
    public void ProcessFamilyTracker_FallsBackToRootProcessWhenSnapshotsFail()
    {
        ManualTimeProvider clock = new();
        Queue<bool> rootRunningStates = new(new[] { true, false });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            () => null,
            isProcessRunning: _ => rootRunningStates.Dequeue(),
            timeProvider: clock);

        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(3));
        tracker.IsRunning().Should().BeFalse();
        tracker.RunningDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ProcessFamilyTrackerForceClose_TargetsTrackedRunningFamily()
    {
        List<int> forceClosedProcessIds = [];
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            () => Snapshot((10, 1), (20, 10), (30, 20), (40, 99)),
            forceCloseProcess: forceClosedProcessIds.Add);
        tracker.IsRunning().Should().BeTrue();

        tracker.ForceClose();

        forceClosedProcessIds.Should().BeEquivalentTo(new[] { 10, 20, 30 });
    }

    [Fact]
    public void ProcessFamilyTrackerForceClose_SkipsDescendantThatAlreadyExited()
    {
        List<int> forceClosedProcessIds = [];
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10), (30, 20), (40, 99)),
            Snapshot((10, 1), (30, 20), (40, 99)),
            Snapshot((10, 1), (30, 20), (40, 99))
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            forceCloseProcess: forceClosedProcessIds.Add);
        tracker.IsRunning().Should().BeTrue();
        tracker.IsRunning().Should().BeTrue();

        tracker.ForceClose();

        forceClosedProcessIds.Should().BeEquivalentTo(new[] { 10, 30 });
    }

    [Fact]
    public void ProcessFamilyTrackerForceClose_WhenSnapshotsFail_TargetsOnlyStillRunningTrackedProcesses()
    {
        List<int> forceClosedProcessIds = [];
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10)),
            null
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            isProcessRunning: processId => processId == 10,
            forceCloseProcess: forceClosedProcessIds.Add);
        tracker.IsRunning().Should().BeTrue();

        tracker.ForceClose();

        forceClosedProcessIds.Should().BeEquivalentTo(new[] { 10 });
    }

    [Fact]
    public void ProcessFamilyTracker_UpdatesCurrentExecutableToDeepestRunningDescendant()
    {
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            NamedSnapshot((10, 1, "generalsonlinezh.exe")),
            NamedSnapshot((10, 1, "generalsonlinezh.exe"), (20, 10, "generalszh.exe")),
            NamedSnapshot(
                (10, 1, "generalsonlinezh.exe"),
                (20, 10, "generalszh.exe"),
                (30, 20, "game.dat"))
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            rootExecutableName: "generalsonlinezh.exe",
            captureProcessSnapshot: snapshots.Dequeue);

        tracker.CurrentExecutableName.Should().Be("generalsonlinezh.exe");
        tracker.IsRunning().Should().BeTrue();
        tracker.CurrentExecutableName.Should().Be("generalsonlinezh.exe");
        tracker.IsRunning().Should().BeTrue();
        tracker.CurrentExecutableName.Should().Be("generalszh.exe");
        tracker.IsRunning().Should().BeTrue();
        tracker.CurrentExecutableName.Should().Be("game.dat");
    }

    [Fact]
    public void ProcessFamilyTracker_StopsAfterChildHandoffExitsEvenWhenRootLauncherStillRuns()
    {
        ManualTimeProvider clock = new();
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            NamedSnapshot((10, 1, "generalsonlinezh.exe"), (20, 10, "generalszh.exe")),
            NamedSnapshot((10, 1, "generalsonlinezh.exe")),
            NamedSnapshot((10, 1, "generalsonlinezh.exe"))
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            rootExecutableName: "generalsonlinezh.exe",
            captureProcessSnapshot: snapshots.Dequeue,
            timeProvider: clock);

        tracker.IsRunning().Should().BeTrue();
        tracker.CurrentExecutableName.Should().Be("generalszh.exe");
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.IsRunning().Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_ReportsHowLongTheProcessFamilyRanAsync()
    {
        using TestDirectory directory = new();
        WindowsProcessFamilyLauncher launcher = new(NullLogger<WindowsProcessFamilyLauncher>.Instance);

        IProcessFamilyLaunchOperation operation = await launcher.StartAsync(
            CommandProcessorFileName,
            "/d /c ping.exe -n 4 127.0.0.1 >nul",
            directory.Path,
            CancellationToken.None);
        TimeSpan runningDuration = await operation.Completion.WaitAsync(TimeSpan.FromSeconds(25));

        runningDuration.Should().BeGreaterThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task StartAsync_ReportsTheDescendantExecutableWhileTheFamilyRunsAsync()
    {
        using TestDirectory directory = new();
        WindowsProcessFamilyLauncher launcher = new(NullLogger<WindowsProcessFamilyLauncher>.Instance);
        IProcessFamilyLaunchOperation operation = await launcher.StartAsync(
            CommandProcessorFileName,
            LongRunningChildArguments,
            directory.Path,
            CancellationToken.None);

        try
        {
            List<string> reportedExecutableNames = [];
            operation.CurrentExecutableNameChanged += (_, _) =>
                reportedExecutableNames.Add(operation.CurrentExecutableName);

            bool reportedTheChild = await WaitUntilAsync(
                () => reportedExecutableNames.Exists(IsChildExecutableName),
                TimeSpan.FromSeconds(12));

            reportedTheChild.Should().BeTrue();
        }
        finally
        {
            operation.ForceClose();
            await operation.Completion.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Fact]
    public async Task StartAsync_DoesNotReportExecutableNameChangesWhileTheSameProcessRunsAsync()
    {
        using TestDirectory directory = new();
        WindowsProcessFamilyLauncher launcher = new(NullLogger<WindowsProcessFamilyLauncher>.Instance);
        int reportedNameChangeCount = 0;

        IProcessFamilyLaunchOperation operation = await launcher.StartAsync(
            ChildExecutableFileName,
            "-n 4 127.0.0.1",
            directory.Path,
            CancellationToken.None);
        operation.CurrentExecutableNameChanged += (_, _) => Interlocked.Increment(ref reportedNameChangeCount);
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(25));

        reportedNameChangeCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_WhenTheLaunchIsCanceled_ReportsCancellationAsync()
    {
        using CancellationTokenSource cancellation = new();
        WindowsProcessFamilyLauncher launcher = new(NullLogger<WindowsProcessFamilyLauncher>.Instance);
        IProcessFamilyLaunchOperation operation = await launcher.StartAsync(
            CommandProcessorFileName,
            LongRunningChildArguments,
            Path.GetTempPath(),
            cancellation.Token);

        try
        {
            await cancellation.CancelAsync();

            Func<Task> completion = () => operation.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            await completion.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            operation.ForceClose();
        }
    }

    [Fact]
    public async Task ForceClose_StopsTheLaunchedProcessFamilyAsync()
    {
        using TestDirectory directory = new();
        WindowsProcessFamilyLauncher launcher = new(NullLogger<WindowsProcessFamilyLauncher>.Instance);
        IProcessFamilyLaunchOperation operation = await launcher.StartAsync(
            CommandProcessorFileName,
            LongRunningChildArguments,
            directory.Path,
            CancellationToken.None);
        await WaitUntilAsync(
            () => IsChildExecutableName(operation.CurrentExecutableName),
            TimeSpan.FromSeconds(12));

        operation.ForceClose();

        Func<Task> familyExit = () => operation.Completion.WaitAsync(TimeSpan.FromSeconds(20));
        await familyExit.Should().NotThrowAsync();
    }

    [Fact]
    public void ProcessFamilyTracker_ReportsMostRecentlyDiscoveredSiblingAsCurrentExecutable()
    {
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            () => NamedSnapshot(
                (10, 1, "generalsonlinezh.exe"),
                (20, 10, "handoff-helper.exe"),
                (30, 10, "generalszh.exe")),
            rootExecutableName: "generalsonlinezh.exe");
        tracker.IsRunning().Should().BeTrue();

        string currentExecutableName = tracker.CurrentExecutableName;

        currentExecutableName.Should().Be("generalszh.exe");
    }

    [Fact]
    public void ProcessFamilyTracker_ReportsDeepestDescendantRatherThanNewestSiblingAsCurrentExecutable()
    {
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            () => NamedSnapshot(
                (10, 1, "generalsonlinezh.exe"),
                (20, 10, "generalszh.exe"),
                (30, 20, "game.dat"),
                (40, 10, "handoff-helper.exe")),
            rootExecutableName: "generalsonlinezh.exe");
        tracker.IsRunning().Should().BeTrue();

        string currentExecutableName = tracker.CurrentExecutableName;

        currentExecutableName.Should().Be("game.dat");
    }

    [Fact]
    public void ProcessFamilyTracker_KeepsTheDescendantNameWhileOnlyTheLauncherRootRemains()
    {
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            NamedSnapshot((10, 1, "generalsonlinezh.exe"), (20, 10, "generalszh.exe")),
            NamedSnapshot((10, 1, "generalsonlinezh.exe"))
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            rootExecutableName: "generalsonlinezh.exe");
        tracker.IsRunning().Should().BeTrue();
        tracker.IsRunning().Should().BeTrue();

        string currentExecutableName = tracker.CurrentExecutableName;

        currentExecutableName.Should().Be("generalszh.exe");
    }

    /// <summary>
    ///     A process snapshot is not ordered parent-first, so a grandchild listed before its parent must still be
    ///     tracked instead of escaping the cleanup wait.
    /// </summary>
    [Fact]
    public void ProcessFamilyTracker_TracksADescendantListedBeforeItsParent()
    {
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            () => NamedSnapshot(
                (30, 20, "game.dat"),
                (20, 10, "generalszh.exe"),
                (10, 1, "generalsonlinezh.exe")),
            rootExecutableName: "generalsonlinezh.exe");
        tracker.IsRunning().Should().BeTrue();

        string currentExecutableName = tracker.CurrentExecutableName;

        currentExecutableName.Should().Be("game.dat");
    }

    [Fact]
    public void ProcessFamilyTracker_KeepsTheCurrentExecutableWhenADescendantHasNoName()
    {
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            () => NamedSnapshot((10, 1, "generalszh.exe"), (20, 10, "")),
            rootExecutableName: "generalszh.exe");
        tracker.IsRunning().Should().BeTrue();

        string currentExecutableName = tracker.CurrentExecutableName;

        currentExecutableName.Should().Be("generalszh.exe");
    }

    [Fact]
    public void ProcessFamilyTracker_WhenSnapshotsStopWorking_ReportsTheRootExecutableName()
    {
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            NamedSnapshot((10, 1, "generalsonlinezh.exe"), (20, 10, "generalszh.exe")),
            null
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            rootExecutableName: "generalsonlinezh.exe",
            isProcessRunning: processId => processId == 10);
        tracker.IsRunning().Should().BeTrue();
        tracker.CurrentExecutableName.Should().Be("generalszh.exe");

        tracker.IsRunning().Should().BeTrue();

        tracker.CurrentExecutableName.Should().Be("generalsonlinezh.exe");
    }

    [Fact]
    public void ProcessFamilyTracker_MeasuresTheHandoffWindowFromTheDeepestRetiredDescendant()
    {
        ManualTimeProvider clock = new();
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10)),
            Snapshot((10, 1), (20, 10), (30, 20)),
            Snapshot()
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            timeProvider: clock);
        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(400));
        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(200));

        bool running = tracker.IsRunning();

        running.Should().BeTrue();
    }

    [Fact]
    public void ProcessFamilyTracker_MeasuresTheHandoffWindowFromTheNewestRetiredSibling()
    {
        ManualTimeProvider clock = new();
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10)),
            Snapshot((10, 1), (20, 10), (30, 10)),
            Snapshot()
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            timeProvider: clock);
        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(400));
        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(200));

        bool running = tracker.IsRunning();

        running.Should().BeTrue();
    }

    [Fact]
    public void ProcessFamilyTracker_ClosesTheHandoffWindowExactlyOneGracePeriodAfterTheChildWasSeen()
    {
        ManualTimeProvider clock = new();
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10)),
            Snapshot(),
            Snapshot()
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            timeProvider: clock);
        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(300));
        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(200));

        bool running = tracker.IsRunning();

        running.Should().BeFalse();
    }

    [Fact]
    public void ProcessFamilyTracker_ForgetsARetiredParentExactlyOneGracePeriodAfterItExited()
    {
        ManualTimeProvider clock = new();
        Queue<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> snapshots = new(new[]
        {
            Snapshot((10, 1), (20, 10)),
            Snapshot(),
            Snapshot((30, 20))
        });
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            snapshots.Dequeue,
            timeProvider: clock);
        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(100));
        tracker.IsRunning().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(500));

        bool running = tracker.IsRunning();

        running.Should().BeFalse();
    }

    [Fact]
    public void ProcessFamilyTrackerForceClose_DiscoversDescendantsWithoutAPriorRunningCheck()
    {
        List<int> forceClosedProcessIds = [];
        WindowsProcessFamilyLauncher.ProcessFamilyTracker tracker = CreateTracker(
            10,
            () => Snapshot((10, 1), (20, 10), (30, 20), (40, 99)),
            forceCloseProcess: forceClosedProcessIds.Add);

        tracker.ForceClose();

        forceClosedProcessIds.Should().BeEquivalentTo(new[] { 10, 20, 30 });
    }

    private static string CommandProcessorFileName =>
        Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

    private static bool IsChildExecutableName(string executableName)
    {
        return executableName.StartsWith("ping", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Polls until <paramref name="condition" /> holds, so a test can observe the launcher's own background poll
    ///     loop without depending on how fast Windows starts the descendant process.
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadlineUtc = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadlineUtc)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        return condition();
    }

    private static WindowsProcessFamilyLauncher.ProcessFamilyTracker CreateTracker(
        int rootProcessId,
        Func<IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry>?> captureProcessSnapshot,
        string rootExecutableName = "",
        Func<int, bool>? isProcessRunning = null,
        ManualTimeProvider? timeProvider = null,
        Action<int>? forceCloseProcess = null)
    {
        return new WindowsProcessFamilyLauncher.ProcessFamilyTracker(
            rootProcessId,
            rootExecutableName,
            NullLogger<WindowsProcessFamilyLauncher>.Instance,
            captureProcessSnapshot,
            isProcessRunning ?? (_ => false),
            timeProvider ?? new ManualTimeProvider(),
            TimeSpan.FromMilliseconds(500),
            forceCloseProcess ?? (_ => { }));
    }

    private static IReadOnlyList<WindowsProcessFamilyLauncher.ProcessSnapshotEntry> Snapshot(
        params (int ProcessId, int ParentProcessId)[] entries)
    {
        List<WindowsProcessFamilyLauncher.ProcessSnapshotEntry> snapshot = [];
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
        List<WindowsProcessFamilyLauncher.ProcessSnapshotEntry> snapshot = [];
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
