using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Core.Mods.Models;

public sealed class LauncherDataTests
{
    [Fact]
    public void AddOrUpdate_AddsContentToMatchingCollections()
    {
        LauncherData launcherData = new();

        launcherData.AddOrUpdate(TestLauncherContent.Version("Shockwave", type: ModificationType.Mod));
        launcherData.AddOrUpdate(TestLauncherContent.Version("Patch", type: ModificationType.Patch));
        launcherData.AddOrUpdate(TestLauncherContent.Version(
            "Addon",
            type: ModificationType.Addon,
            parentContentName: "Shockwave"));
        launcherData.AddOrUpdate(TestLauncherContent.Version("Orphan Addon", type: ModificationType.Addon));

        launcherData.Modifications.Select(modification => modification.Name)
            .Should()
            .ContainSingle()
            .Which.Should().Be("Shockwave");
        launcherData.Patches.Should().ContainSingle().Which.Name.Should().Be("Patch");
        launcherData.Addons.Should().ContainSingle().Which.Name.Should().Be("Addon");
    }

    [Fact]
    public void AddOrUpdate_MergesMatchingVersionsIntoExistingContentCard()
    {
        LauncherData launcherData = new();
        LauncherContentVersion installedVersion = TestLauncherContent.Version("Shockwave", installed: true);
        LauncherContentVersion selectedVersion = TestLauncherContent.Version("shockwave", isSelected: true);

        launcherData.AddOrUpdate(installedVersion);
        launcherData.AddOrUpdate(selectedVersion);

        LauncherContent modification = launcherData.Modifications.Should().ContainSingle().Which;
        modification.Versions.Should().ContainSingle();
        modification.Installed.Should().BeTrue();
        modification.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void Delete_RemovesMatchingVersionAndDeletesEmptyContentCard()
    {
        LauncherData launcherData = new();
        LauncherContentVersion versionOne = TestLauncherContent.Version("Shockwave");
        LauncherContentVersion versionTwo = TestLauncherContent.Version("Shockwave", "2.0");
        launcherData.AddOrUpdate(versionOne);
        launcherData.AddOrUpdate(versionTwo);

        launcherData.DeleteVersion(versionOne.ContentKey);
        launcherData.DeleteVersion(versionTwo.ContentKey);

        launcherData.Modifications.Should().BeEmpty();
    }

    [Fact]
    public void DeleteVersion_WhenAnotherVersionRemains_KeepsTheCardAndItsDependentContent()
    {
        LauncherData launcherData = new();
        LauncherContentVersion firstVersion = TestLauncherContent.Version("Parent");
        LauncherContentVersion secondVersion = TestLauncherContent.Version("Parent", "2.0");
        LauncherContentVersion addon = TestLauncherContent.Version(
            "Addon",
            type: ModificationType.Addon,
            parentContentName: "Parent");
        launcherData.AddOrUpdate(firstVersion);
        launcherData.AddOrUpdate(secondVersion);
        launcherData.AddOrUpdate(addon);

        launcherData.DeleteVersion(secondVersion.ContentKey);

        LauncherContent parent = launcherData.Modifications.Should().ContainSingle().Which;
        parent.Versions.Should().ContainSingle().Which.Should().BeSameAs(firstVersion);
        launcherData.Addons.Should().ContainSingle();
    }

    [Fact]
    public void DeleteVersion_WhenTheContentCardIsMissing_KeepsDependentContent()
    {
        LauncherData launcherData = new();
        LauncherContentVersion addon = TestLauncherContent.Version(
            "Addon",
            type: ModificationType.Addon,
            parentContentName: "Parent");
        launcherData.AddOrUpdate(addon);

        launcherData.DeleteVersion(new LauncherContentKey(ModificationType.Mod, string.Empty, "Parent", "1.0"));

        launcherData.Addons.Should().ContainSingle();
    }

    [Fact]
    public void DeleteContent_WhenTheContentCardIsMissing_KeepsDependentContent()
    {
        LauncherData launcherData = new();
        LauncherContentVersion addon = TestLauncherContent.Version(
            "Addon",
            type: ModificationType.Addon,
            parentContentName: "Parent");
        launcherData.AddOrUpdate(addon);

        launcherData.DeleteContent(LauncherContentKey.ForModificationName("Parent"));

        launcherData.Addons.Should().ContainSingle();
    }

    [Fact]
    public void AddOrUpdate_KeepsChildCardsWithSameNameUnderDifferentParentsSeparate()
    {
        LauncherData launcherData = new();
        LauncherContentVersion firstAddon = TestLauncherContent.Version(
            "Shared Addon",
            type: ModificationType.Addon,
            parentContentName: "First");
        LauncherContentVersion secondAddon = TestLauncherContent.Version(
            "Shared Addon",
            type: ModificationType.Addon,
            parentContentName: "Second");

        launcherData.AddOrUpdate(firstAddon);
        launcherData.AddOrUpdate(secondAddon);

        launcherData.Addons.Should().HaveCount(2);
        launcherData.Addons.Should().ContainSingle(addon => addon.ContentKey.ParentIdentity == "First");
        launcherData.Addons.Should().ContainSingle(addon => addon.ContentKey.ParentIdentity == "Second");
    }

    [Fact]
    public void FindContent_UsesTypeParentNameAndOmitsVersionForCardLookup()
    {
        LauncherData launcherData = new();
        LauncherContentVersion firstAddon = TestLauncherContent.Version(
            "Shared Addon",
            "1.0",
            ModificationType.Addon,
            "First");
        LauncherContentVersion secondAddon = TestLauncherContent.Version(
            "Shared Addon",
            "2.0",
            ModificationType.Addon,
            "Second");
        launcherData.AddOrUpdate(firstAddon);
        launcherData.AddOrUpdate(secondAddon);

        LauncherContent? found = launcherData.FindContent(new LauncherContentKey(
            ModificationType.Addon,
            "second",
            "shared addon",
            "different version"));

        found.Should().NotBeNull();
        found!.ContentKey.ParentIdentity.Should().Be("Second");
        found.Versions.Should().ContainSingle().Which.Should().BeSameAs(secondAddon);
    }

    [Fact]
    public void DeleteContent_RemovesEveryVersionAndDependentAddonAndPatchCards()
    {
        LauncherData launcherData = new();
        LauncherContentVersion mod = TestLauncherContent.Version("Parent");
        LauncherContentVersion secondModVersion = TestLauncherContent.Version("Parent", "2.0");
        LauncherContentVersion addon = TestLauncherContent.Version(
            "Addon",
            type: ModificationType.Addon,
            parentContentName: "Parent");
        LauncherContentVersion patch = TestLauncherContent.Version(
            "Patch",
            type: ModificationType.Patch,
            parentContentName: "Parent");
        LauncherContentVersion patchAddon = TestLauncherContent.Version(
            "Patch Addon",
            type: ModificationType.Addon,
            parentContentName: "Patch");
        LauncherContentVersion unrelatedAddon = TestLauncherContent.Version(
            "Addon",
            type: ModificationType.Addon,
            parentContentName: "Other");
        launcherData.AddOrUpdate(mod);
        launcherData.AddOrUpdate(secondModVersion);
        launcherData.AddOrUpdate(addon);
        launcherData.AddOrUpdate(patch);
        launcherData.AddOrUpdate(patchAddon);
        launcherData.AddOrUpdate(unrelatedAddon);

        launcherData.DeleteContent(mod.ContentKey);

        launcherData.Modifications.Should().BeEmpty();
        launcherData.Addons.Should().ContainSingle().Which.ContentKey.ParentIdentity.Should().Be("Other");
        launcherData.Patches.Should().BeEmpty();
    }

    [Fact]
    public void DeleteAddon_RemovesOnlyMatchingAddonCard()
    {
        LauncherData launcherData = new();
        LauncherContentVersion addon = TestLauncherContent.Version(
            "Addon",
            type: ModificationType.Addon,
            parentContentName: "Shockwave");
        LauncherContentVersion patch = TestLauncherContent.Version("Addon", type: ModificationType.Patch);
        launcherData.AddOrUpdate(addon);
        launcherData.AddOrUpdate(patch);

        launcherData.DeleteVersion(addon.ContentKey);

        launcherData.Addons.Should().BeEmpty();
        launcherData.Patches.Should().ContainSingle();
    }

    [Fact]
    public void DeletePatch_RemovesOnlyMatchingPatchCard()
    {
        LauncherData launcherData = new();
        LauncherContentVersion addon = TestLauncherContent.Version(
            "Patch",
            type: ModificationType.Addon,
            parentContentName: "Shockwave");
        LauncherContentVersion patch = TestLauncherContent.Version("Patch", type: ModificationType.Patch);
        launcherData.AddOrUpdate(addon);
        launcherData.AddOrUpdate(patch);

        launcherData.DeleteVersion(patch.ContentKey);

        launcherData.Patches.Should().BeEmpty();
        launcherData.Addons.Should().ContainSingle();
    }

    [Fact]
    public void DeletePatchAlso_DeletesDependentAddonCards()
    {
        LauncherData launcherData = new();
        LauncherContentVersion patch = TestLauncherContent.Version(
            "Patch",
            type: ModificationType.Patch,
            parentContentName: "Shockwave");
        LauncherContentVersion dependentAddon = TestLauncherContent.Version(
            "Addon",
            type: ModificationType.Addon,
            parentContentName: "Patch");
        LauncherContentVersion unrelatedAddon = TestLauncherContent.Version(
            "Addon",
            type: ModificationType.Addon,
            parentContentName: "Other");
        launcherData.AddOrUpdate(patch);
        launcherData.AddOrUpdate(dependentAddon);
        launcherData.AddOrUpdate(unrelatedAddon);

        launcherData.DeleteVersion(patch.ContentKey);

        launcherData.Patches.Should().BeEmpty();
        launcherData.Addons.Should().ContainSingle().Which.ContentKey.ParentIdentity.Should().Be("Other");
    }

    /// <summary>
    ///     Advertising has no persistent collection behind it, so every catalog operation has to route on the content
    ///     type rather than assume one. Removal is included because a card that was never stored still reaches the
    ///     delete paths when a tile is discarded.
    /// </summary>
    [Fact]
    public void AddOrUpdate_DoesNotEmbedAdvertisingInPersistentContentCollections()
    {
        LauncherData launcherData = new();
        LauncherContentVersion modification = TestLauncherContent.Version("ShockWave");
        LauncherContentVersion advertising = TestLauncherContent.Version(
            "Featured",
            type: ModificationType.Advertising);
        launcherData.AddOrUpdate(modification);
        launcherData.AddOrUpdate(advertising);

        launcherData.DeleteVersion(advertising.ContentKey);
        launcherData.DeleteContent(advertising.ContentKey);

        launcherData.Modifications.Should().ContainSingle()
            .Which.ContentKey.Should().Be(modification.ContentKey.WithoutVersion());
        launcherData.FindContent(advertising.ContentKey).Should().BeNull();
    }

    [Fact]
    public void PersistedSelectedModificationQuery_ReturnsSelectedCard()
    {
        LauncherData launcherData = new();
        LauncherContentVersion selectedVersion = TestLauncherContent.Version("ShockWave", isSelected: true);
        launcherData.AddOrUpdate(selectedVersion);
        launcherData.AddOrUpdate(TestLauncherContent.Version("ShockWave", "1.1"));

        LauncherContent? selectedModification = launcherData.GetSelectedMod();

        selectedModification.Should().NotBeNull();
        selectedModification!.Name.Should().Be("ShockWave");
    }

    [Fact]
    public void OriginalGameContent_QueriesUseOriginalGameDependenciesWhenNoParentIsSupplied()
    {
        LauncherData launcherData = new();
        LauncherContentVersion patch = TestLauncherContent.Version(
            "Original Patch",
            type: ModificationType.Patch,
            parentContentName: LauncherContentKey.OriginalGame.Name,
            isSelected: true);
        LauncherContentVersion originalAddon = TestLauncherContent.Version(
            "Original Addon",
            "2.0",
            ModificationType.Addon,
            LauncherContentKey.OriginalGame.Name,
            isSelected: true);
        LauncherContentVersion patchAddon = TestLauncherContent.Version(
            "Patch Addon",
            "3.0",
            ModificationType.Addon,
            "Original Patch",
            isSelected: true);
        launcherData.AddOrUpdate(patch);
        launcherData.AddOrUpdate(originalAddon);
        launcherData.AddOrUpdate(patchAddon);

        LauncherContent selectedPatch = launcherData.Patches.Single();
        IReadOnlyList<LauncherContent> patches = launcherData.GetPatchesFor(null);
        IReadOnlyList<LauncherContent> addons = launcherData.GetAddonsFor(null, selectedPatch);

        patches.Select(item => item.Name).Should().Equal("Original Patch");
        addons.Select(addon => addon.Name).Should().Equal("Original Addon", "Patch Addon");
    }

    [Fact]
    public void GetAddonsFor_IncludesPatchDependentAddons()
    {
        LauncherData launcherData = new();
        LauncherContentVersion modification = TestLauncherContent.Version("ShockWave", isSelected: true);
        LauncherContentVersion patch = TestLauncherContent.Version(
            "ShockWave Patch",
            "1.1",
            ModificationType.Patch,
            "ShockWave",
            isSelected: true);
        LauncherContentVersion patchAddon = TestLauncherContent.Version(
            "Patch Addon",
            "2.0",
            ModificationType.Addon,
            "ShockWave Patch",
            isSelected: true);
        launcherData.AddOrUpdate(modification);
        launcherData.AddOrUpdate(patch);
        launcherData.AddOrUpdate(patchAddon);
        launcherData.AddOrUpdate(TestLauncherContent.Version(
            "Mod Addon",
            "3.0",
            ModificationType.Addon,
            "ShockWave"));

        LauncherContent selectedModification = launcherData.Modifications.Single();
        LauncherContent selectedPatch = launcherData.Patches.Single();
        IReadOnlyList<LauncherContent> addons = launcherData.GetAddonsFor(
            selectedModification,
            selectedPatch);

        addons.Select(addon => addon.Name).Should().Equal("Mod Addon", "Patch Addon");
    }
}
