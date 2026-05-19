namespace GenLauncherGO.Tests.Testing;

public sealed class SymbolicLinkFactAttribute : FactAttribute
{
    public SymbolicLinkFactAttribute()
    {
        if (!SymbolicLinkTestSupport.IsRequired &&
            !SymbolicLinkTestSupport.IsSupported)
        {
            Skip = SymbolicLinkTestSupport.UnsupportedReason;
        }
    }
}
