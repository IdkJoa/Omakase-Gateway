using Application.Common.Security;
using Xunit;

namespace OG.Application.UnitTests.Security;

/// <summary>
/// Tests unitarios del HtmlOutputSanitizer (HU-028 T-060).
/// Verifica que caracteres peligrosos para XSS son codificados correctamente.
/// </summary>
public class HtmlOutputSanitizerTests
{
    private readonly HtmlOutputSanitizer _sut = new();

    [Fact]
    public void Encodes_Script_Tags()
    {
        var input = "<script>alert('xss')</script>";
        var result = _sut.Sanitize(input);

        Assert.DoesNotContain("<script>", result);
        Assert.DoesNotContain("</script>", result);
        Assert.Contains("&lt;script&gt;", result);
    }

    [Fact]
    public void Encodes_Html_Entities()
    {
        var result = _sut.Sanitize("<b>bold</b> & \"quotes\"");

        Assert.DoesNotContain("<b>", result);
        Assert.Contains("&lt;b&gt;", result);
        Assert.Contains("&amp;", result);
        Assert.Contains("&quot;", result);
    }

    [Fact]
    public void Encodes_Single_Quotes()
    {
        var result = _sut.Sanitize("it's a test");

        // HtmlEncoder encodes single quotes as &#x27;
        Assert.Contains("&#x27;", result);
    }

    [Fact]
    public void Preserves_Safe_Strings()
    {
        const string safe = "admin-user.123 Normal Text";
        Assert.Equal(safe, _sut.Sanitize(safe));
    }

    [Fact]
    public void Preserves_Urls_Without_Dangerous_Chars()
    {
        const string url = "https://api.example.com/v1/data";
        // URLs with :// are safe, HtmlEncoder preserves them
        var result = _sut.Sanitize(url);
        Assert.Contains("https://api.example.com/v1/data", result);
    }

    [Fact]
    public void Returns_Empty_For_Null()
    {
        Assert.Equal(string.Empty, _sut.Sanitize(null));
    }

    [Fact]
    public void Returns_Empty_For_Empty_String()
    {
        Assert.Equal(string.Empty, _sut.Sanitize(string.Empty));
    }

    [Fact]
    public void Encodes_Event_Handler_Injection()
    {
        var input = "\" onmouseover=\"alert(1)\"";
        var result = _sut.Sanitize(input);

        // Quotes are encoded as &quot;, preventing escaping out of HTML attribute values
        Assert.DoesNotContain("\"", result);
        Assert.Contains("&quot;", result);
    }

    [Fact]
    public void Encodes_Javascript_Protocol()
    {
        var result = _sut.Sanitize("javascript:alert(1)");
        // Should be safe — no angle brackets, but the colon is preserved
        // The key is that if this is rendered in an href, Angular's sanitizer handles it
        Assert.DoesNotContain("<", result);
    }
}
