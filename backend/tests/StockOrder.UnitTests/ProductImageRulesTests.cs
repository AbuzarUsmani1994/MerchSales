using Shouldly;
using StockOrder.Modules.Catalog.Application;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>Uploaded pictures are recognised by their first bytes, never by the client's file name or type.</summary>
public sealed class ProductImageRulesTests
{
    [Fact]
    public void Recognises_jpeg_png_and_webp()
    {
        ProductImageRules.DetectContentType([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]).ShouldBe("image/jpeg");
        ProductImageRules.DetectContentType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]).ShouldBe("image/png");
        ProductImageRules.DetectContentType("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()).ShouldBe("image/webp");
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("GIF89a")]
    [InlineData("%PDF-1.7")]
    [InlineData("")]
    public void Refuses_anything_else(string content) =>
        ProductImageRules.DetectContentType(System.Text.Encoding.ASCII.GetBytes(content)).ShouldBeNull();
}
