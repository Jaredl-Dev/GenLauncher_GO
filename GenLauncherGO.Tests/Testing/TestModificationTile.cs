using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Localization;
using GenLauncherGO.Shared.Themes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Owns the collaborators a modification tile needs but no test is asserting about.
/// </summary>
internal static class TestModificationTile
{
    public static ModificationViewModel Create(
        LauncherContent content,
        ILauncherStringLocalizer? localizer = null,
        LauncherPackageActivityService? activity = null,
        ColorsInfo? colors = null,
        IModificationImageFileService? imageFileService = null)
    {
        return new ModificationViewModel(
            content,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(colors: colors),
            imageFileService ?? Substitute.For<IModificationImageFileService>(),
            localizer ?? new FakeStringLocalizer(),
            activity ?? new LauncherPackageActivityService(),
            NullLogger<ModificationViewModel>.Instance);
    }
}
