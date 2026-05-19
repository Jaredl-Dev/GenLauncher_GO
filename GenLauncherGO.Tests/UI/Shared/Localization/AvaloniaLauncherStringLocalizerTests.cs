using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.Tests.UI.Shared.Localization;

[Collection("Avalonia")]
public sealed class AvaloniaLauncherStringLocalizerTests
{
    [Fact]
    public void Indexer_WithNeutralCulture_ReturnsStringFromUiResources()
    {
        AvaloniaLauncherStringLocalizer localizer = new();
        using CultureScope culture = new(uiCultureName: "en");

        string result = localizer["Update"];

        result.Should().Be("Update!");
    }

    [Fact]
    public void Indexer_WithSatelliteCulture_ReturnsStringFromUiResources()
    {
        AvaloniaLauncherStringLocalizer localizer = new();
        using CultureScope culture = new(uiCultureName: "ru");

        string result = localizer["Update"];

        result.Should().Be("ОБНОВИТЬ!");
    }

    /// <summary>
    ///     A key the resource does not carry has to surface as itself, so a missing string reads as the key that is
    ///     missing rather than as blank UI nobody can trace back to a resource.
    /// </summary>
    [Fact]
    public void Indexer_WithMissingKey_ReturnsTheKeyAsDiagnosticPlaceholder()
    {
        AvaloniaLauncherStringLocalizer localizer = new();
        using CultureScope culture = new(uiCultureName: "en");

        string result = localizer["ThisKeyIsNotShipped"];

        result.Should().Be("ThisKeyIsNotShipped");
    }
}
