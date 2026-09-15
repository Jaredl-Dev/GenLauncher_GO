using GenLauncherGO.Features.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Features.Startup;

public sealed class WindowsLauncherHostEnvironmentServiceTests
{
    [Fact]
    public void GetLauncherRootDirectoryForPackagedBuild_ReturnsDurableVelopackRoot()
    {
        const string ProcessPath = @"C:\Portable\current\GenLauncherGO.exe";
        const string PortableRoot = @"C:\Portable";
        WindowsLauncherHostEnvironmentService service = CreateService(ProcessPath, PortableRoot);

        string rootDirectory = service.GetLauncherRootDirectory();

        rootDirectory.Should().Be(PortableRoot);
    }

    [Fact]
    public void GetLauncherRootDirectoryForUnpackagedBuild_ReturnsProcessDirectory()
    {
        const string ProcessPath = @"C:\Development\GenLauncherGO\GenLauncherGO.exe";
        WindowsLauncherHostEnvironmentService service = CreateService(ProcessPath, null);

        string rootDirectory = service.GetLauncherRootDirectory();

        rootDirectory.Should().Be(@"C:\Development\GenLauncherGO");
    }

    private static WindowsLauncherHostEnvironmentService CreateService(
        string processPath,
        string? packagedRootDirectory)
    {
        return new WindowsLauncherHostEnvironmentService(
            NullLogger<WindowsLauncherHostEnvironmentService>.Instance,
            _ => { },
            () => processPath,
            () => packagedRootDirectory);
    }
}
