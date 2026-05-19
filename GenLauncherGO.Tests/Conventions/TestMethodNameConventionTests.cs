using GenLauncherGO.TestAnalyzers;

namespace GenLauncherGO.Tests.Conventions;

public sealed class TestMethodNameConventionTests
{
    [Theory]
    [InlineData("Member_ExpectedOutcome")]
    [InlineData("Member_Scenario_ExpectedOutcome")]
    [InlineData("Http2_UsesVersion2")]
    public void IsValid_WithBehaviorOrScenarioName_ReturnsTrue(string name)
    {
        TestMethodNameConvention.IsValid(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("Member")]
    [InlineData("member_ExpectedOutcome")]
    [InlineData("Member_expectedOutcome")]
    [InlineData("Member__ExpectedOutcome")]
    [InlineData("Member_Scenario_ExpectedOutcome_Extra")]
    public void IsValid_WithMalformedName_ReturnsFalse(string name)
    {
        TestMethodNameConvention.IsValid(name).Should().BeFalse();
    }
}
