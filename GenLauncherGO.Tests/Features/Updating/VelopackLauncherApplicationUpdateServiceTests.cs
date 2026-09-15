using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Persistence;
using GenLauncherGO.Tests.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Features.Updating;

public sealed class VelopackLauncherApplicationUpdateServiceTests
{
    [Fact]
    public async Task CheckAndDownloadUpdateAsyncWhenDisabled_DoesNotContactUpdateSourceAsync()
    {
        using TestDirectory directory = new();
        LauncherStoragePaths paths = new(directory.Path);
        int checkCount = 0;
        VelopackLauncherApplicationUpdateService service = CreateService(
            paths,
            new ManualTimeProvider(),
            false,
            _ =>
            {
                checkCount++;
                return Task.FromResult<string?>("1.2.0");
            });

        string? version = await service.CheckAndDownloadUpdateAsync(CancellationToken.None);

        version.Should().BeNull();
        checkCount.Should().Be(0);
        File.Exists(paths.ApplicationUpdateCheckTimestampFilePath).Should().BeFalse();
    }

    [Fact]
    public async Task CheckAndDownloadUpdateAsyncWhenDue_ReturnsVersionAndRecordsCompletedAttemptAsync()
    {
        using TestDirectory directory = new();
        LauncherStoragePaths paths = new(directory.Path);
        ManualTimeProvider clock = new();
        VelopackLauncherApplicationUpdateService service = CreateService(
            paths,
            clock,
            true,
            _ => Task.FromResult<string?>("1.2.0"));

        string? version = await service.CheckAndDownloadUpdateAsync(CancellationToken.None);

        version.Should().Be("1.2.0");
        File.ReadAllText(paths.ApplicationUpdateCheckTimestampFilePath).Should()
            .Be(clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CheckAndDownloadUpdateAsyncAfterFailedCorruptState_StaysSilentAndThrottlesRetryAsync()
    {
        using TestDirectory directory = new();
        LauncherStoragePaths paths = new(directory.Path);
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(paths.ApplicationUpdateCheckTimestampFilePath, "not a timestamp");
        ManualTimeProvider clock = new();
        int checkCount = 0;
        VelopackLauncherApplicationUpdateService service = CreateService(
            paths,
            clock,
            true,
            _ =>
            {
                checkCount++;
                throw new InvalidOperationException("offline");
            });

        string? firstVersion = await service.CheckAndDownloadUpdateAsync(CancellationToken.None);
        string? throttledVersion = await service.CheckAndDownloadUpdateAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(24));
        string? laterVersion = await service.CheckAndDownloadUpdateAsync(CancellationToken.None);

        firstVersion.Should().BeNull();
        throttledVersion.Should().BeNull();
        laterVersion.Should().BeNull();
        checkCount.Should().Be(2);
    }

    [Fact]
    public async Task CheckAndDownloadUpdateAsyncWithPendingUpdate_ReturnsItWithoutAnotherNetworkCheckAsync()
    {
        using TestDirectory directory = new();
        LauncherStoragePaths paths = new(directory.Path);
        ManualTimeProvider clock = new();
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(
            paths.ApplicationUpdateCheckTimestampFilePath,
            clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        int checkCount = 0;
        VelopackLauncherApplicationUpdateService service = CreateService(
            paths,
            clock,
            true,
            _ =>
            {
                checkCount++;
                return Task.FromResult<string?>(null);
            },
            () => "1.2.0");

        string? version = await service.CheckAndDownloadUpdateAsync(CancellationToken.None);

        version.Should().Be("1.2.0");
        checkCount.Should().Be(0);
    }

    private static VelopackLauncherApplicationUpdateService CreateService(
        LauncherStoragePaths paths,
        TimeProvider timeProvider,
        bool updatesEnabled,
        Func<CancellationToken, Task<string?>> checkAndDownloadUpdateAsync,
        Func<string?>? getPendingUpdateVersion = null)
    {
        return new VelopackLauncherApplicationUpdateService(
            paths,
            new AtomicFileWriter(),
            timeProvider,
            updatesEnabled,
            checkAndDownloadUpdateAsync,
            () => true,
            NullLogger<VelopackLauncherApplicationUpdateService>.Instance,
            getPendingUpdateVersion);
    }
}
