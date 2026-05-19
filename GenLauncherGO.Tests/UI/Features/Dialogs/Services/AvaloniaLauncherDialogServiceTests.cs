using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Dialogs.Services;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Mods.Views;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.Tests.UI.Features.Dialogs.Services;

[Collection("Avalonia")]
public sealed class AvaloniaLauncherDialogServiceTests
{
    [Fact]
    public void ShowModificationSelectionAsync_AcceptedSelectionReturnsName()
    {
        StaTestRunner.Run(async () =>
        {
            AvaloniaLauncherDialogService service = CreateService();

            string? selectedName = await OwnedDialogTestHost.RunAsync<AddModificationWindow, string?>(
                dialog => dialog.ViewModel.AcceptCommand.Execute(null),
                owner => service.ShowModificationSelectionAsync(new[] { "Contra" }, owner));

            selectedName.Should().Be("Contra");
        });
    }

    [Fact]
    public void ShowModificationSelectionAsync_CanceledDialog_StopsPendingMetadataWork()
    {
        StaTestRunner.Run(async () =>
        {
            bool observedCancellation = false;
            FakeLauncherContentCatalog catalog = new()
            {
                MetadataHandler = async (_, cancellationToken) =>
                {
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
            AvaloniaLauncherDialogService service = CreateService(catalog: catalog);

            string? selectedName = await OwnedDialogTestHost.RunAsync<AddModificationWindow, string?>(
                dialog => dialog.ViewModel.CancelCommand.Execute(null),
                owner => service.ShowModificationSelectionAsync(new[] { "Contra" }, owner));

            selectedName.Should().BeNull();
            observedCancellation.Should().BeTrue();
        });
    }

    [Fact]
    public void ShowIntegrityReviewAsync_ConfirmedResolutionReturnsTrue()
    {
        StaTestRunner.Run(async () =>
        {
            AvaloniaLauncherDialogService service = CreateService();

            bool confirmed = await OwnedDialogTestHost.RunAsync<IntegrityReviewDialog, bool>(
                dialog => dialog.ViewModel.ConfirmResolutionCommand.Execute(null),
                owner => service.ShowIntegrityReviewAsync(
                    new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>()),
                    owner));

            confirmed.Should().BeTrue();
        });
    }

    [Fact]
    public void ShowIntegrityReviewAsync_CanceledReviewReturnsFalse()
    {
        StaTestRunner.Run(async () =>
        {
            AvaloniaLauncherDialogService service = CreateService();

            bool confirmed = await OwnedDialogTestHost.RunAsync<IntegrityReviewDialog, bool>(
                dialog => dialog.ViewModel.CancelCommand.Execute(null),
                owner => service.ShowIntegrityReviewAsync(
                    new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>()),
                    owner));

            confirmed.Should().BeFalse();
        });
    }

    [Fact]
    public void ShowManualModificationImportAsync_AcceptedDetailsReturnImportResult()
    {
        StaTestRunner.Run(async () =>
        {
            AvaloniaLauncherDialogService service = CreateService();

            ManualModificationDialogResult? result =
                await OwnedDialogTestHost.RunAsync<ManualAddModificationWindow, ManualModificationDialogResult?>(
                    dialog =>
                    {
                        dialog.ViewModel.ModificationName = "Patch Pack";
                        dialog.ViewModel.Version = "1.2";
                        dialog.ViewModel.AcceptCommand.Execute(null);
                    },
                    owner => service.ShowManualModificationImportAsync(
                        new[] { @"C:\Packages\patch.zip" },
                        owner));

            result.Should().NotBeNull();
            result!.ModificationName.Should().Be("Patch Pack");
            result.Version.Should().Be("1.2");
        });
    }

    [Fact]
    public void ShowManualModificationImportAsync_CanceledImportReturnsNull()
    {
        StaTestRunner.Run(async () =>
        {
            AvaloniaLauncherDialogService service = CreateService();

            ManualModificationDialogResult? result =
                await OwnedDialogTestHost.RunAsync<ManualAddModificationWindow, ManualModificationDialogResult?>(
                    dialog => dialog.ViewModel.CancelCommand.Execute(null),
                    owner => service.ShowManualModificationImportAsync(
                        new[] { @"C:\Packages\patch.zip" },
                        owner));

            result.Should().BeNull();
        });
    }

    [Fact]
    public void ShowWarningConfirmationAsync_PreservesCustomTextAndDetailFontSize()
    {
        StaTestRunner.Run(async () =>
        {
            double observedFontSize = 0;
            string? observedContinueText = null;
            string? observedCancelText = null;
            bool observedWarningIcon = false;
            AvaloniaLauncherDialogService service = CreateService(new FakeStringLocalizer(
                new Dictionary<string, string>
                {
                    ["Continue"] = "Continue",
                    ["Cancel"] = "Cancel"
                }));
            LauncherInfoDialogRequest request = new(
                "Unsafe operation",
                "This operation changes managed files.",
                12.5,
                "Go back");

            bool confirmed = await OwnedDialogTestHost.RunAsync<InfoWindow, bool>(
                dialog =>
                {
                    TextBlock detailMessage =
                        dialog.FindControl<TextBlock>("DetailMessageText") ??
                        throw new InvalidOperationException("The detail message control was not created.");
                    Button continueButton =
                        dialog.FindControl<Button>("ContinueButton") ??
                        throw new InvalidOperationException("The continue button was not created.");
                    Button cancelButton =
                        dialog.FindControl<Button>("CancelButton") ??
                        throw new InvalidOperationException("The cancel button was not created.");

                    observedFontSize = detailMessage.FontSize;
                    observedContinueText = continueButton.Content as string;
                    observedCancelText = cancelButton.Content as string;
                    observedWarningIcon = dialog.ViewModel.IsWarningIconVisible;
                    dialog.ViewModel.ContinueCommand.Execute(null);
                },
                owner => service.ShowWarningConfirmationAsync(request, "Proceed anyway", owner));

            confirmed.Should().BeTrue();
            observedFontSize.Should().Be(12.5);
            observedContinueText.Should().Be("Proceed anyway");
            observedCancelText.Should().Be("Go back");
            observedWarningIcon.Should().BeTrue();
        });
    }

    [Fact]
    public void ShowInfoActionAsync_ReturnsActionAndDisplaysActionText()
    {
        StaTestRunner.Run(async () =>
        {
            string? observedActionText = null;
            AvaloniaLauncherDialogService service = CreateService();

            bool actionChosen = await OwnedDialogTestHost.RunAsync<InfoWindow, bool>(
                dialog =>
                {
                    Button actionButton =
                        dialog.FindControl<Button>("InfoActionButton") ??
                        throw new InvalidOperationException("The info action button was not created.");
                    observedActionText = actionButton.Content as string;
                    dialog.ViewModel.ActionCommand.Execute(null);
                },
                owner => service.ShowInfoActionAsync(
                    new LauncherInfoDialogRequest("Recommendation", "Details"),
                    "Visit GenPatcher download page",
                    owner));

            actionChosen.Should().BeTrue();
            observedActionText.Should().Be("Visit GenPatcher download page");
        });
    }

    private static AvaloniaLauncherDialogService CreateService(
        ILauncherStringLocalizer? stringLocalizer = null,
        ILauncherContentCatalog? catalog = null)
    {
        return new AvaloniaLauncherDialogService(
            stringLocalizer ?? new FakeStringLocalizer(),
            catalog ?? new FakeLauncherContentCatalog(),
            Substitute.For<IRemotePackageSizeResolver>());
    }
}

/// <summary>
///     Shows an owner window, drives the one dialog it opens, and rethrows whatever the driver observed.
/// </summary>
/// <remarks>
///     A modal dialog only exists once the service has shown it, so the driver is posted back to the dispatcher
///     until the owned window appears. Failures inside the driver are captured rather than thrown on the
///     dispatcher, where nothing would observe them and the awaiting test would hang.
/// </remarks>
file sealed class OwnedDialogTestHost
{
    private const int MaxDialogAttempts = 10;

    private Exception? _callbackFailure;

    public static async Task<TResult> RunAsync<TDialog, TResult>(
        Action<TDialog> complete,
        Func<Window, Task<TResult>> showDialog)
        where TDialog : Window
    {
        OwnedDialogTestHost host = new();
        Window owner = new();
        owner.Show();

        try
        {
            Dispatcher.UIThread.Post(() => host.CompleteOwnedDialog(owner, complete));

            TResult result = await showDialog(owner);

            if (host._callbackFailure != null)
            {
                ExceptionDispatchInfo.Capture(host._callbackFailure).Throw();
            }

            return result;
        }
        finally
        {
            owner.Close();
        }
    }

    private void CompleteOwnedDialog<TDialog>(
        Window owner,
        Action<TDialog> complete,
        int attempt = 0)
        where TDialog : Window
    {
        TDialog? dialog = owner.OwnedWindows.OfType<TDialog>().SingleOrDefault();
        if (dialog == null)
        {
            if (attempt < MaxDialogAttempts)
            {
                Dispatcher.UIThread.Post(() => CompleteOwnedDialog(owner, complete, attempt + 1));
                return;
            }

            _callbackFailure = new InvalidOperationException(
                $"The expected {typeof(TDialog).Name} was not shown.");
            owner.Close();
            return;
        }

        try
        {
            complete(dialog);
        }
        catch (Exception exception)
        {
            _callbackFailure = exception;
            dialog.Close();
        }
    }
}
