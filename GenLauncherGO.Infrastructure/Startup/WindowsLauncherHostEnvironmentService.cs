using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Infrastructure.Startup;

/// <summary>
/// Provides Windows process, elevation, single-instance, and foreground-window startup operations.
/// </summary>
public sealed class WindowsLauncherHostEnvironmentService : ILauncherHostEnvironmentService
{
    private const int SwRestore = 9;

    private readonly ILogger<WindowsLauncherHostEnvironmentService> _logger;
    private readonly Action<TimeSpan> _waitBeforeSingleInstanceRetry;

    public WindowsLauncherHostEnvironmentService()
        : this(NullLogger<WindowsLauncherHostEnvironmentService>.Instance)
    {
    }

    public WindowsLauncherHostEnvironmentService(ILogger<WindowsLauncherHostEnvironmentService> logger)
        : this(logger, Thread.Sleep)
    {
    }

    internal WindowsLauncherHostEnvironmentService(
        ILogger<WindowsLauncherHostEnvironmentService> logger,
        Action<TimeSpan> waitBeforeSingleInstanceRetry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _waitBeforeSingleInstanceRetry = waitBeforeSingleInstanceRetry ??
            throw new ArgumentNullException(nameof(waitBeforeSingleInstanceRetry));
    }

    public void ActivateCurrentProcessWindow()
    {
        using var currentProcess = Process.GetCurrentProcess();
        Process? process = Process.GetProcessesByName(currentProcess.ProcessName)
            .FirstOrDefault(candidate => candidate.Id != currentProcess.Id);
        IntPtr windowHandle = process?.MainWindowHandle ?? IntPtr.Zero;

        if (windowHandle == IntPtr.Zero)
        {
            _logger.LogDebug("No existing launcher window was available to activate.");
            return;
        }

        ShowWindowAsync(new HandleRef(null, windowHandle), SwRestore);
        SetForegroundWindow(windowHandle);
    }

    public string GetExecutableDirectory()
    {
        string? executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;

        if (String.IsNullOrWhiteSpace(executablePath))
        {
            return AppContext.BaseDirectory;
        }

        return Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
    }

    public bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public bool IsProtectedProgramFilesDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return IsPathInDirectoryWhenKnown(directory, programFiles) ||
               IsPathInDirectoryWhenKnown(directory, programFilesX86);
    }

    public LauncherRestartResult TryRestartCurrentProcess()
    {
        try
        {
            string? executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (String.IsNullOrWhiteSpace(executablePath))
            {
                const string missingExecutableMessage = "The launcher executable path could not be resolved.";
                _logger.LogError(missingExecutableMessage);
                return LauncherRestartResult.Failure(missingExecutableMessage);
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
            });
            if (process == null)
            {
                const string startFailureMessage = "Windows did not start the replacement launcher process.";
                _logger.LogError(startFailureMessage);
                return LauncherRestartResult.Failure(startFailureMessage);
            }

            process.Dispose();
            _logger.LogInformation("Started a replacement launcher process for restart.");
            return LauncherRestartResult.Success;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not start a replacement launcher process.");
            return LauncherRestartResult.Failure(exception.Message);
        }
    }

    public ILauncherSingleInstanceGuard TryAcquireSingleInstance(string instanceName, TimeSpan retryDelay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);

        Mutex mutex = new(initiallyOwned: true, instanceName, out bool createdNew);
        if (createdNew)
        {
            return new MutexSingleInstanceGuard(mutex, isAcquired: true);
        }

        mutex.Dispose();
        if (retryDelay > TimeSpan.Zero)
        {
            _waitBeforeSingleInstanceRetry(retryDelay);
        }

        mutex = new Mutex(initiallyOwned: true, instanceName, out createdNew);
        if (createdNew)
        {
            return new MutexSingleInstanceGuard(mutex, isAcquired: true);
        }

        mutex.Dispose();
        return MutexSingleInstanceGuard.NotAcquired;
    }

    private static bool IsPathInDirectoryWhenKnown(string path, string directory)
    {
        return !String.IsNullOrWhiteSpace(directory) &&
               LexicalPath.IsPathInDirectory(path, directory);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(HandleRef hWnd, int nCmdShow);

    private sealed class MutexSingleInstanceGuard : ILauncherSingleInstanceGuard
    {
        public static readonly MutexSingleInstanceGuard NotAcquired = new(null, isAcquired: false);

        private readonly Mutex? _mutex;

        public MutexSingleInstanceGuard(Mutex? mutex, bool isAcquired)
        {
            _mutex = mutex;
            IsAcquired = isAcquired;
        }

        public bool IsAcquired { get; }

        public void Dispose()
        {
            _mutex?.Dispose();
        }
    }
}
