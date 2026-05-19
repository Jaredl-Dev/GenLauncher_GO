using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Core.Mods.Models;

public sealed class LauncherDataTests
{
    [Fact]
    public void AddOrUpdateAddsContentToMatchingCollections()
    {
        LauncherData launcherData = new();

        launcherData.AddOrUpdate(CreateVersion("Shockwave", ModificationType.Mod));
        launcherData.AddOrUpdate(CreateVersion("Patch", ModificationType.Patch));
        launcherData.AddOrUpdate(CreateVersion("Addon", ModificationType.Addon, parentContentName: "Shockwave"));
        launcherData.AddOrUpdate(CreateVersion("Orphan Addon", ModificationType.Addon));

        launcherData.Modifications.Select(modification => modification.Name)
            .Should()
            .ContainSingle()
            .Which.Should().Be("Shockwave");
        launcherData.Patches.Should().ContainSingle().Which.Name.Should().Be("Patch");
        launcherData.Addons.Should().ContainSingle().Which.Name.Should().Be("Addon");
    }

    [Fact]
    public void AddOrUpdateMergesMatchingVersionsIntoExistingContentCard()
    {
        LauncherData launcherData = new();
        LauncherContentVersion installedVersion = CreateVersion("Shockwave", ModificationType.Mod, "1.0");
        installedVersion.Installation.Installed = true;
        LauncherContentVersion selectedVersion = CreateVersion("shockwave", ModificationType.Mod, "1.0");
        selectedVersion.Installation.IsSelected = true;

        launcherData.AddOrUpdate(installedVersion);
        launcherData.AddOrUpdate(selectedVersion);

        LauncherContent modification = launcherData.Modifications.Should().ContainSingle().Which;
        modification.Versions.Should().ContainSingle();
        modification.Installed.Should().BeTrue();
        modification.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void DeleteRemovesMatchingVersionAndDeletesEmptyContentCard()
    {
        LauncherData launcherData = new();
        LauncherContentVersion versionOne = CreateVersion("Shockwave", ModificationType.Mod, "1.0");
        LauncherContentVersion versionTwo = CreateVersion("Shockwave", ModificationType.Mod, "2.0");
        launcherData.AddOrUpdate(versionOne);
        launcherData.AddOrUpdate(versionTwo);

        launcherData.DeleteVersion(versionOne.ContentKey);
        launcherData.DeleteVersion(versionTwo.ContentKey);

        launcherData.Modifications.Should().BeEmpty();
    }

    [Fact]
    public void AddOrUpdateKeepsChildCardsWithSameNameUnderDifferentParentsSeparate()
    {
        LauncherData launcherData = new();
        LauncherContentVersion firstAddon = CreateVersion("Shared Addon", ModificationType.Addon, parentContentName: "First");
        LauncherContentVersion secondAddon = CreateVersion("Shared Addon", ModificationType.Addon, parentContentName: "Second");

        launcherData.AddOrUpdate(firstAddon);
        launcherData.AddOrUpdate(secondAddon);

        launcherData.Addons.Should().HaveCount(2);
        launcherData.Addons.Should().ContainSingle(addon => addon.ContentKey.ParentIdentity == "First");
        launcherData.Addons.Should().ContainSingle(addon => addon.ContentKey.ParentIdentity == "Second");
    }

    [Fact]
    public void FindContentUsesTypeParentNameAndOmitsVersionForCardLookup()
    {
        LauncherData launcherData = new();
        LauncherContentVersion firstAddon = CreateVersion(
            "Shared Addon",
            ModificationType.Addon,
            "1.0",
            "First");
        LauncherContentVersion secondAddon = CreateVersion(
            "Shared Addon",
            ModificationType.Addon,
            "2.0",
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
    public void DeleteContentRemovesEveryVersionAndDependentAddonAndPatchCards()
    {
        LauncherData launcherData = new();
        LauncherContentVersion mod = CreateVersion("Parent", ModificationType.Mod);
        LauncherContentVersion secondModVersion = CreateVersion("Parent", ModificationType.Mod, "2.0");
        LauncherContentVersion addon = CreateVersion("Addon", ModificationType.Addon, parentContentName: "Parent");
        LauncherContentVersion patch = CreateVersion("Patch", ModificationType.Patch, parentContentName: "Parent");
        LauncherContentVersion patchAddon = CreateVersion("Patch Addon", ModificationType.Addon, parentContentName: "Patch");
        LauncherContentVersion unrelatedAddon = CreateVersion("Addon", ModificationType.Addon, parentContentName: "Other");
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
    public void DeleteAddonRemovesOnlyMatchingAddonCard()
    {
        LauncherData launcherData = new();
        LauncherContentVersion addon = CreateVersion("Addon", ModificationType.Addon, parentContentName: "Shockwave");
        LauncherContentVersion patch = CreateVersion("Addon", ModificationType.Patch);
        launcherData.AddOrUpdate(addon);
        launcherData.AddOrUpdate(patch);

        launcherData.DeleteVersion(addon.ContentKey);

        launcherData.Addons.Should().BeEmpty();
        launcherData.Patches.Should().ContainSingle();
    }

    [Fact]
    public void DeletePatchRemovesOnlyMatchingPatchCard()
    {
        LauncherData launcherData = new();
        LauncherContentVersion addon = CreateVersion("Patch", ModificationType.Addon, parentContentName: "Shockwave");
        LauncherContentVersion patch = CreateVersion("Patch", ModificationType.Patch);
        launcherData.AddOrUpdate(addon);
        launcherData.AddOrUpdate(patch);

        launcherData.DeleteVersion(patch.ContentKey);

        launcherData.Patches.Should().BeEmpty();
        launcherData.Addons.Should().ContainSingle();
    }

    [Fact]
    public void DeletePatchAlsoDeletesDependentAddonCards()
    {
        LauncherData launcherData = new();
        LauncherContentVersion patch = CreateVersion("Patch", ModificationType.Patch, parentContentName: "Shockwave");
        LauncherContentVersion dependentAddon = CreateVersion("Addon", ModificationType.Addon, parentContentName: "Patch");
        LauncherContentVersion unrelatedAddon = CreateVersion("Addon", ModificationType.Addon, parentContentName: "Other");
        launcherData.AddOrUpdate(patch);
        launcherData.AddOrUpdate(dependentAddon);
        launcherData.AddOrUpdate(unrelatedAddon);

        launcherData.DeleteVersion(patch.ContentKey);

        launcherData.Patches.Should().BeEmpty();
        launcherData.Addons.Should().ContainSingle().Which.ContentKey.ParentIdentity.Should().Be("Other");
    }

    [Fact]
    public void AddOrUpdateDoesNotEmbedAdvertisingInPersistentContentCollections()
    {
        LauncherData launcherData = new();
        LauncherContentVersion advertising = CreateVersion("Featured", ModificationType.Advertising);
        launcherData.AddOrUpdate(advertising);

        launcherData.Modifications.Should().BeEmpty();
        launcherData.FindContent(advertising.ContentKey).Should().BeNull();
    }

    [Fact]
    public void PersistedSelectedModificationQueryReturnsSelectedCard()
    {
        LauncherData launcherData = new();
        LauncherContentVersion selectedVersion = CreateVersion("ShockWave", ModificationType.Mod, "1.0");
        selectedVersion.Installation.IsSelected = true;
        launcherData.AddOrUpdate(selectedVersion);
        launcherData.AddOrUpdate(CreateVersion("ShockWave", ModificationType.Mod, "1.1"));

        LauncherContent? selectedModification = launcherData.GetSelectedMod();

        selectedModification.Should().NotBeNull();
        selectedModification!.Name.Should().Be("ShockWave");
    }

    [Fact]
    public void OriginalGameContentQueriesUseOriginalGameDependenciesWhenNoParentIsSupplied()
    {
        LauncherData launcherData = new();
        LauncherContentVersion patch = CreateVersion(
            "Original Patch",
            ModificationType.Patch,
            parentContentName: LauncherContentKey.OriginalGame.Name);
        patch.Installation.IsSelected = true;
        LauncherContentVersion originalAddon = CreateVersion(
            "Original Addon",
            ModificationType.Addon,
            "2.0",
            LauncherContentKey.OriginalGame.Name);
        originalAddon.Installation.IsSelected = true;
        LauncherContentVersion patchAddon = CreateVersion(
            "Patch Addon",
            ModificationType.Addon,
            "3.0",
            "Original Patch");
        patchAddon.Installation.IsSelected = true;
        launcherData.AddOrUpdate(patch);
        launcherData.AddOrUpdate(originalAddon);
        launcherData.AddOrUpdate(patchAddon);

        LauncherContent selectedPatch = launcherData.Patches.Single();
        IReadOnlyList<LauncherContent> patches = launcherData.GetPatchesFor(null);
        IReadOnlyList<LauncherContent> addons = launcherData.GetAddonsFor(null, selectedPatch);

        patches.Select(item => item.Name).Should().Equal("Original Patch");
        addons.Select(addon => addon.Name).Should().BeEquivalentTo("Original Addon", "Patch Addon");
    }

    [Fact]
    public void GetAddonsForIncludesPatchDependentAddons()
    {
        LauncherData launcherData = new();
        LauncherContentVersion modification = CreateVersion("ShockWave", ModificationType.Mod);
        modification.Installation.IsSelected = true;
        LauncherContentVersion patch = CreateVersion(
            "ShockWave Patch",
            ModificationType.Patch,
            "1.1",
            "ShockWave");
        patch.Installation.IsSelected = true;
        LauncherContentVersion patchAddon = CreateVersion(
            "Patch Addon",
            ModificationType.Addon,
            "2.0",
            "ShockWave Patch");
        patchAddon.Installation.IsSelected = true;
        launcherData.AddOrUpdate(modification);
        launcherData.AddOrUpdate(patch);
        launcherData.AddOrUpdate(patchAddon);
        launcherData.AddOrUpdate(CreateVersion(
            "Mod Addon",
            ModificationType.Addon,
            "3.0",
            "ShockWave"));

        LauncherContent selectedModification = launcherData.Modifications.Single();
        LauncherContent selectedPatch = launcherData.Patches.Single();
        IReadOnlyList<LauncherContent> addons = launcherData.GetAddonsFor(
            selectedModification,
            selectedPatch);

        addons.Select(addon => addon.Name).Should().BeEquivalentTo("Patch Addon", "Mod Addon");
    }

    private static LauncherContentVersion CreateVersion(
        string name,
        ModificationType modificationType,
        string version = "1.0",
        string parentContentName = "")
    {
        return new LauncherContentVersion
        {
            Name = name,
            Version = version,
            ModificationType = modificationType,
            ParentContentName = parentContentName
        };
    }
}
