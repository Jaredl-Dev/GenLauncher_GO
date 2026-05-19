using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using GenLauncherGO.Infrastructure.Shell.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Shell.Services;

public sealed class WindowsLauncherShellServiceTests
{
    private readonly List<string> _openedTargets = [];

    [Fact]
    public void OpenUri_DoesNotLaunchEmptyUri()
    {
        WindowsLauncherShellService service = CreateService();

        service.OpenUri(" ");

        _openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenUri_DoesNotLaunchRelativeUri()
    {
        WindowsLauncherShellService service = CreateService();

        service.OpenUri("not-a-uri");

        _openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenUri_DoesNotLaunchUnsupportedScheme()
    {
        WindowsLauncherShellService service = CreateService();

        service.OpenUri("ftp://example.test/file.big");

        _openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenUri_OpensNormalizedHttpTarget()
    {
        WindowsLauncherShellService service = CreateService();

        service.OpenUri("HTTPS://Example.Test/mods?id=1");

        _openedTargets.Should().Equal("https://example.test/mods?id=1");
    }

    /// <summary>
    ///     A shell that refuses the target must not take the launcher down with it. The refusal is swallowed, so the
    ///     report to the caller-supplied logger is the only trace left for diagnosing "the link does nothing".
    /// </summary>
    [Fact]
    public void OpenUri_WhenTheShellRefusesTheTarget_ReportsTheFailureWithoutThrowing()
    {
        RecordingLogger<WindowsLauncherShellService> logger = new();
        WindowsLauncherShellService service = new(logger, _ => throw new Win32Exception(5));

        Action act = () => service.OpenUri("https://example.test/mods");

        act.Should().NotThrow();
        logger.Entries.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Warning &&
            entry.Exception is Win32Exception);
    }

    [Fact]
    public void OpenFolder_DoesNotLaunchEmptyFolder()
    {
        WindowsLauncherShellService service = CreateService();

        service.OpenFolder(" ");

        _openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenFolder_DoesNotLaunchInvalidPath()
    {
        WindowsLauncherShellService service = CreateService();

        service.OpenFolder("bad\0path");

        _openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenFolder_DoesNotLaunchMissingFolder()
    {
        using TestDirectory directory = new();
        WindowsLauncherShellService service = CreateService();
        string missingFolder = Path.Combine(directory.Path, "missing");

        service.OpenFolder(missingFolder);

        _openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenFolder_CreatesMissingFolderWhenRequested()
    {
        using TestDirectory directory = new();
        string missingFolder = Path.Combine(directory.Path, "Logs");
        WindowsLauncherShellService service = CreateService();

        service.OpenFolder(missingFolder, createIfMissing: true);

        Directory.Exists(missingFolder).Should().BeTrue();
        _openedTargets.Should().Equal(Path.GetFullPath(missingFolder));
    }

    [Fact]
    public void OpenFolder_DoesNotLaunchWhenMissingFolderCannotBeCreated()
    {
        using TestDirectory directory = new();
        string filePath = Path.Combine(directory.Path, "Logs");
        File.WriteAllText(filePath, "not a directory");
        WindowsLauncherShellService service = CreateService();

        service.OpenFolder(filePath, createIfMissing: true);

        _openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenFolder_DoesNotLaunchEmptyFolderWhenFilesAreRequired()
    {
        using TestDirectory directory = new();
        WindowsLauncherShellService service = CreateService();

        service.OpenFolder(directory.Path, true);

        _openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenFolder_OpensExistingFolder()
    {
        using TestDirectory directory = new();
        WindowsLauncherShellService service = CreateService();

        service.OpenFolder(directory.Path);

        _openedTargets.Should().Equal(Path.GetFullPath(directory.Path));
    }

    [Fact]
    public void OpenFolder_OpensExistingFolderWhenRequiredFilesExist()
    {
        using TestDirectory directory = new();
        File.WriteAllText(Path.Combine(directory.Path, "file.txt"), "content");
        WindowsLauncherShellService service = CreateService();

        service.OpenFolder(directory.Path, true);

        _openedTargets.Should().Equal(Path.GetFullPath(directory.Path));
    }

    private WindowsLauncherShellService CreateService()
    {
        return CreateService(_openedTargets.Add);
    }

    private static WindowsLauncherShellService CreateService(Action<string> openShellTarget)
    {
        return new WindowsLauncherShellService(
            NullLogger<WindowsLauncherShellService>.Instance,
            openShellTarget);
    }
}
