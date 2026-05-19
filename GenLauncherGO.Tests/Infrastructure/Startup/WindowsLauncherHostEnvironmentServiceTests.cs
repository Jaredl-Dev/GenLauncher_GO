using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Infrastructure.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Startup;

public sealed class WindowsLauncherHostEnvironmentServiceTests
{
    /// <summary>
    ///     Every launcher-owned path is resolved against this directory, so it has to be the folder the running
    ///     executable lives in rather than any other directory the process happens to be able to name.
    /// </summary>
    [Fact]
    public void GetExecutableDirectory_ReturnsTheDirectoryHoldingTheRunningExecutable()
    {
        var service = new WindowsLauncherHostEnvironmentService();
        string runningExecutableDirectory = Path.GetDirectoryName(Environment.ProcessPath!)!;

        string directory = service.GetExecutableDirectory();

        directory.Should().Be(runningExecutableDirectory);
        Directory.Exists(directory).Should().BeTrue();
    }

    [Fact]
    public void TryAcquireSingleInstance_ReturnsAcquiredGuardForUnusedName()
    {
        var service = new WindowsLauncherHostEnvironmentService();
        string instanceName = CreateInstanceName();

        using ILauncherSingleInstanceGuard guard = service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero);

        guard.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireSingleInstance_ReturnsAcquiredGuardWhenNameIsReleasedBeforeRetryAsync()
    {
        string instanceName = CreateInstanceName();
        using ManualResetEventSlim mutexAcquired = new();
        using ManualResetEventSlim releaseMutex = new();
        using ManualResetEventSlim retryStarted = new();
        using ManualResetEventSlim allowRetry = new();
        var service = new WindowsLauncherHostEnvironmentService(
            NullLogger<WindowsLauncherHostEnvironmentService>.Instance,
            _ =>
            {
                retryStarted.Set();
                allowRetry.Wait();
            });
        Exception? ownerException = null;
        Thread ownerThread = new(() =>
        {
            try
            {
                using Mutex owner = new(true, instanceName, out _);
                mutexAcquired.Set();
                releaseMutex.Wait();
                owner.ReleaseMutex();
            }
            catch (Exception exception)
            {
                ownerException = exception;
                mutexAcquired.Set();
            }
        })
        {
            IsBackground = true
        };

        ownerThread.Start();
        try
        {
            mutexAcquired.Wait(TestTimeouts.Wait).Should().BeTrue();

            Task<ILauncherSingleInstanceGuard> acquisition = Task.Run(() =>
                service.TryAcquireSingleInstance(instanceName, TimeSpan.FromMilliseconds(100)));
            retryStarted.Wait(TestTimeouts.Wait).Should().BeTrue();

            releaseMutex.Set();
            ownerThread.Join();
            allowRetry.Set();
            using ILauncherSingleInstanceGuard guard =
                await acquisition.WaitAsync(TestTimeouts.Wait);

            ownerException.Should().BeNull();
            guard.IsAcquired.Should().BeTrue();
        }
        finally
        {
            releaseMutex.Set();
            allowRetry.Set();
            ownerThread.Join(TestTimeouts.Wait);
        }
    }

    [Fact]
    public void TryAcquireSingleInstance_ReturnsRejectedGuardWhenNameIsAlreadyOwned()
    {
        var service = new WindowsLauncherHostEnvironmentService();
        string instanceName = CreateInstanceName();
        using ILauncherSingleInstanceGuard firstGuard = service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero);

        using ILauncherSingleInstanceGuard secondGuard = service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero);

        firstGuard.IsAcquired.Should().BeTrue();
        secondGuard.IsAcquired.Should().BeFalse();
    }

    /// <summary>
    ///     A zero retry delay asks for an immediate second attempt. Waiting anyway would stall startup for a launcher
    ///     that explicitly opted out of the wait.
    /// </summary>
    [Fact]
    public void TryAcquireSingleInstance_ZeroRetryDelay_RetriesWithoutWaiting()
    {
        var requestedWaits = new List<TimeSpan>();
        var service = new WindowsLauncherHostEnvironmentService(
            NullLogger<WindowsLauncherHostEnvironmentService>.Instance,
            requestedWaits.Add);
        string instanceName = CreateInstanceName();
        using ILauncherSingleInstanceGuard firstGuard = service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero);

        using ILauncherSingleInstanceGuard secondGuard = service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero);

        secondGuard.IsAcquired.Should().BeFalse();
        requestedWaits.Should().BeEmpty();
    }

    /// <summary>
    ///     Neither the guard that owns the instance name nor a rejected attempt may keep it reserved after they are
    ///     released; otherwise closing the running launcher would still block the next one from starting.
    /// </summary>
    [Fact]
    public void TryAcquireSingleInstance_AfterHoldingAndRejectedGuardsAreReleased_AllowsANewGuard()
    {
        var service = new WindowsLauncherHostEnvironmentService();
        string instanceName = CreateInstanceName();
        using (service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero))
        {
            service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero).Dispose();
        }

        using ILauncherSingleInstanceGuard replacementGuard =
            service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero);

        replacementGuard.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void IsProtectedProgramFilesDirectory_ReturnsFalseForTemporaryDirectory()
    {
        var service = new WindowsLauncherHostEnvironmentService();

        bool result = service.IsProtectedProgramFilesDirectory(Path.GetTempPath());

        result.Should().BeFalse();
    }

    /// <summary>
    ///     Both Program Files roots are protected, so an installation under either one has to be recognized on its own.
    /// </summary>
    [Theory]
    [InlineData(Environment.SpecialFolder.ProgramFiles)]
    [InlineData(Environment.SpecialFolder.ProgramFilesX86)]
    public void IsProtectedProgramFilesDirectory_ProgramFilesRoot_ReturnsTrue(Environment.SpecialFolder programFiles)
    {
        var service = new WindowsLauncherHostEnvironmentService();
        string programFilesPath = Environment.GetFolderPath(programFiles);
        if (string.IsNullOrWhiteSpace(programFilesPath))
        {
            return;
        }

        bool rootIsProtected = service.IsProtectedProgramFilesDirectory(programFilesPath);
        bool installationIsProtected =
            service.IsProtectedProgramFilesDirectory(Path.Combine(programFilesPath, "Game"));

        rootIsProtected.Should().BeTrue();
        installationIsProtected.Should().BeTrue();
    }

    private static string CreateInstanceName()
    {
        return "GenLauncherGO.Tests." + Guid.NewGuid().ToString("N");
    }
}
