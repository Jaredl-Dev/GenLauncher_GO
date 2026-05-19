using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Services;
using GenLauncherGO.Tests.Testing;

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

}
