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
    public void Generate_LeadingZeros_Preserved()
    {
        // Over many iterations we should occasionally see a code whose decimal
        // value is small enough to need left-padding. The format string "D6"
        // is what ensures "042" doesn't come out as "42".
        var sawLeadingZero = false;
        for (var i = 0; i < 10_000 && !sawLeadingZero; i++)
        {
            var code = SessionCode.Generate();
            if (code[0] == '0') sawLeadingZero = true;
        }
        Assert.True(sawLeadingZero, "Expected at least one code with a leading zero in 10k samples");
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

    [Fact]
    public void Generate_AllDigitsAppear()
    {
        // Statistical smoke test: across many generations each decimal digit
        // should show up. Any systematic bias (e.g. off-by-one in modulo) would
        // skew the distribution visibly.
        var counts = new int[10];
        for (var i = 0; i < 5_000; i++)
        {
            var code = SessionCode.Generate();
            foreach (var c in code) counts[c - '0']++;
        }
        // 5_000 codes × 6 digits = 30_000 samples; expected 3000 per digit.
        // Allow wide tolerance (within 50% of uniform).
        Assert.All(counts, n => Assert.InRange(n, 1500, 4500));
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("000000", true)]
    [InlineData("999999", true)]
    [InlineData("12345", false)]   // too short
    [InlineData("1234567", false)] // too long
    [InlineData("12345a", false)]  // letter
    [InlineData("abcdef", false)]  // all letters
    [InlineData("12345 ", false)]  // trailing space
    [InlineData(" 12345", false)]  // leading space
    [InlineData("12-45x", false)]  // punctuation
    [InlineData("1 3456", false)]  // internal space
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidFormat_Matches(string? input, bool expected) =>
        Assert.Equal(expected, SessionCode.IsValidFormat(input));

    [Fact]
    public void Digits_Constant_IsSix()
    {
        // If someone ever changes this we want the test suite screaming.
        Assert.Equal(6, SessionCode.Digits);
    }

    [Fact]
    public void Generate_ResultIsAlwaysInValidFormat()
    {
        for (var i = 0; i < 200; i++)
            Assert.True(SessionCode.IsValidFormat(SessionCode.Generate()));
    }
}
