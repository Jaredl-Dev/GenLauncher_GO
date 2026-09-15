using System.Collections.Generic;
using GenLauncherGO.Features.Mods;

namespace GenLauncherGO.Tests.Features.Mods;

public sealed class LauncherContentKeyTests
{
    [Fact]
    public void VersionIdentity_DeduplicatesCaseInsensitiveMatchesInHashCollections()
    {
        LauncherContentVersion first = TestLauncherContent.Version(
            "ShockWave",
            "1.2",
            ModificationType.Addon,
            "Parent");
        LauncherContentVersion duplicate = TestLauncherContent.Version(
            "shockwave",
            "1.2",
            ModificationType.Addon,
            "parent");
        var keys = new HashSet<LauncherContentKey>
        {
            first.ContentKey,
            duplicate.ContentKey
        };

        keys.Should().ContainSingle().Which.Should().Be(first.ContentKey);
    }

    /// <summary>
    ///     Every identity component has to reach both the comparison and the hash. A component the hash leaves out
    ///     still compares correctly, so equality alone cannot see the omission — while every catalog key that differs
    ///     only in that component starts landing in one bucket.
    /// </summary>
    [Fact]
    public void VersionIdentity_IncludesVersionTypeAndParent()
    {
        LauncherContentKey key = TestLauncherContent
            .Version("Shared", "1.0", ModificationType.Addon, "First").ContentKey;
        LauncherContentKey otherName = TestLauncherContent
            .Version("Other", "1.0", ModificationType.Addon, "First").ContentKey;
        LauncherContentKey otherVersion = TestLauncherContent
            .Version("Shared", "2.0", ModificationType.Addon, "First").ContentKey;
        LauncherContentKey otherType = TestLauncherContent
            .Version("Shared", "1.0", ModificationType.Patch, "First").ContentKey;
        LauncherContentKey otherParent = TestLauncherContent
            .Version("Shared", "1.0", ModificationType.Addon, "Second").ContentKey;

        key.Should().NotBe(otherName);
        key.Should().NotBe(otherVersion);
        key.Should().NotBe(otherType);
        key.Should().NotBe(otherParent);
        key.GetHashCode().Should().NotBe(otherName.GetHashCode());
        key.GetHashCode().Should().NotBe(otherVersion.GetHashCode());
        key.GetHashCode().Should().NotBe(otherType.GetHashCode());
        key.GetHashCode().Should().NotBe(otherParent.GetHashCode());
    }

    [Fact]
    public void StableString_PreservesExistingIntegrityIdentityFormat()
    {
        var key = new LauncherContentKey(
            ModificationType.Addon,
            "ShockWave Patch",
            "Music Pack",
            "V1.2");

        key.ToStableString().Should().Be("addon:shockwave patch:music pack:v1.2");
    }

}
