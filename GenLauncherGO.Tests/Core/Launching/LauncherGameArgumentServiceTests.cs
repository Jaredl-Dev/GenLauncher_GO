using GenLauncherGO.Core.Launching;

namespace GenLauncherGO.Tests.Core.Launching;

public sealed class LauncherGameArgumentServiceTests
{
    [Fact]
    public void SetArgumentEnabledAddsArgumentWhenMissing()
    {
        string arguments = "-foo";

        string result = LauncherGameArgumentService.SetArgumentEnabled(
            arguments,
            LauncherGameArgumentService.WindowedArgument,
            enabled: true);

        result.Should().Be("-foo -win");
    }

    [Fact]
    public void SetArgumentEnabledDoesNotDuplicateExistingArgument()
    {
        string arguments = "-foo -WIN";

        string result = LauncherGameArgumentService.SetArgumentEnabled(
            arguments,
            LauncherGameArgumentService.WindowedArgument,
            enabled: true);

        result.Should().Be("-foo -WIN");
    }

    [Fact]
    public void SetArgumentEnabledRemovesStandaloneArgumentAndKeepsOtherArguments()
    {
        string arguments = "-foo \"bar baz\" -win -quickstart";

        string result = LauncherGameArgumentService.SetArgumentEnabled(
            arguments,
            LauncherGameArgumentService.WindowedArgument,
            enabled: false);

        result.Should().Be("-foo \"bar baz\" -quickstart");
    }

    [Fact]
    public void ContainsArgumentRequiresStandaloneArgument()
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
