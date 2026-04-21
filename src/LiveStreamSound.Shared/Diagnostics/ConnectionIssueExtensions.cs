using LiveStreamSound.Shared.Localization;

namespace LiveStreamSound.Shared.Diagnostics;

public static class ConnectionIssueExtensions
{
    public static string TitleKey(this ConnectionIssue issue) =>
        issue == ConnectionIssue.None ? "Issue.None" : $"Issue.{issue}.Title";

    public static string BodyKey(this ConnectionIssue issue) =>
        issue == ConnectionIssue.None ? "Issue.None" : $"Issue.{issue}.Body";

    public static string Title(this ConnectionIssue issue) => Loc.Instance.Get(issue.TitleKey());
    public static string Body(this ConnectionIssue issue) => Loc.Instance.Get(issue.BodyKey());

    public static string LocalizedLabel(this QualityLevel level) => level switch
    {
        QualityLevel.Good => Loc.Instance.Get("Quality.Good"),
        QualityLevel.Degraded => Loc.Instance.Get("Quality.Degraded"),
        QualityLevel.Bad => Loc.Instance.Get("Quality.Bad"),
        QualityLevel.Disconnected => Loc.Instance.Get("Quality.Disconnected"),
        _ => level.ToString(),
    };
}
