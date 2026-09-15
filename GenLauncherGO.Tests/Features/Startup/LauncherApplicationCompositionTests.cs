using GenLauncherGO.Features.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace GenLauncherGO.Tests.Features.Startup;

public sealed class LauncherApplicationCompositionTests
{
    [Fact]
    public void CreateServiceCollection_BuildsCompleteValidatedApplicationGraph()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        LauncherRuntimeContext runtimeContext = new(
            TestLauncherPaths.CreateRuntimePathContext(paths),
            "1.0.0-test");
        ILauncherPathResolver pathResolver = Substitute.For<ILauncherPathResolver>();
        ILauncherHostEnvironmentService hostEnvironmentService =
            Substitute.For<ILauncherHostEnvironmentService>();

        ServiceCollection services = LauncherApplicationHost.CreateServiceCollection(
            runtimeContext,
            pathResolver,
            hostEnvironmentService,
            new FakeStringLocalizer());

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        provider.GetRequiredService<LauncherRuntimeContext>().Should().BeSameAs(runtimeContext);
        provider.GetRequiredService<IStartupDialogService>().Should().BeOfType<AvaloniaStartupDialogService>();
    }
}
