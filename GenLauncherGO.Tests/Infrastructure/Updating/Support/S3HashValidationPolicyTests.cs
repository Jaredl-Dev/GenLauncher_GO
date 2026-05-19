using System.Collections.Generic;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Support;

public sealed class S3HashValidationPolicyTests
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789ABCDEF")]
    public void IsReliableMd5HashReturnsTrueForPlainHexMd5(string hash)
    {
        bool result = S3HashValidationPolicy.IsReliableMd5Hash(hash);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef-2")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void IsReliableMd5HashReturnsFalseForMultipartOrMalformedHashes(string hash)
    {
        bool result = S3HashValidationPolicy.IsReliableMd5Hash(hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldCheckHashReturnsTrueWhenExtensionRequiresReliableMd5Validation()
    {
        RemoteFileManifestEntry file = new(
            "Data/asset.big",
            "0123456789abcdef0123456789abcdef",
            10);
        HashSet<string> hashCheckedExtensions = new(
            new[] { ".big" },
            System.StringComparer.OrdinalIgnoreCase);

        bool result = S3HashValidationPolicy.ShouldCheckHash(file, hashCheckedExtensions);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldCheckHashReturnsFalseForUncheckedExtension()
    {
        RemoteFileManifestEntry file = new(
            "Data/readme.txt",
            "0123456789abcdef0123456789abcdef",
            10);
        HashSet<string> hashCheckedExtensions = new(
            new[] { ".big" },
            System.StringComparer.OrdinalIgnoreCase);

        bool result = S3HashValidationPolicy.ShouldCheckHash(file, hashCheckedExtensions);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldCheckHashReturnsFalseForUnreliableHash()
    {
        RemoteFileManifestEntry file = new(
            "Data/asset.big",
            "0123456789abcdef0123456789abcdef-2",
            10);
        HashSet<string> hashCheckedExtensions = new(
            new[] { ".big" },
            System.StringComparer.OrdinalIgnoreCase);

        bool result = S3HashValidationPolicy.ShouldCheckHash(file, hashCheckedExtensions);

        result.Should().BeFalse();
    }
}
