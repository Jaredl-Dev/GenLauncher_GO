using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Mods.ViewModels;

namespace GenLauncherGO.Tests.UI.Features.Mods.ViewModels;

public sealed class AddModificationViewModelTests
{
    [Fact]
    public void SearchText_FiltersNamesCaseInsensitivelyWithoutReorderingSource()
    {
        string[] sourceNames = ["Rise of the Reds", "Contra", "ShockWave"];
        using AddModificationViewModel viewModel = CreateViewModel(sourceNames);

        viewModel.SearchText = "CONtr";

        viewModel.VisibleModifications.Select(item => item.Name).Should().Equal("Contra");
        sourceNames.Should().Equal("Rise of the Reds", "Contra", "ShockWave");
    }

    [Fact]
    public void SearchText_PreservesVisibleSelectionAndSelectsFirstWhenHidden()
    {
        using AddModificationViewModel viewModel = CreateViewModel(
            "Rise of the Reds",
            "Contra",
            "ShockWave");
        AddModificationItemViewModel shockWave = viewModel.VisibleModifications[2];
        viewModel.SelectedModification = shockWave;

        viewModel.SearchText = "shock";

        viewModel.SelectedModification.Should().BeSameAs(shockWave);

        viewModel.SearchText = "rise";

        viewModel.SelectedModificationName.Should().Be("Rise of the Reds");
        viewModel.AcceptCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SearchText_WithNoMatchesClearsSelectionAndDisablesAdd()
    {
        using AddModificationViewModel viewModel = CreateViewModel("Contra", "ShockWave");

        viewModel.SearchText = "missing";

        viewModel.VisibleModifications.Should().BeEmpty();
        viewModel.HasNoVisibleModifications.Should().BeTrue();
        viewModel.SelectedModification.Should().BeNull();
        viewModel.CanAccept.Should().BeFalse();
        viewModel.AcceptCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LoadMetadataAsync_ShowsVersionAndFormattedPackageSizeAsync()
    {
        LauncherContentVersion remoteVersion = new()
        {
            ModificationType = ModificationType.Mod,
            Name = "Rise of the Reds",
            Version = "1.87",
            SimpleDownloadLink = "https://example.test/rotr.zip",
        };
        FakeLauncherContentCatalog catalog = new()
        {
            MetadataHandler = (_, _) => Task.FromResult(remoteVersion),
        };
        IRemotePackageSizeResolver sizeResolver = Substitute.For<IRemotePackageSizeResolver>();
        sizeResolver.GetTotalBytesAsync(remoteVersion, Arg.Any<CancellationToken>())
            .Returns((long?)6_012_954_214);
        using AddModificationViewModel viewModel = CreateViewModel(catalog, sizeResolver, remoteVersion.Name);

        await viewModel.LoadMetadataAsync();

        AddModificationItemViewModel item = viewModel.VisibleModifications.Single();
        item.VersionText.Should().Be("1.87");
        item.PackageSizeText.Should().Be("5.6 GB");
    }

    [Fact]
    public async Task LoadMetadataAsync_MapsSizeFailureToUnavailableStateAsync()
    {
        LauncherContentVersion remoteVersion = new()
        {
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "009",
            SimpleDownloadLink = "https://example.test/contra.zip",
        };
        FakeLauncherContentCatalog catalog = new()
        {
            MetadataHandler = (_, _) => Task.FromResult(remoteVersion),
        };
        IRemotePackageSizeResolver sizeResolver = Substitute.For<IRemotePackageSizeResolver>();
        sizeResolver.GetTotalBytesAsync(remoteVersion, Arg.Any<CancellationToken>())
            .Returns<Task<long?>>(_ => throw new IOException("Offline"));
        using AddModificationViewModel viewModel = CreateViewModel(catalog, sizeResolver, remoteVersion.Name);

        await viewModel.LoadMetadataAsync();

        AddModificationItemViewModel item = viewModel.VisibleModifications.Single();
        item.VersionText.Should().Be("009");
        item.PackageSizeText.Should().Be("Unavailable");
    }

    [Fact]
    public async Task LoadMetadataAsync_DoesNotHoldVersionSlotsWhilePackageSizesArePendingAsync()
    {
        string[] names = Enumerable.Range(1, 7)
            .Select(index => $"Modification {index}")
            .ToArray();
        int metadataRequestCount = 0;
        TaskCompletionSource allVersionsRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releasePackageSizes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeLauncherContentCatalog catalog = new()
        {
            MetadataHandler = (name, _) =>
            {
                if (Interlocked.Increment(ref metadataRequestCount) == names.Length)
                {
                    allVersionsRequested.SetResult();
                }

                return Task.FromResult(new LauncherContentVersion
                {
                    ModificationType = ModificationType.Mod,
                    Name = name,
                    Version = "1.0",
                    SimpleDownloadLink = $"https://example.test/{name}.zip",
                });
            },
        };
        IRemotePackageSizeResolver sizeResolver = Substitute.For<IRemotePackageSizeResolver>();
        sizeResolver.GetTotalBytesAsync(Arg.Any<LauncherContentVersion>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await releasePackageSizes.Task;
                return (long?)1024;
            });
        using AddModificationViewModel viewModel = CreateViewModel(catalog, sizeResolver, names);

        Task loadingTask = viewModel.LoadMetadataAsync();
        try
        {
            await allVersionsRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

            viewModel.VisibleModifications.Should().OnlyContain(item => item.VersionText == "1.0");
        }
        finally
        {
            releasePackageSizes.TrySetResult();
            await loadingTask;
        }
    }

    [Fact]
    public async Task CancelMetadataLoading_CancelsPendingCatalogWorkAsync()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool observedCancellation = false;
        FakeLauncherContentCatalog catalog = new()
        {
            MetadataHandler = async (_, cancellationToken) =>
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("The cancellation delay unexpectedly completed.");
                }
                finally
                {
                    observedCancellation = cancellationToken.IsCancellationRequested;
                }
            },
        };
        using AddModificationViewModel viewModel = CreateViewModel(
            catalog,
            Substitute.For<IRemotePackageSizeResolver>(),
            "Contra");

        Task loadingTask = viewModel.LoadMetadataAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.CancelMetadataLoading();
        await loadingTask;

        observedCancellation.Should().BeTrue();
    }

    private static AddModificationViewModel CreateViewModel(params string[] names)
    {
        return CreateViewModel(
            new FakeLauncherContentCatalog(),
            Substitute.For<IRemotePackageSizeResolver>(),
            names);
    }

    private static AddModificationViewModel CreateViewModel(
        FakeLauncherContentCatalog catalog,
        IRemotePackageSizeResolver sizeResolver,
        params string[] names)
    {
        return new AddModificationViewModel(
            names,
            catalog,
            sizeResolver,
            new TestStringLocalizer(new Dictionary<string, string>
            {
                ["CalculatingPackageSize"] = "Calculating...",
                ["PackageSizeUnavailable"] = "Unavailable",
            }));
    }
}
