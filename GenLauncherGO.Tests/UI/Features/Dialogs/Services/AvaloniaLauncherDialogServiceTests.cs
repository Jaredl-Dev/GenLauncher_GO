using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Threading;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Dialogs.Services;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Integrity.ViewModels;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Features.Mods.Views;
using GenLauncherGO.UI.Shared.Dialogs;

namespace GenLauncherGO.Tests.UI.Features.Dialogs.Services;

public sealed class AvaloniaLauncherDialogServiceTests
{
    [Fact]
    public void DialogWithoutOwnerClosesWithInfoWindowResultAsync()
    {
        StaTestRunner.Run(async () =>
        {
            InfoWindow dialog = new(
                new LauncherInfoDialogRequest("Retry startup", "The startup operation failed."),
                InfoDialogKind.WarningConfirmation,
                TestLauncherTheme.Create(),
                continueText: "Retry",
                cancelText: "Cancel");
            Dispatcher.UIThread.Post(() => dialog.ViewModel.ContinueCommand.Execute(null));

            bool confirmed = await AvaloniaDialog.ShowAsync(
                dialog,
                owner: null,
                () => dialog.Accepted);

            confirmed.Should().BeTrue();
            dialog.IsVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void DialogWithoutOwnerClosesWithIntegrityReviewResultAsync()
    {
        StaTestRunner.Run(async () =>
        {
            IntegrityReviewViewModel viewModel = new(
                new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>()),
                new TestStringLocalizer());
            IntegrityReviewDialog dialog = new(viewModel);
            Dispatcher.UIThread.Post(() => viewModel.ConfirmResolutionCommand.Execute(null));

            bool confirmed = await AvaloniaDialog.ShowAsync(
                dialog,
                owner: null,
                () => dialog.ResolutionConfirmed);

            confirmed.Should().BeTrue();
            dialog.IsVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void ShowWarningConfirmationAsync_PreservesCustomTextAndDetailFontSize()
    {
        StaTestRunner.Run(async () =>
        {
            Window owner = new();
            owner.Show();
            Exception? callbackFailure = null;
            double observedFontSize = 0;
            string? observedContinueText = null;
            string? observedCancelText = null;
            bool observedWarningIcon = false;

            try
            {
                AvaloniaLauncherDialogService service = new(
                    TestLauncherRuntimeContext.Create(),
                    new TestStringLocalizer(new Dictionary<string, string>
                    {
                        ["Continue"] = "Continue",
                        ["Cancel"] = "Cancel",
                    }),
                    new FakeLauncherContentCatalog(),
                    Substitute.For<IRemotePackageSizeResolver>());
                LauncherInfoDialogRequest request = new(
                    "Unsafe operation",
                    "This operation changes managed files.",
                    detailFontSize: 12.5,
                    cancelText: "Go back");

                Dispatcher.UIThread.Post(() => CompleteDialog(attempt: 0));

                bool confirmed = await service.ShowWarningConfirmationAsync(
                    request,
                    continueText: "Proceed anyway",
                    owner);

                if (callbackFailure != null)
                {
                    ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                }

                confirmed.Should().BeTrue();
                observedFontSize.Should().Be(12.5);
                observedContinueText.Should().Be("Proceed anyway");
                observedCancelText.Should().Be("Go back");
                observedWarningIcon.Should().BeTrue();
            }
            finally
            {
                owner.Close();
            }

            void CompleteDialog(int attempt)
            {
                InfoWindow? dialog = owner.OwnedWindows.OfType<InfoWindow>().SingleOrDefault();
                if (dialog == null && attempt < 10)
                {
                    Dispatcher.UIThread.Post(() => CompleteDialog(attempt + 1));
                    return;
                }

                try
                {
                    dialog.Should().NotBeNull();
                    TextBlock detailMessage =
                        dialog!.FindControl<TextBlock>("DetailMessageText") ??
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
                }
                catch (Exception exception)
                {
                    callbackFailure = exception;
                    if (dialog != null)
                    {
                        dialog.Close(false);
                    }
                    else
                    {
                        owner.Close();
                    }
                }
            }
        });
    }
}
