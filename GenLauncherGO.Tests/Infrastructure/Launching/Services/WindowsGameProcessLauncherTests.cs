using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class WindowsGameProcessLauncherTests
{
    [Fact]
    public void GameLaunchRequestRejectsExternalOrNestedExecutablePaths()
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
    public async Task StartAsyncUsesExplicitGameExecutableAndArgumentsAsync()
    {
        using var directory = new TestDirectory();
        string executableName = directory.CreateFile("custom.exe", string.Empty);
        var processLauncher = new RecordingProcessFamilyLauncher
        {
            RunningDuration = TimeSpan.FromSeconds(13),
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

    [Fact]
    public async Task StartAsyncUsesExplicitWorldBuilderExecutableAndArgumentsAsync()
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
    public async Task StartAsyncRejectsMissingExecutableAsync()
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

    [SymbolicLinkFact]
    public async Task StartAsyncRejectsExecutableSymbolicLinkAsync()
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
        public TimeSpan RunningDuration { get; set; } = TimeSpan.FromSeconds(1);

        public List<(string ExecutableName, string Arguments, string WorkingDirectory)> Calls { get; } = new();

        public Task<IProcessFamilyLaunchOperation> StartAsync(
            string executableName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            Calls.Add((executableName, arguments, workingDirectory));
            return Task.FromResult<IProcessFamilyLaunchOperation>(
                new RecordingProcessFamilyLaunchOperation(executableName, RunningDuration));
        }
    }

    private sealed class RecordingProcessFamilyLaunchOperation : IProcessFamilyLaunchOperation
    {
        public RecordingProcessFamilyLaunchOperation(
            string executableName,
            TimeSpan runningDuration)
        {
            CurrentExecutableName = executableName;
            Completion = Task.FromResult(runningDuration);
        }

        public string CurrentExecutableName { get; }

        public event EventHandler? CurrentExecutableNameChanged
        {
            add { }
            remove { }
        }

        public Task<TimeSpan> Completion { get; }

        public void ForceClose()
        {
        }
    }
}
