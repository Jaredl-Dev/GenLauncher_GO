using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using GenLauncherGO.Shared.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack.Locators;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Provides Windows process, elevation, single-instance, and foreground-window startup operations.
/// </summary>
internal sealed class WindowsLauncherHostEnvironmentService : ILauncherHostEnvironmentService
{
    private const int SwRestore = 9;

    private readonly ILogger<WindowsLauncherHostEnvironmentService> _logger;
    private readonly Func<string?> _packagedRootDirectoryResolver;
    private readonly Func<string?> _processPathResolver;
    private readonly Action<TimeSpan> _waitBeforeSingleInstanceRetry;

    public WindowsLauncherHostEnvironmentService()
        : this(NullLogger<WindowsLauncherHostEnvironmentService>.Instance)
    {
    }

    public WindowsLauncherHostEnvironmentService(ILogger<WindowsLauncherHostEnvironmentService> logger)
        : this(logger, Thread.Sleep, ResolveProcessPath, ResolvePackagedRootDirectory)
    {
    }

    internal WindowsLauncherHostEnvironmentService(
        ILogger<WindowsLauncherHostEnvironmentService> logger,
        Action<TimeSpan> waitBeforeSingleInstanceRetry,
        Func<string?>? processPathResolver = null,
        Func<string?>? packagedRootDirectoryResolver = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _waitBeforeSingleInstanceRetry = waitBeforeSingleInstanceRetry ??
                                         throw new ArgumentNullException(nameof(waitBeforeSingleInstanceRetry));
        _processPathResolver = processPathResolver ?? ResolveProcessPath;
        _packagedRootDirectoryResolver = packagedRootDirectoryResolver ?? ResolvePackagedRootDirectory;
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

    public string GetLauncherRootDirectory()
    {
        string? packagedRootDirectory = _packagedRootDirectoryResolver();
        if (!string.IsNullOrWhiteSpace(packagedRootDirectory))
        {
            return packagedRootDirectory;
        }

        string? executablePath = _processPathResolver();

        if (string.IsNullOrWhiteSpace(executablePath))
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
            string? executablePath = _processPathResolver();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                const string MissingExecutableMessage = "The launcher executable path could not be resolved.";
                _logger.LogError(MissingExecutableMessage);
                return LauncherRestartResult.Failure(MissingExecutableMessage);
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            if (process == null)
            {
                const string StartFailureMessage = "Windows did not start the replacement launcher process.";
                _logger.LogError(StartFailureMessage);
                return LauncherRestartResult.Failure(StartFailureMessage);
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

        Mutex mutex = new(true, instanceName, out bool createdNew);
        if (createdNew)
        {
            return new MutexSingleInstanceGuard(mutex, true);
        }

        mutex.Dispose();
        if (retryDelay > TimeSpan.Zero)
        {
            _waitBeforeSingleInstanceRetry(retryDelay);
        }

        mutex = new Mutex(true, instanceName, out createdNew);
        if (createdNew)
        {
            return new MutexSingleInstanceGuard(mutex, true);
        }

        mutex.Dispose();
        return MutexSingleInstanceGuard.NotAcquired;
    }

    private static bool IsPathInDirectoryWhenKnown(string path, string directory)
    {
        return !string.IsNullOrWhiteSpace(directory) &&
               LexicalPath.IsPathInDirectory(path, directory);
    }

    private static string? ResolveProcessPath()
    {
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
    }

    private static string? ResolvePackagedRootDirectory()
    {
        IVelopackLocator locator = VelopackLocator.Current;
        return locator.CurrentlyInstalledVersion == null ? null : locator.RootAppDir;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(HandleRef hWnd, int nCmdShow);

    private sealed class MutexSingleInstanceGuard : ILauncherSingleInstanceGuard
    {
        public static readonly MutexSingleInstanceGuard NotAcquired = new(null, false);

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
