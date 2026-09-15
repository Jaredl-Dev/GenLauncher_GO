using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Features.Mods;

namespace GenLauncherGO.Tests.Features.Mods;

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
