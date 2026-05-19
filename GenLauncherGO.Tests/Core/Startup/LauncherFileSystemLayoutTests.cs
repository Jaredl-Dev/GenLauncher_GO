using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Core.Startup;

public sealed class LauncherFileSystemLayoutTests
{
    [Fact]
    public void GetBuiltInGameExecutableNames_ForZeroHour_ListsOnlineThenCommunityThenRetail()
    {
        IReadOnlyList<string> executableNames =
            LauncherFileSystemLayout.GetBuiltInGameExecutableNames(SupportedGame.ZeroHour);

        executableNames.Should().Equal("generalsonlinezh.exe", "generalszh.exe", "generals.exe");
    }

    [Fact]
    public void GetBuiltInGameExecutableNames_ForGenerals_ListsCommunityThenRetail()
    {
        IReadOnlyList<string> executableNames =
            LauncherFileSystemLayout.GetBuiltInGameExecutableNames(SupportedGame.Generals);

        executableNames.Should().Equal("generalsv.exe", "generals.exe");
    }

    [Fact]
    public void GetBuiltInWorldBuilderExecutableNames_ForZeroHour_ListsRetailThenCommunity()
    {
        IReadOnlyList<string> executableNames =
            LauncherFileSystemLayout.GetBuiltInWorldBuilderExecutableNames(SupportedGame.ZeroHour);

        executableNames.Should().Equal("WorldBuilder.exe", "worldbuilderzh.exe");
    }

    [Fact]
    public void GetBuiltInWorldBuilderExecutableNames_ForGenerals_ListsRetailThenCommunity()
    {
        IReadOnlyList<string> executableNames =
            LauncherFileSystemLayout.GetBuiltInWorldBuilderExecutableNames(SupportedGame.Generals);

        executableNames.Should().Equal("WorldBuilder.exe", "worldbuilderv.exe");
    }

    [Theory]
    [InlineData("  custom.exe  ", "custom.exe")]
    [InlineData("Custom.EXE", "Custom.EXE")]
    public void NormalizeExecutableFileName_TrimsAndKeepsAnyCaseExeExtension(
        string executableName,
        string expectedExecutableName)
    {
        string normalizedName = LauncherFileSystemLayout.NormalizeExecutableFileName(executableName);

        normalizedName.Should().Be(expectedExecutableName);
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("custom.txt")]
    [InlineData("custom.exe.txt")]
    public void NormalizeExecutableFileName_WithoutExeExtension_Throws(string executableName)
    {
        Action act = () => LauncherFileSystemLayout.NormalizeExecutableFileName(executableName);

        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(executableName));
    }

    [Theory]
    [InlineData(@"tools\custom.exe")]
    [InlineData("tools/custom.exe")]
    [InlineData(@"..\custom.exe")]
    [InlineData(@"C:\tools\custom.exe")]
    [InlineData("CON.exe")]
    [InlineData("")]
    public void NormalizeExecutableFileName_WithUnsafeRootLevelName_Throws(string executableName)
    {
        Action act = () => LauncherFileSystemLayout.NormalizeExecutableFileName(executableName);

        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(executableName));
    }
}
