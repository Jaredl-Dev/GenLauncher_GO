using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.IO;
using Microsoft.Extensions.Logging;
using Minio.Exceptions;

namespace GenLauncherGO.Features.Updating;

/// <summary>
///     Downloads and installs modification packages using the managed package-source policy.
/// </summary>
internal sealed class PackageDownloadService : IPackageDownloadService
{
    private readonly ILogger<PackageDownloadService> _logger;
    private readonly ManagedPackageSourceResolver _packageSourceResolver;
    private readonly LauncherRuntimePathContext _runtimePathContext;
    private readonly IS3PackageUpdater _s3PackageUpdater;

    private readonly ISingleFilePackageUpdater _singleFilePackageUpdater;

    public PackageDownloadService(
        ISingleFilePackageUpdater singleFilePackageUpdater,
        IS3PackageUpdater s3PackageUpdater,
        ManagedPackageSourceResolver packageSourceResolver,
        LauncherRuntimePathContext runtimePathContext,
        ILogger<PackageDownloadService> logger)
    {
        _singleFilePackageUpdater = singleFilePackageUpdater ??
                                    throw new ArgumentNullException(nameof(singleFilePackageUpdater));
        _s3PackageUpdater = s3PackageUpdater ?? throw new ArgumentNullException(nameof(s3PackageUpdater));
        _packageSourceResolver = packageSourceResolver ??
                                 throw new ArgumentNullException(nameof(packageSourceResolver));
        _runtimePathContext = runtimePathContext ?? throw new ArgumentNullException(nameof(runtimePathContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PackageDownloadResult> DownloadAsync(
        LauncherContent modification,
        LauncherContentVersion version,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ArgumentNullException.ThrowIfNull(version);
        pauseController ??= new PackageDownloadPauseController();

        IProgress<PackageUpdateProgress>? monotonicProgress = progress is null
            ? null
            : new MonotonicPackageProgress(progress);

        try
        {
            await pauseController.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            LauncherPaths paths = _runtimePathContext.ActivePaths;
            ManagedPackageSourceResolver.PackageSource? source = await _packageSourceResolver.ResolveAsync(
                version,
                cancellationToken).ConfigureAwait(false);

            switch (source)
            {
                case ManagedPackageSourceResolver.PackageSource.S3 s3Source:
                    await DownloadS3PackageAsync(
                        modification,
                        version,
                        s3Source,
                        paths,
                        monotonicProgress,
                        pauseController,
                        cancellationToken).ConfigureAwait(false);
                    break;

                case ManagedPackageSourceResolver.PackageSource.SingleFile singleFileSource:
                    await DownloadSingleFilePackageAsync(
                        version,
                        singleFileSource.Metadata,
                        paths,
                        monotonicProgress,
                        pauseController,
                        cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Content source '{version.EffectiveContentSourceKind}' is not managed by the package downloader.");
            }

            _logger.LogInformation(
                "Completed package download for {ContentName} {ContentVersion}.",
                version.Name,
                version.Version);
            return PackageDownloadResult.Succeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Canceled package download for {ContentName} {ContentVersion}.",
                version.Name,
                version.Version);
            return PackageDownloadResult.Canceled();
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                exception,
                "Package provider surfaced a failure after cancellation for {ContentName} {ContentVersion}; treating the pre-commit operation as canceled.",
                version.Name,
                version.Version);
            return PackageDownloadResult.Canceled();
        }
        catch (UnexpectedMinioException exception)
        {
            return CreateRecoverableProviderFailure(version, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException)
        {
            return CreateRecoverableProviderFailure(version, exception);
        }
        catch (InvalidDataException exception)
        {
            _logger.LogWarning(
                exception,
                "Package validation failed for {ContentName} {ContentVersion}.",
                version.Name,
                version.Version);
            return PackageDownloadResult.RecoverableFailure(
                "The downloaded package could not be validated.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Package staging or installation failed for {ContentName} {ContentVersion}.",
                version.Name,
                version.Version);
            return PackageDownloadResult.RecoverableFailure(
                "The package could not be staged or installed in launcher storage.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Package download failed unexpectedly for {ContentName} {ContentVersion}.",
                version.Name,
                version.Version);
            return PackageDownloadResult.UnexpectedFailure(
                "An unexpected package download error occurred.");
        }
    }

    private async Task DownloadSingleFilePackageAsync(
        LauncherContentVersion version,
        DownloadFileMetadata metadata,
        LauncherPaths paths,
        IProgress<PackageUpdateProgress>? progress,
        PackageDownloadPauseController pauseController,
        CancellationToken cancellationToken)
    {
        PackageUpdatePathSet packagePaths = CreatePackagePaths(paths, version);
        OwnedDirectoryTree.EnsureExists(
            packagePaths.TemporaryPath.OwnerRoot,
            packagePaths.TemporaryPath.FullPath);

        _logger.LogInformation(
            "Starting single-file package download for {ContentName} {ContentVersion}.",
            version.Name,
            version.Version);

        await _singleFilePackageUpdater.UpdateAsync(
            metadata,
            packagePaths,
            progress,
            cancellationToken,
            pauseController).ConfigureAwait(false);
    }

    private async Task DownloadS3PackageAsync(
        LauncherContent modification,
        LauncherContentVersion version,
        ManagedPackageSourceResolver.PackageSource.S3 source,
        LauncherPaths paths,
        IProgress<PackageUpdateProgress>? progress,
        PackageDownloadPauseController pauseController,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting S3 package download for {ContentName} {ContentVersion}.",
            version.Name,
            version.Version);

        await pauseController.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
        LauncherContentVersion? latestInstalledVersion = modification.LatestInstalledVersion;
        PackageUpdatePathSet packagePaths = CreatePackagePaths(
            paths,
            version,
            latestInstalledVersion);

        await _s3PackageUpdater.UpdateAsync(
            new S3PackageUpdateRequest(
                source.Files,
                source.Request,
                packagePaths,
                S3HashValidationPolicy.InstallHashCheckedExtensions),
            progress,
            cancellationToken,
            pauseController).ConfigureAwait(false);
    }

    private static PackageUpdatePathSet CreatePackagePaths(
        LauncherPaths paths,
        LauncherContentVersion version,
        LauncherContentVersion? latestInstalledVersion = null)
    {
        OwnedContentPath installedPath = ResolveVersionPath(paths, version);
        OwnedContentPath? latestInstalledPath = latestInstalledVersion is null
            ? null
            : ResolveVersionPath(paths, latestInstalledVersion);
        return PackageUpdatePathSet.Create(
            paths,
            installedPath,
            latestInstalledPath);
    }

    private static OwnedContentPath ResolveVersionPath(
        LauncherPaths paths,
        LauncherContentVersion version)
    {
        return LauncherContentPathResolver.ResolveVersionPath(
                   paths,
                   version.ContentKey)
               ?? throw new InvalidOperationException(
                   "The package version did not resolve to a supported launcher content path.");
    }

    private PackageDownloadResult CreateRecoverableProviderFailure(
        LauncherContentVersion version,
        Exception exception)
    {
        _logger.LogWarning(
            exception,
            "Remote package provider failed for {ContentName} {ContentVersion}.",
            version.Name,
            version.Version);
        return PackageDownloadResult.RecoverableFailure(
            "The remote package provider could not complete the download.");
    }
}
