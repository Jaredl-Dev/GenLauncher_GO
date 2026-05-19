using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("GenLauncherGO.Tests")]

// Every P/Invoke in this assembly targets kernel32 or user32, so restricting
// resolution to System32 cannot fail to find them, and it removes the search of
// the application directory that a planted DLL of the same name would exploit.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
