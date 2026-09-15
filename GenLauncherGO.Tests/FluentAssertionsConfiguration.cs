using FluentAssertions.Extensibility;

// Acknowledge the Fluent Assertions 8 license before its first assertion to suppress its per-run reminder.
[assembly: AssertionEngineInitializer(
    typeof(GenLauncherGO.Tests.FluentAssertionsConfiguration),
    nameof(GenLauncherGO.Tests.FluentAssertionsConfiguration.AcceptLicense))]

namespace GenLauncherGO.Tests;

internal static class FluentAssertionsConfiguration
{
    public static void AcceptLicense()
    {
        License.Accepted = true;
    }
}
