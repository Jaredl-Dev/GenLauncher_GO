using System.Runtime.CompilerServices;

// Lets Core types stay internal when only the test suite needs to reach them, so
// visibility reflects production consumption rather than test access. Matches the
// Infrastructure and UI assemblies.
[assembly: InternalsVisibleTo("GenLauncherGO.Tests")]
