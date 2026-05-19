using System.Collections.Generic;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Core.Mods.Models;

public sealed class LauncherContentKeyTests
{
    [Fact]
    public void VersionIdentityDeduplicatesCaseInsensitiveMatchesInHashCollections()
    {
        LauncherContentVersion first = CreateVersion("ShockWave", "1.2", ModificationType.Addon, "Parent");
        LauncherContentVersion duplicate = CreateVersion("shockwave", "1.2", ModificationType.Addon, "parent");
        var keys = new HashSet<LauncherContentKey>();

        keys.Add(first.ContentKey);
        keys.Add(duplicate.ContentKey);

        keys.Should().ContainSingle().Which.Should().Be(first.ContentKey);
    }

    [Fact]
    public void VersionIdentityIncludesVersionTypeAndParent()
    {
        LauncherContentKey key =
            CreateVersion("Shared", "1.0", ModificationType.Addon, "First").ContentKey;
        LauncherContentKey otherVersion =
            CreateVersion("Shared", "2.0", ModificationType.Addon, "First").ContentKey;
        LauncherContentKey otherType =
            CreateVersion("Shared", "1.0", ModificationType.Patch, "First").ContentKey;
        LauncherContentKey otherParent =
            CreateVersion("Shared", "1.0", ModificationType.Addon, "Second").ContentKey;

        key.Should().NotBe(otherVersion);
        key.Should().NotBe(otherType);
        key.Should().NotBe(otherParent);
    }

    [Fact]
    public void MissingIdentityTextRetainsEmptyStringComparisonSemantics()
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
    }

    [Fact]
    public void OriginalGameIdentityIsStableAndMatchesLegacyCasing()
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
    public void StableStringPreservesExistingIntegrityIdentityFormat()
    {
        var key = new LauncherContentKey(
            ModificationType.Addon,
            "ShockWave Patch",
            "Music Pack",
            "V1.2");

        key.ToStableString().Should().Be("addon:shockwave patch:music pack:v1.2");
    }

    private static LauncherContentVersion CreateVersion(
        string name,
        string version,
        ModificationType type,
        string parentContentName)
    {
        return new LauncherContentVersion
        {
            Name = name,
            Version = version,
            ModificationType = type,
            ParentContentName = parentContentName
        };
    }
}
