using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Shared.Errors;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Shared.Errors;

public sealed class AvaloniaUiExceptionBoundaryTests
{
    [Fact]
    public void ExecuteAsync_WhenOperationIsCanceled_ReturnsTypedCancellationWithoutShowingError()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            AvaloniaUiExceptionBoundary boundary = CreateBoundary(dialogService);

            UiOperationOutcome outcome = await boundary.ExecuteAsync(
                "canceling a test operation",
                () => Task.FromCanceled(new CancellationToken(canceled: true)));

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
            AvaloniaUiExceptionBoundary boundary = CreateBoundary(dialogService);
            InvalidOperationException exception = new("unexpected");

            UiOperationOutcome outcome = await boundary.ExecuteAsync(
                "running a test operation",
                () => Task.FromException(exception));

            outcome.Should().Be(UiOperationOutcome.Failed);
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Something went wrong" &&
                    request.DetailMessage ==
                    $"Try again. If the problem continues, check the logs.{Environment.NewLine}" +
                    $"{Environment.NewLine}InvalidOperationException: unexpected"),
                Arg.Any<Window?>());
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
            new TestStringLocalizer(new Dictionary<string, string>
            {
                ["UnexpectedErrorTitle"] = "Something went wrong",
                ["UnexpectedErrorDetails"] = "Try again. If the problem continues, check the logs.",
            }),
            NullLogger<AvaloniaUiExceptionBoundary>.Instance);
    }
}
