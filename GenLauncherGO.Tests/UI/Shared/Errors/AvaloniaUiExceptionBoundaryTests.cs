using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Shared.Errors;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Shared.Errors;

[Collection("Avalonia")]
public sealed class AvaloniaUiExceptionBoundaryTests
{
    [Fact]
    public void ExecuteAsync_WhenCalledOffThread_RunsOperationOnDispatcherAndReturnsSuccess()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            AvaloniaUiExceptionBoundary boundary = CreateBoundary(dialogService);
            bool operationRanOnUiThread = false;

            UiOperationOutcome outcome = await Task.Run(() => boundary.ExecuteAsync(
                "running a successful test operation",
                () =>
                {
                    operationRanOnUiThread = Avalonia.Threading.Dispatcher.UIThread.CheckAccess();
                    return Task.CompletedTask;
                }));

            outcome.Should().Be(UiOperationOutcome.Succeeded);
            operationRanOnUiThread.Should().BeTrue();
            await dialogService.DidNotReceive().ShowErrorAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void ExecuteAsync_WhenOperationIsCanceled_ReturnsTypedCancellationWithoutShowingError()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            AvaloniaUiExceptionBoundary boundary = CreateBoundary(dialogService);

            UiOperationOutcome outcome = await boundary.ExecuteAsync(
                "canceling a test operation",
                () => Task.FromCanceled(new CancellationToken(true)));

            outcome.Should().Be(UiOperationOutcome.Canceled);
            await dialogService.DidNotReceive().ShowErrorAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void ExecuteAsync_WhenOperationFails_ShowsConsistentErrorAndReturnsTypedFailure()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            bool dialogRanOnUiThread = false;
            dialogService
                .When(service => service.ShowErrorAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<Window?>()))
                .Do(_ => dialogRanOnUiThread = Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
            AvaloniaUiExceptionBoundary boundary = CreateBoundary(dialogService);
            InvalidOperationException exception = new("unexpected");
            var owner = new Window();

            UiOperationOutcome outcome = await Task.Run(() => boundary.ExecuteAsync(
                "running a test operation",
                () => Task.FromException(exception),
                owner));

            outcome.Should().Be(UiOperationOutcome.Failed);
            // A dialog owned by a window can only be shown from the thread that owns it, so a failure surfacing
            // from a background thread has to be marshalled back before the error is presented.
            dialogRanOnUiThread.Should().BeTrue();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Something went wrong" &&
                    request.DetailMessage ==
                    $"Try again. If the problem continues, check the logs.{Environment.NewLine}" +
                    $"{Environment.NewLine}InvalidOperationException: unexpected"),
                owner);
        });
    }

    [Fact]
    public void HandleUnexpectedAsync_WhenErrorDialogAlsoFails_StillReturnsTypedFailure()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService
                .When(service => service.ShowErrorAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<Window?>()))
                .Do(_ => throw new InvalidOperationException("dialog failed"));
            AvaloniaUiExceptionBoundary boundary = CreateBoundary(dialogService);
            InvalidOperationException exception = new("unexpected");

            UiOperationOutcome outcome = await boundary.HandleUnexpectedAsync(
                exception,
                "handling a test event");

            outcome.Should().Be(UiOperationOutcome.Failed);
        });
    }

    private static AvaloniaUiExceptionBoundary CreateBoundary(
        ILauncherDialogService dialogService)
    {
        return new AvaloniaUiExceptionBoundary(
            dialogService,
            new FakeStringLocalizer(new Dictionary<string, string>
            {
                ["UnexpectedErrorTitle"] = "Something went wrong",
                ["UnexpectedErrorDetails"] = "Try again. If the problem continues, check the logs."
            }),
            NullLogger<AvaloniaUiExceptionBoundary>.Instance);
    }
}
