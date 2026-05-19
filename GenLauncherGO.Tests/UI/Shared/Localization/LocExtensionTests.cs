using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.Tests.UI.Shared.Localization;

/// <summary>
///     Covers the markup extension every AXAML label resolves its text through.
/// </summary>
public sealed class LocExtensionTests
{
    [Fact]
    public void ProvideValue_WithKnownKey_ReturnsTheValueForTheCurrentUiCulture()
    {
        LocExtension extension = new("Update");
        using CultureScope culture = new(uiCultureName: "ru");

        string value = extension.ProvideValue();

        value.Should().Be("ОБНОВИТЬ!");
    }

    /// <summary>
    ///     Markup resolves at load time, so a key nobody ships has to render as itself rather than leave the control
    ///     blank with nothing naming what is missing.
    /// </summary>
    [Fact]
    public void ProvideValue_WithUnknownKey_ReturnsTheKey()
    {
        LocExtension extension = new("ThisKeyIsNotShipped");
        using CultureScope culture = new(uiCultureName: "ru");

        string value = extension.ProvideValue();

        value.Should().Be("ThisKeyIsNotShipped");
    }
}
