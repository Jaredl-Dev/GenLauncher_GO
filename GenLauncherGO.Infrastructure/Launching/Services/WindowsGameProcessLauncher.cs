using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Launching.Services;

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

        string executableName = Path.Combine(request.GameDirectory, request.ExecutableName);
        try
        {
            EnsureExecutableCanLaunch(executableName);
            IProcessFamilyLaunchOperation operation = await _processFamilyLauncher.StartAsync(
                executableName,
                request.Arguments,
                request.GameDirectory,
                cancellationToken).ConfigureAwait(false);
            return new WindowsGameProcessLaunchOperation(
                request.TargetKind,
                executableName,
                operation,
                _logger);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to launch {ExecutableName}.", executableName);
            throw;
        }
    }

    private static void EnsureExecutableCanLaunch(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The selected executable is no longer available.", executablePath);
        }

        if (FileSystemPathSafety.IsReparsePoint(executablePath))
        {
            throw new IOException("The selected executable must not be a symbolic link or other reparse point.");
        }
    }

    /// <summary>
    ///     Adapts an infrastructure process-family operation to the Core game-launch operation contract.
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
                    _logger.LogInformation(
                        "Launch of {ExecutableName} ended after {RunningDurationMs}ms, below the success threshold.",
                        _executableName,
                        runningDuration.TotalMilliseconds);
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed while waiting for launched process {ExecutableName}.", _executableName);
                throw;
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
