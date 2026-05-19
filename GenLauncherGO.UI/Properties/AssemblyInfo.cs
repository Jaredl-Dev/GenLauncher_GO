using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: ComVisible(false)]
// NSubstitute's Castle proxy assembly implements internal UI boundaries exercised by the test project.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
[assembly: InternalsVisibleTo("GenLauncherGO.Tests")]
[assembly: SupportedOSPlatform("windows7.0")]
