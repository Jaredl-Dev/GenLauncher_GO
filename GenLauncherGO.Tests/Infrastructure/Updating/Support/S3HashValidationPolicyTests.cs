using System;
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
    public void IsReliableMd5Hash_ReturnsTrueForPlainHexMd5(string hash)
    {
        bool result = S3HashValidationPolicy.IsReliableMd5Hash(hash);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef-2")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void IsReliableMd5Hash_ReturnsFalseForMultipartOrMalformedHashes(string hash)
    {
        bool result = S3HashValidationPolicy.IsReliableMd5Hash(hash);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("Data/asset.big", "0123456789abcdef0123456789abcdef", true)]
    [InlineData("Data/readme.txt", "0123456789abcdef0123456789abcdef", false)]
    [InlineData("Data/asset.big", "0123456789abcdef0123456789abcdef-2", false)]
    public void ShouldCheckHash_ReturnsExpectedResult(
        string path,
        string hash,
        bool expected)
    {
        RemoteFileManifestEntry file = new(path, hash, 10);
        HashSet<string> hashCheckedExtensions = new(
            new[] { ".big" },
            StringComparer.OrdinalIgnoreCase);

        bool result = S3HashValidationPolicy.ShouldCheckHash(file, hashCheckedExtensions);

        result.Should().Be(expected);
    }
}
