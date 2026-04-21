using LiveStreamSound.Shared.Session;

namespace LiveStreamSound.Shared.Tests;

public class SessionCodeTests
{
    [Fact]
    public void Generate_Returns_SixDigitsNumeric()
    {
        for (var i = 0; i < 500; i++)
        {
            var code = SessionCode.Generate();
            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.InRange(c, '0', '9'));
        }
    }

    [Fact]
    public void Generate_ProducesReasonablyDistinctCodes()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 2000; i++) seen.Add(SessionCode.Generate());
        // 2000 samples out of 1_000_000 — birthday paradox gives ~expected ≈ 2
        // but we still expect well over 1900 distinct.
        Assert.True(seen.Count > 1950, $"Only {seen.Count} unique of 2000");
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("000000", true)]
    [InlineData("999999", true)]
    [InlineData("12345", false)]   // too short
    [InlineData("1234567", false)] // too long
    [InlineData("12345a", false)]  // letter
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidFormat_Matches(string? input, bool expected) =>
        Assert.Equal(expected, SessionCode.IsValidFormat(input));
}
