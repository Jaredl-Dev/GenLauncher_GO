using GenLauncherGO.Core.Launching;

namespace GenLauncherGO.Tests.Core.Launching;

public sealed class LauncherGameArgumentServiceTests
{
    [Fact]
    public void SetArgumentEnabled_AddsArgumentWhenMissing()
    {
        string arguments = "-foo";

        string result = LauncherGameArgumentService.SetArgumentEnabled(
            arguments,
            LauncherGameArgumentService.WindowedArgument,
            true);

        result.Should().Be("-foo -win");
    }

    [Fact]
    public void SetArgumentEnabled_DoesNotDuplicateExistingArgument()
    {
        string arguments = "-foo -WIN";

        string result = LauncherGameArgumentService.SetArgumentEnabled(
            arguments,
            LauncherGameArgumentService.WindowedArgument,
            true);

        result.Should().Be("-foo -WIN");
    }

    [Fact]
    public void SetArgumentEnabled_RemovesStandaloneArgumentAndKeepsOtherArguments()
    {
        string arguments = "-foo \"bar baz\" -win -quickstart";

        string result = LauncherGameArgumentService.SetArgumentEnabled(
            arguments,
            LauncherGameArgumentService.WindowedArgument,
            false);

        result.Should().Be("-foo \"bar baz\" -quickstart");
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  -foo   -win  ", "-foo")]
    public void SetArgumentEnabled_WhenDisablingArgument_NormalizesSurroundingWhitespace(
        string? arguments,
        string expected)
    {
        string result = LauncherGameArgumentService.SetArgumentEnabled(
            arguments,
            LauncherGameArgumentService.WindowedArgument,
            false);

        result.Should().Be(expected);
    }

    [Fact]
    public void SetArgumentEnabled_RemovesCompleteQuotedArgument()
    {
        string result = LauncherGameArgumentService.SetArgumentEnabled(
            "\"-win\" -quickstart",
            LauncherGameArgumentService.WindowedArgument,
            false);

        result.Should().Be("-quickstart");
    }

    [Fact]
    public void ContainsArgument_RequiresStandaloneArgument()
    {
        string arguments = "-windowed -quickstart";

        bool containsWindowed = LauncherGameArgumentService.ContainsArgument(
            arguments,
            LauncherGameArgumentService.WindowedArgument);
        bool containsQuickStart = LauncherGameArgumentService.ContainsArgument(
            arguments,
            LauncherGameArgumentService.QuickStartArgument);

        containsWindowed.Should().BeFalse();
        containsQuickStart.Should().BeTrue();
    }

    [Fact]
    public void ContainsArgument_RecognizesCompleteQuotedArgument()
    {
        bool result = LauncherGameArgumentService.ContainsArgument(
            "\"-win\" -quickstart",
            LauncherGameArgumentService.WindowedArgument);

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsArgument_DoesNotUnquoteIncompleteQuotedToken()
    {
        bool result = LauncherGameArgumentService.ContainsArgument(
            "\"-win",
            LauncherGameArgumentService.WindowedArgument);

        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsArgument_DoesNotTreatTrailingQuoteAsAnOpeningQuote()
    {
        bool result = LauncherGameArgumentService.ContainsArgument(
            "--win\"",
            LauncherGameArgumentService.WindowedArgument);

        result.Should().BeFalse();
    }
}
