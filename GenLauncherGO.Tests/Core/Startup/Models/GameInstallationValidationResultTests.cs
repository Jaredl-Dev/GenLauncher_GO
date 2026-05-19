using System;
using GenLauncherGO.Core.Startup.Models;

namespace GenLauncherGO.Tests.Core.Startup.Models;

public sealed class GameInstallationValidationResultTests
{
    [Fact]
    public void Invalid_WithoutAFailure_Throws()
    {
        Action act = () => GameInstallationValidationResult.Invalid(
            GameInstallationValidationFailure.None);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("failure");
    }
}
