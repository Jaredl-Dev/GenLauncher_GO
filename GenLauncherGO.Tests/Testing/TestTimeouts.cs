using System;

namespace GenLauncherGO.Tests.Testing;

internal static class TestTimeouts
{
    /// <summary>
    ///     How long a test waits for asynchronous work before failing it.
    /// </summary>
    /// <remarks>
    ///     Long enough to absorb scheduling jitter on a loaded agent, short enough that a genuine deadlock fails the
    ///     run instead of hanging it. Shared so the budget is one edit rather than one per await.
    /// </remarks>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
}
