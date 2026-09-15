using System.Runtime.CompilerServices;

namespace GenLauncherGO.Tests.Testing;

public sealed class SymbolicLinkFactAttribute : FactAttribute
{
    public SymbolicLinkFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!SymbolicLinkTestSupport.IsRequired &&
            !SymbolicLinkTestSupport.IsSupported)
        {
            Skip = SymbolicLinkTestSupport.UnsupportedReason;
        }
    }
}
