using System.Text.RegularExpressions;

namespace GenLauncherGO.TestAnalyzers;

internal static class TestMethodNameConvention
{
    private static readonly Regex _validName = new(
        "^[A-Z][A-Za-z0-9]*_[A-Z][A-Za-z0-9]*(?:_[A-Z][A-Za-z0-9]*)?$",
        RegexOptions.CultureInvariant);

    public static bool IsValid(string name)
    {
        return _validName.IsMatch(name);
    }
}
