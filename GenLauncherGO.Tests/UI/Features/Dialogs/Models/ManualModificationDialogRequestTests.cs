using GenLauncherGO.UI.Features.Dialogs.Models;

namespace GenLauncherGO.Tests.UI.Features.Dialogs.Models;

public sealed class ManualModificationDialogRequestTests
{
    [Fact]
    public void ConstructorCopiesSelectedFilesAndParentContentName()
    {
        string[] files = { @"C:\Packages\mod.zip" };

        ManualModificationDialogRequest request = new(files, "ShockWave");
        files[0] = @"C:\Packages\changed.zip";

        request.Files.Should().Equal(@"C:\Packages\mod.zip");
        request.ParentContentName.Should().Be("ShockWave");
    }
}
