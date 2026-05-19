using System.Globalization;
using GenLauncherGO.UI.Features.Startup.Services;

namespace GenLauncherGO.Tests.UI.Features.Startup;

[Collection("AvaloniaLocalization")]
public sealed class LauncherStartupCultureTests
{
    [Fact]
    public void ApplyWhenEnglishIsPersistedSetsUiCultureWithoutChangingFormattingCulture()
    {
        CultureInfo previousCurrentCulture = CultureInfo.CurrentCulture;
        CultureInfo previousCurrentUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo? previousDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo? previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            LauncherStartupCulture.Apply(useEnglishLanguage: true);

            CultureInfo.CurrentCulture.Should().Be(previousCurrentCulture);
            CultureInfo.CurrentUICulture.Name.Should().Be("en-US");
            CultureInfo.DefaultThreadCurrentCulture.Should().Be(previousDefaultCulture);
            CultureInfo.DefaultThreadCurrentUICulture!.Name.Should().Be("en-US");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCurrentCulture;
            CultureInfo.CurrentUICulture = previousCurrentUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = previousDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
        }
    }
}
