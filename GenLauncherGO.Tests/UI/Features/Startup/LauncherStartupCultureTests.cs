using System.Globalization;
using GenLauncherGO.UI.Features.Startup.Services;

namespace GenLauncherGO.Tests.UI.Features.Startup;

[Collection("Avalonia")]
public sealed class LauncherStartupCultureTests
{
    [Fact]
    public void ApplyWhenEnglish_IsPersistedSetsUiCultureWithoutChangingFormattingCulture()
    {
        using CultureScope cultureScope = new();
        CultureInfo previousCurrentCulture = CultureInfo.CurrentCulture;
        CultureInfo? previousDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;

        LauncherStartupCulture.Apply(true);

        CultureInfo.CurrentCulture.Should().Be(previousCurrentCulture);
        CultureInfo.CurrentUICulture.Name.Should().Be("en-US");
        CultureInfo.DefaultThreadCurrentCulture.Should().Be(previousDefaultCulture);
        CultureInfo.DefaultThreadCurrentUICulture!.Name.Should().Be("en-US");
    }

    [Fact]
    public void ApplyWhenEnglish_IsNotPersistedUsesInstalledUiCulture()
    {
        using CultureScope cultureScope = new("de-DE", "de-DE");
        CultureInfo previousCurrentCulture = CultureInfo.CurrentCulture;
        CultureInfo? previousDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;

        LauncherStartupCulture.Apply(false);

        CultureInfo.CurrentUICulture.Should().Be(CultureInfo.InstalledUICulture);
        CultureInfo.DefaultThreadCurrentUICulture.Should().Be(CultureInfo.InstalledUICulture);
        CultureInfo.CurrentCulture.Should().Be(previousCurrentCulture);
        CultureInfo.DefaultThreadCurrentCulture.Should().Be(previousDefaultCulture);
    }
}
