using GenLauncherGO.Features.Launching;

namespace GenLauncherGO.Tests.Features.Launching;

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
    public void SetArgumentEnabled_RemovesStandaloneArgumentAndKeepsOtherArguments()
    {
        string arguments = "-foo \"bar baz\" -win -quickstart";

        string result = LauncherGameArgumentService.SetArgumentEnabled(
            arguments,
            LauncherGameArgumentService.WindowedArgument,
            false);

        result.Should().Be("-foo \"bar baz\" -quickstart");
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

}
