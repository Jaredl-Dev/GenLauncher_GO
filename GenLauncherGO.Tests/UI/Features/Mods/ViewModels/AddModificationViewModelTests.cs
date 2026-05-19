using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.UI.Features.Mods.ViewModels;

namespace GenLauncherGO.Tests.UI.Features.Mods.ViewModels;

public sealed class AddModificationViewModelTests
{
    /// <summary>
    ///     The placeholder an item shows until its remote version resolves.
    /// </summary>
    private const string PendingVersionText = "…";

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
    public void SearchText_NoMatches_ClearsSelectionAndDisablesAdd()
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
    public void AcceptCommand_WithSelection_RequestsAcceptedClose()
    {
        using AddModificationViewModel viewModel = CreateViewModel("Contra", "ShockWave");
        viewModel.SelectedModification = viewModel.VisibleModifications[0];
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.AcceptCommand.Execute(null);

        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().BeTrue();
        viewModel.SelectedModificationName.Should().Be("Contra");
    }

    [Fact]
    public void CancelCommand_RequestsCanceledClose()
    {
        using AddModificationViewModel viewModel = CreateViewModel("Contra");
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.CancelCommand.Execute(null);

        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().BeFalse();
    }

    [Fact]
    public async Task LoadMetadataAsync_ShowsVersionAndFormattedPackageSizeAsync()
    {
        LauncherContentVersion remoteVersion = TestLauncherContent.Version(
            "Rise of the Reds",
            "1.87",
            simpleDownloadLink: "https://example.test/rotr.zip");
        FakeLauncherContentCatalog catalog = new()
        {
            MetadataHandler = (_, _) => Task.FromResult(remoteVersion)
        };
        IRemotePackageSizeResolver sizeResolver = Substitute.For<IRemotePackageSizeResolver>();
        sizeResolver.GetTotalBytesAsync(remoteVersion, Arg.Any<CancellationToken>())
            .Returns(6_012_954_214);
        using AddModificationViewModel viewModel = CreateViewModel(catalog, sizeResolver, remoteVersion.Name);

        await viewModel.LoadMetadataAsync();

        AddModificationItemViewModel item = viewModel.VisibleModifications.Single();
        item.VersionText.Should().Be("1.87");
        item.PackageSizeText.Should().Be("5.6 GB");
    }

    [Fact]
    public async Task LoadMetadataAsync_SizeResolverContractViolation_PropagatesAsync()
    {
        LauncherContentVersion remoteVersion = TestLauncherContent.Version(
            "Contra",
            "009",
            simpleDownloadLink: "https://example.test/contra.zip");
        FakeLauncherContentCatalog catalog = new()
        {
            MetadataHandler = (_, _) => Task.FromResult(remoteVersion)
        };
        IRemotePackageSizeResolver sizeResolver = Substitute.For<IRemotePackageSizeResolver>();
        sizeResolver.GetTotalBytesAsync(remoteVersion, Arg.Any<CancellationToken>())
            .Returns<Task<long?>>(_ => throw new IOException("Offline"));
        using AddModificationViewModel viewModel = CreateViewModel(catalog, sizeResolver, remoteVersion.Name);

        Func<Task> loadMetadata = viewModel.LoadMetadataAsync;

        await loadMetadata.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task LoadMetadataAsync_UnknownPackageSize_ShowsUnavailableAsync()
    {
        LauncherContentVersion remoteVersion = TestLauncherContent.Version(
            "Contra",
            "009",
            simpleDownloadLink: "https://example.test/contra.zip");
        FakeLauncherContentCatalog catalog = new()
        {
            MetadataHandler = (_, _) => Task.FromResult(remoteVersion)
        };
        IRemotePackageSizeResolver sizeResolver = Substitute.For<IRemotePackageSizeResolver>();
        sizeResolver.GetTotalBytesAsync(remoteVersion, Arg.Any<CancellationToken>())
            .Returns((long?)null);
        using AddModificationViewModel viewModel = CreateViewModel(catalog, sizeResolver, remoteVersion.Name);

        await viewModel.LoadMetadataAsync();

        AddModificationItemViewModel item = viewModel.VisibleModifications.Single();
        item.VersionText.Should().Be("009");
        item.PackageSizeText.Should().Be("Unavailable");
    }

    [Fact]
    public async Task LoadMetadataAsync_CatalogMetadataFails_LeavesTheItemUnavailableAsync()
    {
        FakeLauncherContentCatalog catalog = new()
        {
            MetadataHandler = (_, _) =>
                Task.FromException<LauncherContentVersion>(new IOException("Offline"))
        };
        IRemotePackageSizeResolver sizeResolver = Substitute.For<IRemotePackageSizeResolver>();
        using AddModificationViewModel viewModel = CreateViewModel(catalog, sizeResolver, "Contra");

        await viewModel.LoadMetadataAsync();

        AddModificationItemViewModel item = viewModel.VisibleModifications.Single();
        item.VersionText.Should().Be("—");
        item.PackageSizeText.Should().Be("Unavailable");
        await sizeResolver.DidNotReceive().GetTotalBytesAsync(
            Arg.Any<LauncherContentVersion>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadMetadataAsync_DoesNotHoldVersionSlotsWhilePackageSizesArePendingAsync()
    {
        // One more name than the view model's version-request concurrency limit, so the last item can only
        // resolve if a completed version request released its slot instead of waiting for the package size.
        string[] names = Enumerable.Range(1, 7)
            .Select(index => $"Modification {index}")
            .ToArray();
        TaskCompletionSource releasePackageSizes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeLauncherContentCatalog catalog = new()
        {
            MetadataHandler = (name, _) => Task.FromResult(TestLauncherContent.Version(
                name,
                simpleDownloadLink: $"https://example.test/{name}.zip"))
        };
        IRemotePackageSizeResolver sizeResolver = Substitute.For<IRemotePackageSizeResolver>();
        sizeResolver.GetTotalBytesAsync(Arg.Any<LauncherContentVersion>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await releasePackageSizes.Task;
                return (long?)1024;
            });
        using AddModificationViewModel viewModel = CreateViewModel(catalog, sizeResolver, names);
        TaskCompletionSource allVersionsResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (AddModificationItemViewModel item in viewModel.VisibleModifications)
        {
            item.PropertyChanged += (_, arguments) =>
            {
                if (string.Equals(
                        arguments.PropertyName,
                        nameof(AddModificationItemViewModel.VersionText),
                        StringComparison.Ordinal) &&
                    viewModel.VisibleModifications.All(candidate =>
                        !string.Equals(candidate.VersionText, PendingVersionText, StringComparison.Ordinal)))
                {
                    allVersionsResolved.TrySetResult();
                }
            };
        }

        Task loadingTask = viewModel.LoadMetadataAsync();
        try
        {
            await allVersionsResolved.Task.WaitAsync(TestTimeouts.Wait);

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
            }
        };
        using AddModificationViewModel viewModel = CreateViewModel(
            catalog,
            Substitute.For<IRemotePackageSizeResolver>(),
            "Contra");

        Task loadingTask = viewModel.LoadMetadataAsync();
        await started.Task.WaitAsync(TestTimeouts.Wait);
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
            new FakeStringLocalizer(new Dictionary<string, string>
            {
                ["CalculatingPackageSize"] = "Calculating...",
                ["PackageSizeUnavailable"] = "Unavailable"
            }));
    }
}
