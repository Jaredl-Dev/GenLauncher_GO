using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Shared.IO;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Launching;

/// <summary>
///     Launches Windows game and World Builder processes for supported Command &amp; Conquer clients.
/// </summary>
internal sealed class WindowsGameProcessLauncher : IGameProcessLauncher
{
    /// <summary>
    ///     The observed game-process running time required to treat a launch as successful.
    /// </summary>
    private const int SuccessfulLaunchThresholdMilliseconds = 12000;

    private readonly ILogger<WindowsGameProcessLauncher> _logger;

    private readonly IProcessFamilyLauncher _processFamilyLauncher;

    public WindowsGameProcessLauncher(
        IProcessFamilyLauncher processFamilyLauncher,
        ILogger<WindowsGameProcessLauncher> logger)
    {
        _processFamilyLauncher =
            processFamilyLauncher ?? throw new ArgumentNullException(nameof(processFamilyLauncher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IGameProcessLaunchOperation> StartAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string executablePath = request.ExecutablePath;
        EnsureExecutableCanLaunch(executablePath);
        IProcessFamilyLaunchOperation operation = await _processFamilyLauncher.StartAsync(
            executablePath,
            request.Arguments,
            Path.GetDirectoryName(executablePath)!,
            cancellationToken).ConfigureAwait(false);
        return new WindowsGameProcessLaunchOperation(
            request.TargetKind,
            Path.GetFileName(executablePath),
            operation,
            _logger);
    }

    private static void EnsureExecutableCanLaunch(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The selected executable is no longer available.", executablePath);
        }

        if (FileSystemPathSafety.ExistingPathChainContainsReparsePoint(executablePath, "Executable paths"))
        {
            throw new IOException("The selected executable must not be reached through a symbolic link or other reparse point.");
        }
    }

    /// <summary>
    ///     Applies the game-start success threshold while tracking the launched process family.
    /// </summary>
    private sealed class WindowsGameProcessLaunchOperation : IGameProcessLaunchOperation
    {
        private readonly string _executableName;

        private readonly ILogger<WindowsGameProcessLauncher> _logger;

        private readonly IProcessFamilyLaunchOperation _processFamilyOperation;
        private readonly GameLaunchTargetKind _targetKind;

        public WindowsGameProcessLaunchOperation(
            GameLaunchTargetKind targetKind,
            string executableName,
            IProcessFamilyLaunchOperation processFamilyOperation,
            ILogger<WindowsGameProcessLauncher> logger)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

            _targetKind = targetKind;
            _executableName = executableName;
            _processFamilyOperation = processFamilyOperation ??
                                      throw new ArgumentNullException(nameof(processFamilyOperation));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _processFamilyOperation.CurrentExecutableNameChanged += ProcessFamilyOperation_CurrentExecutableNameChanged;
            Completion = CompleteAsync();
        }

        public string CurrentExecutableName => _processFamilyOperation.CurrentExecutableName;

        public event EventHandler? CurrentExecutableNameChanged;

        public Task<bool> Completion { get; }

        public void ForceClose()
        {
            _processFamilyOperation.ForceClose();
        }

        /// <summary>
        ///     Determines whether the process-family completion satisfies the launch success policy.
        /// </summary>
        private async Task<bool> CompleteAsync()
        {
            try
            {
                TimeSpan runningDuration = await _processFamilyOperation.Completion.ConfigureAwait(false);
                if (_targetKind == GameLaunchTargetKind.GameClient &&
                    runningDuration.TotalMilliseconds < SuccessfulLaunchThresholdMilliseconds)
                {
                    _logger.LogWarning(
                        "Launch of {ExecutableName} ended after {RunningDurationMs}ms, below the success threshold.",
                        _executableName,
                        runningDuration.TotalMilliseconds);
                    return false;
                }

                return true;
            }
            finally
            {
                _processFamilyOperation.CurrentExecutableNameChanged -=
                    ProcessFamilyOperation_CurrentExecutableNameChanged;
            }
        }

        private void ProcessFamilyOperation_CurrentExecutableNameChanged(object? sender, EventArgs e)
        {
            CurrentExecutableNameChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
