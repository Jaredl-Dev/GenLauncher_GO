using System.Collections.Generic;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Core.Mods.Models;

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
    public void MissingIdentityText_RetainsEmptyStringComparisonSemantics()
    {
        var missingText = new LauncherContentKey(ModificationType.Mod, null, null, null);
        var emptyText = new LauncherContentKey(
            ModificationType.Mod,
            string.Empty,
            string.Empty,
            string.Empty);
        LauncherContentKey defaultKey = default;

        missingText.Should().Be(emptyText);
        defaultKey.Should().Be(emptyText);
        defaultKey.GetHashCode().Should().Be(emptyText.GetHashCode());
        missingText.ParentIdentity.Should().BeEmpty();
        missingText.Name.Should().BeEmpty();
        missingText.Version.Should().BeEmpty();
        missingText.HasName(null).Should().BeTrue();
        LauncherContentKey.OriginalGame.HasName("original game").Should().BeTrue();
    }

    [Fact]
    public void OriginalGameIdentity_IsStableAndMatchesLegacyCasing()
    {
        LauncherContentKey originalGame = LauncherContentKey.OriginalGame;
        var originalGamePatch = new LauncherContentKey(
            ModificationType.Patch,
            "Original game",
            "GenPatcher",
            "1.0");

        originalGame.ContentType.Should().Be(ModificationType.Mod);
        originalGame.ParentIdentity.Should().BeEmpty();
        originalGame.Name.Should().Be("Original Game");
        originalGame.Version.Should().BeEmpty();
        originalGamePatch.IsChildOf(originalGame).Should().BeTrue();
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

    [Fact]
    public void ModificationNameIdentity_SetsOnlyTheModificationName()
    {
        var key = LauncherContentKey.ForModificationName("ShockWave");

        key.ContentType.Should().Be(ModificationType.Mod);
        key.ParentIdentity.Should().BeEmpty();
        key.Name.Should().Be("ShockWave");
        key.Version.Should().BeEmpty();
    }
}
