using Application.Common.Security;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del LogSanitizer (seguridad de logs): neutraliza caracteres de inyeccion
/// (controles ASCII, C1 y separadores de linea Unicode) y aplica truncado y trim.
/// </summary>
public class LogSanitizerTests
{
    private readonly LogSanitizer _sut = new();

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("\u001B")]
    public void RemovesAsciiControlCharacters(string control)
    {
        var result = _sut.Sanitize("a" + control + "b");
        Assert.Equal("a b", result);
    }

    [Theory]
    [InlineData('\u0085')]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    public void RemovesUnicodeLineSeparators(char newline)
    {
        var result = _sut.Sanitize("Mozilla/5.0" + newline + "INJECTED");
        Assert.False(result.Contains(newline), "el separador Unicode debe eliminarse");
    }

    [Fact]
    public void PreservesLegitimateUserAgent()
    {
        const string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
        Assert.Equal(ua, _sut.Sanitize(ua));
    }

    [Fact]
    public void TruncatesToMaxLength()
    {
        var result = _sut.Sanitize(new string('x', 1000), maxLength: 512);
        Assert.Equal(512, result.Length);
    }

    [Fact]
    public void TrimsWhitespace()
    {
        Assert.Equal("hello", _sut.Sanitize("   hello   "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsEmpty_ForNullOrWhitespace(string? input)
    {
        Assert.Equal(string.Empty, _sut.Sanitize(input));
    }
}
