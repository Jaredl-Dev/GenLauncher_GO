using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Launching.Services;

/// <summary>
/// Starts Windows processes and waits until the launched process family has exited.
/// </summary>
internal sealed class WindowsProcessFamilyLauncher : IProcessFamilyLauncher
{
    private const int ProcessPollMilliseconds = 500;

    // Some launchers exit before their replacement child becomes visible in a process snapshot.
    private const int ProcessHandoffGraceMilliseconds = 5000;

    private const uint Th32csSnapprocess = 0x00000002;

    private static readonly IntPtr _invalidHandleValue = new(-1);

    private readonly ILogger<WindowsProcessFamilyLauncher> _logger;

    public WindowsProcessFamilyLauncher(ILogger<WindowsProcessFamilyLauncher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IProcessFamilyLaunchOperation> StartAsync(
        string executableName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        return Task.Run(
            () => StartLaunchOperation(
                executableName,
                arguments ?? string.Empty,
                workingDirectory,
                cancellationToken),
            cancellationToken);
    }

    private IProcessFamilyLaunchOperation StartLaunchOperation(
        string executableName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        Process process = StartExecutable(executableName, arguments, workingDirectory);
        return new WindowsProcessFamilyLaunchOperation(
            executableName,
            process,
            new ProcessFamilyTracker(process.Id, executableName, _logger),
            cancellationToken,
            _logger);
    }

    private static Process StartExecutable(
        string executableName,
        string arguments,
        string workingDirectory)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executableName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        });
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start {executableName}.");
        }

        return process;
    }

    /// <summary>
    /// Returns <see langword="null"/> when ToolHelp cannot capture a snapshot so tracking can fall back to the root process.
    /// </summary>
    private static IReadOnlyList<ProcessSnapshotEntry>? TryCaptureProcessSnapshot()
    {
        IntPtr snapshotHandle = CreateToolhelp32Snapshot(Th32csSnapprocess, 0);
        if (snapshotHandle == _invalidHandleValue)
        {
            return null;
        }

        try
        {
            var entries = new List<ProcessSnapshotEntry>();
            var nativeEntry = new NativeProcessEntry
            {
                Size = (uint)Marshal.SizeOf(typeof(NativeProcessEntry)),
            };
            if (!Process32First(snapshotHandle, ref nativeEntry))
            {
                return entries;
            }

            do
            {
                entries.Add(new ProcessSnapshotEntry(
                    unchecked((int)nativeEntry.ProcessId),
                    unchecked((int)nativeEntry.ParentProcessId),
                    nativeEntry.ExecutableFileName ?? string.Empty));
            } while (Process32Next(snapshotHandle, ref nativeEntry));

            return entries;
        }
        finally
        {
            CloseHandle(snapshotHandle);
        }
    }

    // ToolHelp provides parent process ids that System.Diagnostics.Process does not expose reliably.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32First(IntPtr snapshotHandle, ref NativeProcessEntry processEntry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32Next(IntPtr snapshotHandle, ref NativeProcessEntry processEntry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Observes a started Windows process family and exposes a force-close command for it.
    /// </summary>
    private sealed class WindowsProcessFamilyLaunchOperation : IProcessFamilyLaunchOperation
    {
        private readonly ILogger<WindowsProcessFamilyLauncher> _logger;

        private readonly string _executableName;

        private readonly Process _rootProcess;

        private readonly ProcessFamilyTracker _processFamily;

        private readonly CancellationToken _cancellationToken;

        private readonly object _syncRoot = new();

        private bool _disposed;

        public WindowsProcessFamilyLaunchOperation(
            string executableName,
            Process rootProcess,
            ProcessFamilyTracker processFamily,
            CancellationToken cancellationToken,
            ILogger<WindowsProcessFamilyLauncher> logger)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

            _executableName = executableName;
            _rootProcess = rootProcess ?? throw new ArgumentNullException(nameof(rootProcess));
            _processFamily = processFamily ?? throw new ArgumentNullException(nameof(processFamily));
            _cancellationToken = cancellationToken;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Completion = Task.Run(WaitForProcessFamilyExit);
        }

        public string CurrentExecutableName => _processFamily.CurrentExecutableName;

        public event EventHandler? CurrentExecutableNameChanged;

        public Task<TimeSpan> Completion { get; }

        public void ForceClose()
        {
            _logger.LogWarning(
                "Force close requested for launched process family {ExecutableName}.",
                _executableName);
            _processFamily.ForceClose();
        }

        private TimeSpan WaitForProcessFamilyExit()
        {
            string currentExecutableName = CurrentExecutableName;
            try
            {
                while (_processFamily.IsRunning())
                {
                    RaiseCurrentExecutableNameChangedIfNeeded(ref currentExecutableName);
                    if (_cancellationToken.WaitHandle.WaitOne(ProcessPollMilliseconds))
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                RaiseCurrentExecutableNameChangedIfNeeded(ref currentExecutableName);
                return _processFamily.RunningDuration;
            }
            finally
            {
                DisposeRootProcess();
            }
        }

        private void RaiseCurrentExecutableNameChangedIfNeeded(ref string currentExecutableName)
        {
            string updatedExecutableName = CurrentExecutableName;
            if (String.Equals(updatedExecutableName, currentExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            currentExecutableName = updatedExecutableName;
            CurrentExecutableNameChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DisposeRootProcess()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _rootProcess.Dispose();
                _disposed = true;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NativeProcessEntry
    {
        public uint Size;

        public uint UsageCount;

        public uint ProcessId;

        public IntPtr DefaultHeapId;

        public uint ModuleId;

        public uint ThreadCount;

        public uint ParentProcessId;

        public int PriorityClassBase;

        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? ExecutableFileName;
    }

    /// <summary>
    /// Tracks descendants across launcher handoffs until the complete Windows process family exits.
    /// </summary>
    internal sealed class ProcessFamilyTracker
    {
        private readonly Func<IReadOnlyList<ProcessSnapshotEntry>?> _captureProcessSnapshot;

        private readonly Func<int, bool> _isProcessRunning;

        private readonly Func<DateTime> _getUtcNow;

        private readonly Action<int> _forceCloseProcess;

        private readonly object _syncRoot = new();

        private readonly TimeSpan _handoffGracePeriod;

        // Retain recently exited parents so a replacement child appearing in a later snapshot is still discovered.
        private readonly Dictionary<int, TrackedProcess> _trackedProcesses = new();

        private readonly int _rootProcessId;

        private readonly DateTime _startedAtUtc;

        private readonly ILogger<WindowsProcessFamilyLauncher> _logger;

        // An empty snapshot after a child was seen may be a handoff gap rather than the end of the family.
        private DateTime? _emptyFamilyObservedAtUtc;

        private DateTime _lastObservedRunningAtUtc;

        private bool _childProcessObserved;

        private bool _snapshotFailureLogged;

        private string _currentExecutableName;

        private int _nextProcessOrder;

        public ProcessFamilyTracker(
            int rootProcessId,
            string rootExecutableName,
            ILogger<WindowsProcessFamilyLauncher> logger)
            : this(
                rootProcessId,
                rootExecutableName,
                logger,
                TryCaptureProcessSnapshot,
                IsProcessRunning,
                () => DateTime.UtcNow,
                TimeSpan.FromMilliseconds(ProcessHandoffGraceMilliseconds),
                ForceCloseProcess)
        {
        }

        internal ProcessFamilyTracker(
            int rootProcessId,
            string rootExecutableName,
            ILogger<WindowsProcessFamilyLauncher> logger,
            Func<IReadOnlyList<ProcessSnapshotEntry>?> captureProcessSnapshot,
            Func<int, bool> isProcessRunning,
            Func<DateTime> getUtcNow,
            TimeSpan handoffGracePeriod,
            Action<int> forceCloseProcess)
        {
            _rootProcessId = rootProcessId;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _captureProcessSnapshot = captureProcessSnapshot ??
                                      throw new ArgumentNullException(nameof(captureProcessSnapshot));
            _isProcessRunning = isProcessRunning ?? throw new ArgumentNullException(nameof(isProcessRunning));
            _getUtcNow = getUtcNow ?? throw new ArgumentNullException(nameof(getUtcNow));
            _handoffGracePeriod = handoffGracePeriod;
            _forceCloseProcess = forceCloseProcess ?? throw new ArgumentNullException(nameof(forceCloseProcess));
            _startedAtUtc = _getUtcNow();
            _lastObservedRunningAtUtc = _startedAtUtc;
            _currentExecutableName = NormalizeExecutableName(rootExecutableName);
            _trackedProcesses.Add(
                rootProcessId,
                new TrackedProcess(_currentExecutableName, depth: 0, order: _nextProcessOrder++));
        }

        public TimeSpan RunningDuration => _lastObservedRunningAtUtc - _startedAtUtc;

        public string CurrentExecutableName
        {
            get
            {
                lock (_syncRoot)
                {
                    return _currentExecutableName;
                }
            }
        }

        public bool IsRunning()
        {
            lock (_syncRoot)
            {
                return IsRunningCore();
            }
        }

        public void ForceClose()
        {
            IReadOnlyList<int> processIds;
            lock (_syncRoot)
            {
                processIds = GetTrackedRunningProcessIds();
            }

            foreach (int processId in processIds)
            {
                TryForceCloseProcess(processId);
            }
        }

        private bool IsRunningCore()
        {
            IReadOnlyList<ProcessSnapshotEntry>? entries = _captureProcessSnapshot();
            if (entries == null)
            {
                LogSnapshotFailureOnce();
                return IsRootProcessRunning();
            }

            DateTime nowUtc = _getUtcNow();
            ExpireRetiredProcessIds(nowUtc);
            TrackDescendants(entries);

            var runningProcessIds = entries
                .Select(entry => entry.ProcessId)
                .ToHashSet();

            IReadOnlyList<int> knownRunningProcessIds = UpdateKnownProcessState(runningProcessIds, nowUtc);
            UpdateCurrentExecutableName(knownRunningProcessIds);
            if (HasActiveTrackedProcess(knownRunningProcessIds))
            {
                _emptyFamilyObservedAtUtc = null;
                _lastObservedRunningAtUtc = nowUtc;
                return true;
            }

            if (!_childProcessObserved)
            {
                return false;
            }

            _emptyFamilyObservedAtUtc ??= nowUtc;
            return nowUtc - _emptyFamilyObservedAtUtc.Value < _handoffGracePeriod;
        }

        private IReadOnlyList<int> GetTrackedRunningProcessIds()
        {
            IReadOnlyList<ProcessSnapshotEntry>? entries = _captureProcessSnapshot();
            if (entries == null)
            {
                LogSnapshotFailureOnce();
                return _trackedProcesses.Keys
                    .Where(_isProcessRunning)
                    .ToList();
            }

            TrackDescendants(entries);
            var runningProcessIds = entries
                .Select(entry => entry.ProcessId)
                .ToHashSet();
            return _trackedProcesses
                .Where(process => !process.Value.RetiredAtUtc.HasValue)
                .Select(process => process.Key)
                .Where(runningProcessIds.Contains)
                .ToList();
        }

        private void TryForceCloseProcess(int processId)
        {
            try
            {
                _forceCloseProcess(processId);
                _logger.LogWarning("Force closed launched process {ProcessId}.", processId);
            }
            catch (ArgumentException)
            {
                _logger.LogInformation(
                    "Tracked launched process {ProcessId} exited before force close completed.",
                    processId);
            }
            catch (InvalidOperationException)
            {
                _logger.LogInformation(
                    "Tracked launched process {ProcessId} exited before force close completed.",
                    processId);
            }
            catch (Win32Exception ex)
            {
                _logger.LogWarning(ex, "Failed to force close launched process {ProcessId}.", processId);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "Failed to force close launched process {ProcessId}.", processId);
            }
        }

        private void ExpireRetiredProcessIds(DateTime nowUtc)
        {
            foreach (KeyValuePair<int, TrackedProcess> retiredProcess in _trackedProcesses.ToList())
            {
                if (retiredProcess.Value.RetiredAtUtc is not DateTime retiredAtUtc ||
                    nowUtc - retiredAtUtc < _handoffGracePeriod)
                {
                    continue;
                }

                _trackedProcesses.Remove(retiredProcess.Key);
            }
        }

        private void TrackDescendants(IReadOnlyList<ProcessSnapshotEntry> entries)
        {
            bool addedProcess;
            do
            {
                addedProcess = false;
                foreach (ProcessSnapshotEntry entry in entries)
                {
                    if (!_trackedProcesses.TryGetValue(
                            entry.ParentProcessId,
                            out TrackedProcess? parentProcess) ||
                        _trackedProcesses.ContainsKey(entry.ProcessId))
                    {
                        continue;
                    }

                    addedProcess = true;
                    _childProcessObserved = true;
                    var trackedProcess = new TrackedProcess(
                        NormalizeExecutableName(entry.ExecutableFileName),
                        parentProcess.Depth + 1,
                        _nextProcessOrder++);
                    _trackedProcesses.Add(entry.ProcessId, trackedProcess);

                    _logger.LogInformation(
                        "Tracking launched child process {ProcessId} ({ExecutableName}) for cleanup wait.",
                        entry.ProcessId,
                        trackedProcess.ExecutableName);
                }
            } while (addedProcess);
        }

        private IReadOnlyList<int> UpdateKnownProcessState(
            HashSet<int> runningProcessIds,
            DateTime nowUtc)
        {
            var knownRunningProcessIds = new List<int>();
            foreach ((int processId, TrackedProcess process) in _trackedProcesses)
            {
                if (runningProcessIds.Contains(processId) && !process.RetiredAtUtc.HasValue)
                {
                    knownRunningProcessIds.Add(processId);
                    continue;
                }

                if (!process.RetiredAtUtc.HasValue)
                {
                    process.RetiredAtUtc = nowUtc;
                }
            }

            return knownRunningProcessIds;
        }

        private bool HasActiveTrackedProcess(IReadOnlyList<int> knownRunningProcessIds)
        {
            if (!_childProcessObserved)
            {
                return knownRunningProcessIds.Contains(_rootProcessId);
            }

            return knownRunningProcessIds.Any(processId => processId != _rootProcessId);
        }

        private void UpdateCurrentExecutableName(IReadOnlyList<int> knownRunningProcessIds)
        {
            IEnumerable<int> candidates = _childProcessObserved
                ? knownRunningProcessIds.Where(processId => processId != _rootProcessId)
                : knownRunningProcessIds;
            int? currentProcessId = candidates
                .OrderByDescending(GetProcessDepth)
                .ThenByDescending(GetProcessOrder)
                .Cast<int?>()
                .FirstOrDefault();
            if (!currentProcessId.HasValue)
            {
                return;
            }

            if (_trackedProcesses.TryGetValue(currentProcessId.Value, out TrackedProcess? process) &&
                !String.IsNullOrWhiteSpace(process.ExecutableName))
            {
                _currentExecutableName = process.ExecutableName;
            }
        }

        private int GetProcessDepth(int processId)
        {
            return _trackedProcesses.TryGetValue(processId, out TrackedProcess? process)
                ? process.Depth
                : 0;
        }

        private int GetProcessOrder(int processId)
        {
            return _trackedProcesses.TryGetValue(processId, out TrackedProcess? process)
                ? process.Order
                : 0;
        }

        private bool IsRootProcessRunning()
        {
            if (!_isProcessRunning(_rootProcessId))
            {
                return false;
            }

            if (_trackedProcesses.TryGetValue(_rootProcessId, out TrackedProcess? process) &&
                !String.IsNullOrWhiteSpace(process.ExecutableName))
            {
                _currentExecutableName = process.ExecutableName;
            }

            _lastObservedRunningAtUtc = _getUtcNow();
            return true;
        }

        private void LogSnapshotFailureOnce()
        {
            if (_snapshotFailureLogged)
            {
                return;
            }

            _logger.LogWarning(
                "Could not inspect launched child processes; falling back to the root launch process only.");
            _snapshotFailureLogged = true;
        }

        private static string NormalizeExecutableName(string? executableName)
        {
            return executableName?.Trim() ?? string.Empty;
        }

        private sealed class TrackedProcess
        {
            public TrackedProcess(string executableName, int depth, int order)
            {
                ExecutableName = executableName;
                Depth = depth;
                Order = order;
            }

            public string ExecutableName { get; }

            public int Depth { get; }

            public int Order { get; }

            public DateTime? RetiredAtUtc { get; set; }
        }
    }

    internal sealed record ProcessSnapshotEntry(
        int ProcessId,
        int ParentProcessId,
        string ExecutableFileName = "");

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void ForceCloseProcess(int processId)
    {
        using var process = Process.GetProcessById(processId);
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
