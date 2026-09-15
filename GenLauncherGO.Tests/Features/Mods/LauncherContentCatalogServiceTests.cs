using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.IO;
using GenLauncherGO.Shared.Remote;
using Microsoft.Extensions.DependencyInjection;

namespace GenLauncherGO.Tests.Features.Mods;

public sealed class LauncherContentCatalogServiceTests
{
    [Fact]
    public async Task InitDataAsync_LocalOnly_LoadsInstalledContentWithoutRemoteRequestsAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        LauncherContentVersion version = InstallVersion(paths);
        RecordingUnavailableYamlReader reader = new();
        using ServiceProvider provider = CreateProvider(paths, reader);
        ILauncherContentCatalog catalog = provider.GetRequiredService<ILauncherContentCatalog>();

        await catalog.InitDataAsync(null, paths,
            TestContext.Current.CancellationToken);

        catalog.Data.FindContent(version.ContentKey).Should().NotBeNull();
        catalog.Data.FindContent(version.ContentKey)!.Installed.Should().BeTrue();
        reader.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task InitDataAsync_FailedReinitialization_RestoresPreviousCatalogAndSelectionAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        LauncherContentVersion version = InstallVersion(paths);
        RecordingUnavailableYamlReader reader = new();
        using ServiceProvider provider = CreateProvider(paths, reader);
        ILauncherContentCatalog catalog = provider.GetRequiredService<ILauncherContentCatalog>();
        await catalog.InitDataAsync(null, paths,
            TestContext.Current.CancellationToken);
        LauncherContent selected = catalog.Data.FindContent(version.ContentKey)!;
        selected.IsSelected = true;
        Uri manifestUri = new("https://example.test/catalog.yaml");

        Func<Task> act = () => catalog.InitDataAsync(
            manifestUri, paths,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IOException>();
        catalog.Data.GetSelectedMod().Should().BeSameAs(selected);
        catalog.Data.FindContent(version.ContentKey).Should().BeSameAs(selected);
        reader.Requests.Should().Equal(manifestUri);
    }

    private static LauncherContentVersion InstallVersion(LauncherPaths paths)
    {
        LauncherContentVersion version = TestLauncherContent.Version(installed: true);
        OwnedContentPath ownedPath = LauncherContentPathResolver.ResolveVersionPath(paths, version.ContentKey)!;
        Directory.CreateDirectory(ownedPath.FullPath);
        File.WriteAllText(Path.Combine(ownedPath.FullPath, "content.big"), "installed content");
        return version;
    }

    private static ServiceProvider CreateProvider(LauncherPaths paths, IRemoteYamlDocumentReader reader)
    {
        LauncherRuntimeContext runtime = new(TestLauncherPaths.CreateRuntimePathContext(paths), "1.0.0-test");
        ServiceCollection services = LauncherApplicationHost.CreateServiceCollection(
            runtime,
            Substitute.For<ILauncherPathResolver>(),
            Substitute.For<ILauncherHostEnvironmentService>(),
            new FakeStringLocalizer());
        services.AddSingleton(reader);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingUnavailableYamlReader : IRemoteYamlDocumentReader
    {
        public List<Uri> Requests { get; } = [];

        public Task<T> ReadYamlAsync<T>(Uri documentUri, CancellationToken cancellationToken)
        {
            Requests.Add(documentUri);
            return Task.FromException<T>(new IOException("The remote catalog is unavailable."));
        }
    }
}
