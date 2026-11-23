using Ake.Core.Model;

namespace Ake.Core.Tests.Model;

public class ReferenceDesignationTests
{
    [Theory]
    [InlineData("=A1+S1-K1", "A1", "S1", "K1", null)]
    [InlineData("-K1", null, null, "K1", null)]
    [InlineData("+S1-X1:3", null, "S1", "X1", "3")]
    [InlineData("=A1.M1+P1-K1:A1", "A1.M1", "P1", "K1", "A1")]
    [InlineData("-T1:+", null, null, "T1", "+")]
    [InlineData("-T1:-", null, null, "T1", "-")]
    [InlineData("=A1", "A1", null, null, null)]
    public void Parse_reads_each_aspect(string text, string? function, string? location, string? product, string? point)
    {
        var result = ReferenceDesignation.Parse(text);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(new ReferenceDesignation(function, location, product, point), result.Value);
    }

    [Theory]
    [InlineData("=A1+S1-K1")]
    [InlineData("-K1:A1")]
    [InlineData("+S1-X1:3")]
    [InlineData("=A1.M1+P1-K1")]
    public void Parse_then_ToString_returns_the_same_text(string text)
    {
        Assert.Equal(text, ReferenceDesignation.Parse(text).Value.ToString());
    }

    [Theory]
    [InlineData("K1")]
    [InlineData("+S1=A1-K1")]
    [InlineData("-K 1")]
    [InlineData("=+S1")]
    public void Parse_rejects_malformed_tags(string text)
    {
        var result = ReferenceDesignation.Parse(text);

        Assert.True(result.IsFailure);
        Assert.Equal("tag.gecersiz", result.Error!.Code);
    }

    [Fact]
    public void Parse_rejects_empty_text()
    {
        Assert.Equal("tag.bos", ReferenceDesignation.Parse("  ").Error!.Code);
    }
}
