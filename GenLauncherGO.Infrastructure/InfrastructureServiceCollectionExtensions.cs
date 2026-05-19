using System;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Remote;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Infrastructure.Archives;
using GenLauncherGO.Infrastructure.Archives.Contracts;
using GenLauncherGO.Infrastructure.Integrity.Contracts;
using GenLauncherGO.Infrastructure.Integrity.Services;
using GenLauncherGO.Infrastructure.Launching.Contracts;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using GenLauncherGO.Infrastructure.Mods.Contracts;
using GenLauncherGO.Infrastructure.Mods.Services;
using GenLauncherGO.Infrastructure.Persistence.Services;
using GenLauncherGO.Infrastructure.Remote;
using GenLauncherGO.Infrastructure.Remote.Contracts;
using GenLauncherGO.Infrastructure.Shell.Services;
using GenLauncherGO.Infrastructure.Updating.Clients;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GenLauncherGO.Infrastructure;

/// <summary>
/// Registers the launcher runtime's Infrastructure services after storage and logging bootstrap is complete.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddGenLauncherGoInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAtomicFileWriter, AtomicFileWriter>();
        services.AddSingleton<IArchiveExtractor, ArchiveExtractor>();
        services.AddSingleton<IResumableFileDownloader, ResumableHttpFileDownloader>();
        services.AddSingleton<IRemoteAssetDownloader, HttpRemoteAssetDownloader>();
        services.AddSingleton<IRemoteYamlDocumentReader, HttpRemoteYamlDocumentReader>();

        services.AddSingleton<IDownloadFileMetadataReader, HttpDownloadFileMetadataReader>();
        services.AddSingleton<IRemotePackageSizeResolver, RemotePackageSizeResolver>();
        services.AddSingleton<ISingleFilePackageUpdater, SingleFilePackageUpdater>();
        services.AddSingleton<IS3PackageUpdater, S3PackageUpdater>();
        services.AddSingleton<IPackageDownloadService, PackageDownloadService>();
        services.AddSingleton<IRemoteConnectionProbe, HttpRemoteConnectionProbe>();
        services.AddSingleton<IS3ObjectManifestReader, MinioS3ObjectManifestReader>();
        services.AddSingleton<IFileHashService, Md5FileHashService>();

        services.AddSingleton<IContentIntegrityService, FileSystemContentIntegrityService>();

        services.AddSingleton<IHardLinkCreator, WindowsHardLinkCreator>();
        services.AddSingleton<FileSystemDeploymentService>();
        services.AddSingleton<ILaunchPreparationService, DeploymentLaunchPreparationService>();
        services.AddSingleton<ILaunchContentIntegrityTargetBuilder, FileSystemLaunchContentIntegrityTargetBuilder>();
        services.AddSingleton<ILaunchContentIntegrityResolutionService,
            FileSystemLaunchContentIntegrityResolutionService>();
        services.AddSingleton<IProcessFamilyLauncher, WindowsProcessFamilyLauncher>();
        services.AddSingleton<IGameProcessLauncher, WindowsGameProcessLauncher>();
        services.AddSingleton<IGameExecutableDiscoveryService, WindowsGameExecutableDiscoveryService>();

        services.AddSingleton<ILauncherShellService, WindowsLauncherShellService>();

        services.AddSingleton<ILauncherContentStateStore, YamlLauncherContentStateStore>();
        services.AddSingleton<ILocalLauncherContentService, FileSystemLocalLauncherContentService>();
        services.AddSingleton<IManualModificationImporter, FileSystemManualModificationImporter>();
        services.AddSingleton<IModificationImageFileService, FileSystemModificationImageFileService>();
        services.AddSingleton<RemoteLauncherCatalogClient>();
        services.AddSingleton<LauncherCatalogImageCache>();
        services.AddSingleton<LauncherLocalContentReconciler>();
        services.AddSingleton<ILauncherContentCatalog, LauncherContentCatalogService>();

        return services;
    }
}
