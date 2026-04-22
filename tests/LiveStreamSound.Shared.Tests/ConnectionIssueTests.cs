using LiveStreamSound.Shared.Diagnostics;
using LiveStreamSound.Shared.Localization;

namespace LiveStreamSound.Shared.Tests;

/// <summary>
/// The ConnectionIssue enum has a 1:1 relationship with Loc keys — every issue
/// variant MUST have a localized Title and Body in both languages, otherwise
/// the UI falls back to showing the raw key which is a terrible user experience
/// for the teacher during an exam.
/// </summary>
public class ConnectionIssueTests
{
    public static IEnumerable<object[]> NonNoneIssues =>
        Enum.GetValues<ConnectionIssue>()
            .Where(i => i != ConnectionIssue.None)
            .Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(NonNoneIssues))]
    public void EveryIssue_HasLocalizedTitle_InBothLanguages(ConnectionIssue issue)
    {
        var prev = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = Loc.Lang.German;
            var titleDe = issue.Title();
            Loc.Instance.Language = Loc.Lang.English;
            var titleEn = issue.Title();

            Assert.False(string.IsNullOrWhiteSpace(titleDe),
                $"DE title missing for {issue}");
            Assert.False(string.IsNullOrWhiteSpace(titleEn),
                $"EN title missing for {issue}");
            Assert.NotEqual(issue.TitleKey(), titleDe); // translated, not a raw key
            Assert.NotEqual(issue.TitleKey(), titleEn);
        }
        finally { Loc.Instance.Language = prev; }
    }

    [Theory]
    [MemberData(nameof(NonNoneIssues))]
    public void EveryIssue_HasLocalizedBody_InBothLanguages(ConnectionIssue issue)
    {
        var prev = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = Loc.Lang.German;
            var bodyDe = issue.Body();
            Loc.Instance.Language = Loc.Lang.English;
            var bodyEn = issue.Body();

            Assert.False(string.IsNullOrWhiteSpace(bodyDe),
                $"DE body missing for {issue}");
            Assert.False(string.IsNullOrWhiteSpace(bodyEn),
                $"EN body missing for {issue}");
            // Body text should be meaningfully long (explanation, not just a label).
            Assert.True(bodyDe.Length > 20, $"DE body too short for {issue}: '{bodyDe}'");
            Assert.True(bodyEn.Length > 20, $"EN body too short for {issue}: '{bodyEn}'");
        }
        finally { Loc.Instance.Language = prev; }
    }

    [Fact]
    public void TitleKey_Pattern_MatchesConvention()
    {
        Assert.Equal("Issue.HighLatency.Title",
            ConnectionIssue.HighLatency.TitleKey());
        Assert.Equal("Issue.None",
            ConnectionIssue.None.TitleKey());
    }

    [Fact]
    public void BodyKey_Pattern_MatchesConvention()
    {
        Assert.Equal("Issue.PacketLoss.Body",
            ConnectionIssue.PacketLoss.BodyKey());
        Assert.Equal("Issue.None",
            ConnectionIssue.None.BodyKey());
    }

    [Theory]
    [InlineData(QualityLevel.Good)]
    [InlineData(QualityLevel.Degraded)]
    [InlineData(QualityLevel.Bad)]
    [InlineData(QualityLevel.Disconnected)]
    public void LocalizedLabel_ResolvesInBothLanguages(QualityLevel level)
    {
        var prev = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = Loc.Lang.German;
            var de = level.LocalizedLabel();
            Loc.Instance.Language = Loc.Lang.English;
            var en = level.LocalizedLabel();

            Assert.False(string.IsNullOrWhiteSpace(de));
            Assert.False(string.IsNullOrWhiteSpace(en));
            // Expect DE and EN to differ (at least for most quality levels).
            if (level != QualityLevel.Good) // "Good"/"Gut" differ
                Assert.NotEqual(de, en);
        }
        finally { Loc.Instance.Language = prev; }
    }

    [Fact]
    public void QualityLevel_SeverityOrdering_StrictlyAscending()
    {
        // The enum values are used for severity comparisons elsewhere.
        Assert.True((int)QualityLevel.Disconnected < (int)QualityLevel.Bad);
        Assert.True((int)QualityLevel.Bad < (int)QualityLevel.Degraded);
        Assert.True((int)QualityLevel.Degraded < (int)QualityLevel.Good);
    }

    [Fact]
    public void ConnectionQuality_Record_EqualityWorks()
    {
        var a = new ConnectionQuality(10, 0.1f, 2, 80, Array.Empty<ConnectionIssue>(), QualityLevel.Good);
        var b = new ConnectionQuality(10, 0.1f, 2, 80, Array.Empty<ConnectionIssue>(), QualityLevel.Good);
        Assert.Equal(a, b);
    }
}
