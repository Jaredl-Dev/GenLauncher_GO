using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using GenLauncherGO.Infrastructure.Shell.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Shell.Services;

public sealed class WindowsLauncherShellServiceTests
{
    [Fact]
    public void OpenUriDoesNotLaunchEmptyUri()
    {
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenUri(" ");

        openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenUriDoesNotLaunchRelativeUri()
    {
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenUri("not-a-uri");

        openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenUriDoesNotLaunchUnsupportedScheme()
    {
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenUri("ftp://example.test/file.big");

        openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenUriOpensNormalizedHttpTarget()
    {
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenUri("HTTPS://Example.Test/mods?id=1");

        openedTargets.Should().Equal("https://example.test/mods?id=1");
    }

    [Fact]
    public void OpenUriDoesNotPropagateShellOpenFailure()
    {
        WindowsLauncherShellService service = CreateService(_ => throw new Win32Exception(5));

        Action act = () => service.OpenUri("https://example.test/mods");

        act.Should().NotThrow();
    }

    [Fact]
    public void OpenFolderDoesNotLaunchEmptyFolder()
    {
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenFolder(" ");

        openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenFolderDoesNotLaunchInvalidPath()
    {
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenFolder("bad\0path");

        openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenFolderDoesNotLaunchMissingFolder()
    {
        using TestDirectory directory = new();
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);
        string missingFolder = Path.Combine(directory.Path, "missing");

        service.OpenFolder(missingFolder);

        openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenFolderCreatesMissingFolderWhenRequested()
    {
        using TestDirectory directory = new();
        string missingFolder = Path.Combine(directory.Path, "Logs");
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenFolder(missingFolder, createIfMissing: true);

        Directory.Exists(missingFolder).Should().BeTrue();
        openedTargets.Should().Equal(Path.GetFullPath(missingFolder));
    }

    [Fact]
    public void OpenFolderDoesNotLaunchWhenMissingFolderCannotBeCreated()
    {
        using TestDirectory directory = new();
        string filePath = Path.Combine(directory.Path, "Logs");
        File.WriteAllText(filePath, "not a directory");
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenFolder(filePath, createIfMissing: true);

        openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenFolderDoesNotLaunchEmptyFolderWhenFilesAreRequired()
    {
        using TestDirectory directory = new();
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenFolder(directory.Path, requireFiles: true);

        openedTargets.Should().BeEmpty();
    }

    [Fact]
    public void OpenFolderOpensExistingFolder()
    {
        using TestDirectory directory = new();
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenFolder(directory.Path);

        openedTargets.Should().Equal(Path.GetFullPath(directory.Path));
    }

    [Fact]
    public void OpenFolderOpensExistingFolderWhenRequiredFilesExist()
    {
        using TestDirectory directory = new();
        File.WriteAllText(Path.Combine(directory.Path, "file.txt"), "content");
        List<string> openedTargets = new();
        WindowsLauncherShellService service = CreateService(openedTargets.Add);

        service.OpenFolder(directory.Path, requireFiles: true);

        openedTargets.Should().Equal(Path.GetFullPath(directory.Path));
    }

    private static WindowsLauncherShellService CreateService(Action<string> openShellTarget)
    {
        return new WindowsLauncherShellService(
            NullLogger<WindowsLauncherShellService>.Instance,
            openShellTarget);
    }
}
