using System;
using System.Globalization;
using GenLauncherGO.UI.Shared.Formatting;

namespace GenLauncherGO.Tests.UI.Shared.Formatting;

public sealed class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741823, "1024 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(6012954214, "5.6 GB")]
    public void Format_UsesCompactBinaryUnits(long bytes, string expected)
    {
        using CultureScope culture = new("en-US");

        string formatted = ByteSizeFormatter.Format(bytes);

        formatted.Should().Be(expected);
    }

    [Fact]
    public void Format_UsesCurrentCultureDecimalSeparator()
    {
        using CultureScope culture = new("de-DE");

        string formatted = ByteSizeFormatter.Format(1_625_292_800);

        formatted.Should().Be("1,5 GB");
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

        public CultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
        }
    }
}
