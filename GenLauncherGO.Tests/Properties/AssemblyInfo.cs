using System.Runtime.InteropServices;

// Matches the production assemblies: the test P/Invokes target kernel32 only, so
// resolution is restricted to System32 rather than searching the test output
// directory first.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
