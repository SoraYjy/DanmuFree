using DanmuFree.Core;
namespace DanmuFree.Tests;

public class NumberFormatterTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(10000, "1万")]
    [InlineData(12345, "1.2万")]
    [InlineData(99999, "10万")]
    [InlineData(100000000, "1亿")]
    [InlineData(123456789, "1.2亿")]
    public void Format_various(int n, string expected) => Assert.Equal(expected, NumberFormatter.Format(n));
}
