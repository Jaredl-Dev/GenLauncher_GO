using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Infrastructure.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Startup;

public sealed class WindowsLauncherHostEnvironmentServiceTests
{
    [Fact]
    public void GetExecutableDirectoryReturnsExistingDirectory()
    {
        var service = new WindowsLauncherHostEnvironmentService();

        string directory = service.GetExecutableDirectory();

        directory.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(directory).Should().BeTrue();
    }

    [Fact]
    public void TryAcquireSingleInstanceReturnsAcquiredGuardForUnusedName()
    {
        var service = new WindowsLauncherHostEnvironmentService();
        string instanceName = CreateInstanceName();

        using ILauncherSingleInstanceGuard guard = service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero);

        guard.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireSingleInstanceReturnsAcquiredGuardWhenNameIsReleasedBeforeRetryAsync()
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
                using Mutex owner = new(initiallyOwned: true, instanceName, out _);
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
            IsBackground = true,
        };

        ownerThread.Start();
        try
        {
            mutexAcquired.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            Task<ILauncherSingleInstanceGuard> acquisition = Task.Run(() =>
                service.TryAcquireSingleInstance(instanceName, TimeSpan.FromMilliseconds(100)));
            retryStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            releaseMutex.Set();
            ownerThread.Join();
            allowRetry.Set();
            using ILauncherSingleInstanceGuard guard =
                await acquisition.WaitAsync(TimeSpan.FromSeconds(5));

            ownerException.Should().BeNull();
            guard.IsAcquired.Should().BeTrue();
        }
        finally
        {
            releaseMutex.Set();
            allowRetry.Set();
            ownerThread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void TryAcquireSingleInstanceReturnsRejectedGuardWhenNameIsAlreadyOwned()
    {
        var service = new WindowsLauncherHostEnvironmentService();
        string instanceName = CreateInstanceName();
        using ILauncherSingleInstanceGuard firstGuard = service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero);

        using ILauncherSingleInstanceGuard secondGuard = service.TryAcquireSingleInstance(instanceName, TimeSpan.Zero);

        firstGuard.IsAcquired.Should().BeTrue();
        secondGuard.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void IsProtectedProgramFilesDirectoryReturnsFalseForTemporaryDirectory()
    {
        var service = new WindowsLauncherHostEnvironmentService();

        bool result = service.IsProtectedProgramFilesDirectory(Path.GetTempPath());

        result.Should().BeFalse();
    }

    private static string CreateInstanceName()
    {
        return "GenLauncherGO.Tests." + Guid.NewGuid().ToString("N");
    }
}
