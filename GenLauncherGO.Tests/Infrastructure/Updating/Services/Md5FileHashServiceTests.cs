using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Services;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Services;

public sealed class Md5FileHashServiceTests
{
    [Fact]
    public async Task ComputeMd5HashAsync_ReturnsUppercaseMd5HashAsync()
    {
        using TestDirectory testDirectory = new();
        string filePath = Path.Combine(testDirectory.Path, "payload.txt");
        await File.WriteAllTextAsync(filePath, "abc");
        var service = new Md5FileHashService();

        string hash = await service.ComputeMd5HashAsync(filePath, CancellationToken.None);

        hash.Should().Be("900150983CD24FB0D6963F7D28E17F72");
    }

    /// <summary>
    ///     Package integrity is decided by comparing this hash against a manifest entry, so a file that cannot be read
    ///     has to surface the read failure. A swallowed failure would hand the caller a hash it never computed.
    /// </summary>
    [Fact]
    public async Task ComputeMd5HashAsync_UnreadableFile_SurfacesTheReadFailureAsync()
    {
        using TestDirectory testDirectory = new();
        string missingFilePath = Path.Combine(testDirectory.Path, "absent.txt");
        var service = new Md5FileHashService();

        Func<Task<string>> computeHash = () => service.ComputeMd5HashAsync(missingFilePath, CancellationToken.None);

        await computeHash.Should().ThrowAsync<IOException>();
    }
}
