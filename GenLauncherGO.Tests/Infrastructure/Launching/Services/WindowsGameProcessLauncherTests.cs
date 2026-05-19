using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class WindowsGameProcessLauncherTests
{
    [Fact]
    public void GameLaunchRequest_RejectsExternalOrNestedExecutablePaths()
    {
        using var directory = new TestDirectory();

        Action rooted = () => GameLaunchRequest.ForGameClient(
            directory.Path,
            Path.Combine(directory.Path, "external.exe"),
            string.Empty);
        Action nested = () => GameLaunchRequest.ForWorldBuilder(
            directory.Path,
            @"tools\custom.exe",
            string.Empty);
        Action traversal = () => GameLaunchRequest.ForGameClient(
            directory.Path,
            @"..\custom.exe",
            string.Empty);

        rooted.Should().Throw<ArgumentException>();
        nested.Should().Throw<ArgumentException>();
        traversal.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task StartAsync_UsesExplicitGameExecutableAndArgumentsAsync()
    {
        using var directory = new TestDirectory();
        string executableName = directory.CreateFile("custom.exe", string.Empty);
        var processLauncher = new RecordingProcessFamilyLauncher
        {
            RunningDuration = TimeSpan.FromSeconds(13)
        };
        WindowsGameProcessLauncher launcher = CreateLauncher(processLauncher);

        bool succeeded = await StartAndCompleteAsync(
            launcher,
            GameLaunchRequest.ForGameClient(directory.Path, "custom.exe", "-quickstart"),
            CancellationToken.None);

        succeeded.Should().BeTrue();
        processLauncher.Calls.Should().ContainSingle().Which.Should().Be(
            (executableName, "-quickstart", directory.Path));
    }

    [Theory]
    [InlineData(11_999, false)]
    [InlineData(12_000, true)]
    public async Task StartAsync_GameClientSuccessRequiresTwelveSecondDurationAsync(
        int runningDurationMilliseconds,
        bool expectedSuccess)
    {
        using var directory = new TestDirectory();
        directory.CreateFile("custom.exe", string.Empty);
        var processLauncher = new RecordingProcessFamilyLauncher
        {
            RunningDuration = TimeSpan.FromMilliseconds(runningDurationMilliseconds)
        };
        WindowsGameProcessLauncher launcher = CreateLauncher(processLauncher);

        bool succeeded = await StartAndCompleteAsync(
            launcher,
            GameLaunchRequest.ForGameClient(directory.Path, "custom.exe", string.Empty),
            CancellationToken.None);

        succeeded.Should().Be(expectedSuccess);
    }

    [Fact]
    public async Task StartAsync_UsesExplicitWorldBuilderExecutableAndArgumentsAsync()
    {
        using var directory = new TestDirectory();
        string executableName = directory.CreateFile("custom-wb.exe", string.Empty);
        var processLauncher = new RecordingProcessFamilyLauncher();
        WindowsGameProcessLauncher launcher = CreateLauncher(processLauncher);

        bool succeeded = await StartAndCompleteAsync(
            launcher,
            GameLaunchRequest.ForWorldBuilder(directory.Path, "custom-wb.exe", "-wb"),
            CancellationToken.None);

        succeeded.Should().BeTrue();
        processLauncher.Calls.Should().ContainSingle().Which.Should().Be(
            (executableName, "-wb", directory.Path));
    }

    [Fact]
    public async Task ForceClose_StopsTheLaunchedProcessFamilyAsync()
    {
        using var directory = new TestDirectory();
        directory.CreateFile("custom.exe", string.Empty);
        var processLauncher = new RecordingProcessFamilyLauncher { RunningDuration = null };
        WindowsGameProcessLauncher launcher = CreateLauncher(processLauncher);
        IGameProcessLaunchOperation operation = await launcher.StartAsync(
            GameLaunchRequest.ForGameClient(directory.Path, "custom.exe", string.Empty),
            CancellationToken.None);

        operation.ForceClose();

        processLauncher.StartedOperation!.ForceCloseCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_ForwardsCurrentExecutableNameChangesUntilTheGameExitsAsync()
    {
        using var directory = new TestDirectory();
        directory.CreateFile("custom.exe", string.Empty);
        var processLauncher = new RecordingProcessFamilyLauncher { RunningDuration = null };
        WindowsGameProcessLauncher launcher = CreateLauncher(processLauncher);
        List<string> observedExecutableNames = [];

        IGameProcessLaunchOperation operation = await launcher.StartAsync(
            GameLaunchRequest.ForGameClient(directory.Path, "custom.exe", string.Empty),
            CancellationToken.None);
        operation.CurrentExecutableNameChanged += (_, _) =>
            observedExecutableNames.Add(operation.CurrentExecutableName);
        processLauncher.StartedOperation!.RaiseCurrentExecutableNameChanged("game.dat");
        processLauncher.StartedOperation.Complete(TimeSpan.FromSeconds(13));
        await operation.Completion;
        processLauncher.StartedOperation.RaiseCurrentExecutableNameChanged("worldbuilder.exe");

        observedExecutableNames.Should().Equal("game.dat");
    }

    [Fact]
    public async Task StartAsync_PropagatesProcessFamilyFailureAsync()
    {
        using var directory = new TestDirectory();
        directory.CreateFile("custom.exe", string.Empty);
        var failure = new InvalidOperationException("the launched process could not be observed");
        var processLauncher = new RecordingProcessFamilyLauncher { Failure = failure };
        WindowsGameProcessLauncher launcher = CreateLauncher(processLauncher);

        IGameProcessLaunchOperation operation = await launcher.StartAsync(
            GameLaunchRequest.ForGameClient(directory.Path, "custom.exe", string.Empty),
            CancellationToken.None);
        Func<Task<bool>> complete = () => operation.Completion;

        (await complete.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task StartAsync_RejectsMissingExecutableAsync()
    {
        using var directory = new TestDirectory();
        WindowsGameProcessLauncher launcher = CreateLauncher(new RecordingProcessFamilyLauncher());
        var request = GameLaunchRequest.ForGameClient(
            directory.Path,
            "missing.exe",
            string.Empty);

        Func<Task> start = () => launcher.StartAsync(request, CancellationToken.None);

        await start.Should().ThrowAsync<FileNotFoundException>();
    }

    // Needs a file that is itself a reparse point, which only a symbolic link provides. A junction
    // is a directory, so File.Exists short-circuits and production never reaches the reparse check.
    [SymbolicLinkFact]
    public async Task StartAsync_RejectsExecutableSymbolicLinkAsync()
    {
        using var directory = new TestDirectory();
        string targetPath = directory.CreateFile("target.bin", string.Empty);
        SymbolicLinkTestSupport.CreateFileLink(
            Path.Combine(directory.Path, "custom.exe"),
            targetPath);
        WindowsGameProcessLauncher launcher = CreateLauncher(new RecordingProcessFamilyLauncher());
        var request = GameLaunchRequest.ForGameClient(
            directory.Path,
            "custom.exe",
            string.Empty);

        Func<Task> start = () => launcher.StartAsync(request, CancellationToken.None);

        await start.Should().ThrowAsync<IOException>()
            .WithMessage("*reparse point*");
    }

    private static WindowsGameProcessLauncher CreateLauncher(RecordingProcessFamilyLauncher processLauncher)
    {
        return new WindowsGameProcessLauncher(
            processLauncher,
            NullLogger<WindowsGameProcessLauncher>.Instance);
    }

    private static async Task<bool> StartAndCompleteAsync(
        WindowsGameProcessLauncher launcher,
        GameLaunchRequest request,
        CancellationToken cancellationToken)
    {
        IGameProcessLaunchOperation operation = await launcher.StartAsync(request, cancellationToken);
        return await operation.Completion;
    }

    private sealed class RecordingProcessFamilyLauncher : IProcessFamilyLauncher
    {
        /// <summary>
        ///     Completes the launched operation immediately with this duration. Null leaves the operation running so a
        ///     test can drive <see cref="StartedOperation" /> itself.
        /// </summary>
        public TimeSpan? RunningDuration { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        ///     Fails the launched operation instead of completing it.
        /// </summary>
        public Exception? Failure { get; set; }

        public ControllableProcessFamilyLaunchOperation? StartedOperation { get; private set; }

        public List<(string ExecutableName, string Arguments, string WorkingDirectory)> Calls { get; } = [];

        public Task<IProcessFamilyLaunchOperation> StartAsync(
            string executableName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            Calls.Add((executableName, arguments, workingDirectory));
            ControllableProcessFamilyLaunchOperation operation = new(executableName);
            StartedOperation = operation;
            if (Failure is not null)
            {
                operation.Completion = Task.FromException<TimeSpan>(Failure);
            }
            else if (RunningDuration is not null)
            {
                operation.Complete(RunningDuration.Value);
            }

            return Task.FromResult<IProcessFamilyLaunchOperation>(operation);
        }
    }
}
