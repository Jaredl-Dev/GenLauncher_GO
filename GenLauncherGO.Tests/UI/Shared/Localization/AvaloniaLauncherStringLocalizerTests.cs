using System.Globalization;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.Tests.UI.Shared.Localization;

[Collection("AvaloniaLocalization")]
public sealed class AvaloniaLauncherStringLocalizerTests
{
    [Fact]
    public void Indexer_WithNeutralCulture_ReturnsStringFromUiResources()
    {
        AvaloniaLauncherStringLocalizer localizer = new();
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            string result = localizer["Update"];

            result.Should().Be("Update!");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void Indexer_WithSatelliteCulture_ReturnsStringFromUiResources()
    {
        AvaloniaLauncherStringLocalizer localizer = new();
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
            string result = localizer["Update"];

            result.Should().Be("\u041E\u0411\u041D\u041E\u0412\u0418\u0422\u042C!");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
