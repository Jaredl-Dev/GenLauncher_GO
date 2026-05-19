using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;
using Microsoft.Extensions.Logging;
using Minio.Exceptions;

namespace GenLauncherGO.Infrastructure.Updating.Services;

/// <summary>
///     Downloads and installs modification packages while keeping provider selection inside Infrastructure.
/// </summary>
internal sealed class PackageDownloadService : IPackageDownloadService
{
    private static readonly HashSet<string> _extensionsToCheckHash =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".w3d",
            LauncherContentFileTypes.BigExtension,
            ".bik",
            LauncherContentFileTypes.GibExtension,
            ".dds",
            ".tga",
            ".ini",
            ".scb",
            ".wnd",
            ".csf",
            ".str"
        };

    private readonly ILogger<PackageDownloadService> _logger;
    private readonly LauncherRuntimePathContext _runtimePathContext;
    private readonly IS3ObjectManifestReader _s3ObjectManifestReader;
    private readonly IS3PackageUpdater _s3PackageUpdater;

    private readonly ISingleFilePackageUpdater _singleFilePackageUpdater;

    public PackageDownloadService(
        ISingleFilePackageUpdater singleFilePackageUpdater,
        IS3PackageUpdater s3PackageUpdater,
        IS3ObjectManifestReader s3ObjectManifestReader,
        LauncherRuntimePathContext runtimePathContext,
        ILogger<PackageDownloadService> logger)
    {
        _singleFilePackageUpdater = singleFilePackageUpdater ??
                                    throw new ArgumentNullException(nameof(singleFilePackageUpdater));
        _s3PackageUpdater = s3PackageUpdater ?? throw new ArgumentNullException(nameof(s3PackageUpdater));
        _s3ObjectManifestReader = s3ObjectManifestReader ??
                                  throw new ArgumentNullException(nameof(s3ObjectManifestReader));
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

            if (version.EffectiveContentSourceKind == ContentSourceKind.ManagedS3)
            {
                await DownloadS3PackageAsync(
                    modification,
                    version,
                    paths,
                    monotonicProgress,
                    pauseController,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await DownloadSingleFilePackageAsync(
                    version,
                    paths,
                    monotonicProgress,
                    pauseController,
                    cancellationToken).ConfigureAwait(false);
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
            _logger.LogInformation(
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
        LauncherPaths paths,
        IProgress<PackageUpdateProgress>? progress,
        PackageDownloadPauseController pauseController,
        CancellationToken cancellationToken)
    {
        Uri downloadUri = DownloadLinkResolver.ResolveDownloadUri(version.SimpleDownloadLink);
        PackageUpdatePathSet packagePaths = CreatePackagePaths(paths, version);
        OwnedDirectoryTree.EnsureExists(
            packagePaths.TemporaryPath.OwnerRoot,
            packagePaths.TemporaryPath.FullPath);

        _logger.LogInformation(
            "Starting single-file package download for {ContentName} {ContentVersion}.",
            version.Name,
            version.Version);

        await _singleFilePackageUpdater.UpdateAsync(
            downloadUri,
            packagePaths,
            progress,
            cancellationToken,
            pauseController).ConfigureAwait(false);
    }

    private async Task DownloadS3PackageAsync(
        LauncherContent modification,
        LauncherContentVersion version,
        LauncherPaths paths,
        IProgress<PackageUpdateProgress>? progress,
        PackageDownloadPauseController pauseController,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting S3 package download for {ContentName} {ContentVersion}.",
            version.Name,
            version.Version);

        S3ObjectManifestRequest source = S3CatalogDefaults.CreateManifestRequest(version);
        IReadOnlyList<RemoteFileManifestEntry> repositoryFilesInfo =
            await _s3ObjectManifestReader.ReadManifestAsync(source, cancellationToken).ConfigureAwait(false);
        await pauseController.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
        LauncherContentVersion? latestInstalledVersion = modification.LatestInstalledVersion;
        PackageUpdatePathSet packagePaths = CreatePackagePaths(
            paths,
            version,
            latestInstalledVersion);

        await _s3PackageUpdater.UpdateAsync(
            new S3PackageUpdateRequest(
                repositoryFilesInfo,
                source,
                packagePaths,
                _extensionsToCheckHash),
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
