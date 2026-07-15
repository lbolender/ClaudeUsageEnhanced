using System.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage.Pages;

internal sealed class UsagePage : ContentPage
{
    private readonly UsageClient _client = new();

    public UsagePage()
    {
        Id = "claudeusage.page.usage";
        Name = "Claude Usage";
        Title = "Claude usage stats";
        Icon = IconHelpers.FromRelativePath("Assets\\icons\\claude-filled.svg");
    }

    public override IContent[] GetContent()
    {
        UsageSnapshot? snapshot = null;
        UsageFailureReason failureReason = UsageFailureReason.Ok;
        int? httpStatus = null;
        try
        {
            var result = _client.GetAsync(force: false).Result;
            snapshot = result.Snapshot;
            failureReason = result.Reason;
            httpStatus = result.HttpStatusCode;
        }
        catch { }

        var md = new MarkdownContent(BuildMarkdown(snapshot, failureReason, httpStatus));
        var form = new FormContent
        {
            TemplateJson = """
            {
              "type": "AdaptiveCard",
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "version": "1.5",
              "body": [
                { "type": "TextBlock", "text": "Refresh usage data", "wrap": true }
              ],
              "actions": [
                { "type": "Action.Submit", "title": "Refresh now", "data": { "action": "refresh" } }
              ]
            }
            """,
            DataJson = "{}",
        };

        return [md, form];
    }

    private string BuildMarkdown(UsageSnapshot? snapshot, UsageFailureReason failureReason, int? httpStatus)
    {
        if (snapshot == null)
        {
            var errorMsg = failureReason switch
            {
                UsageFailureReason.NoCredentials => "No credentials found. Sign in to Claude Code to start tracking usage.",
                UsageFailureReason.HttpError => $"API error {httpStatus}. Please try again later.",
                UsageFailureReason.NetworkError => "Network error. Please check your connection.",
                UsageFailureReason.ParseError => "Failed to parse usage data. Please try again later.",
                _ => "Failed to fetch usage data."
            };
            return $"# Claude usage\n\n{errorMsg}";
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Claude usage");
        sb.AppendLine();

        var sessionResets = snapshot.SessionResetsAt.ToLocalTime();
        sb.AppendLine($"Max 5× · session resets {sessionResets:HH:mm}");
        sb.AppendLine();

        // Session limit
        sb.AppendLine("## Session (5h)");
        var sessionPercent = (int)Math.Round(snapshot.SessionLeftPercent);
        var sessionBar = BuildUnicodeBar(100 - sessionPercent, 24);
        sb.AppendLine($"{sessionBar} {sessionPercent}% left");
        sb.AppendLine();

        // Weekly limits
        var weeklyResets = snapshot.WeeklyResetsAt.ToLocalTime();
        var weeklyPercent = (int)Math.Round(snapshot.WeeklyLeftPercent);
        sb.AppendLine("## Week (all models)");
        var weeklyBar = BuildUnicodeBar(100 - weeklyPercent, 24);
        sb.AppendLine($"{weeklyBar} {weeklyPercent}% left");
        sb.AppendLine();

        // Per-model limits
        var scopedLimits = snapshot.Limits.Where(l => l.Kind == "weekly_scoped").ToList();
        if (scopedLimits.Count > 0)
        {
            foreach (var limit in scopedLimits)
            {
                if (!string.IsNullOrEmpty(limit.ModelDisplayName))
                {
                    sb.AppendLine($"## Week — {limit.ModelDisplayName}");
                    var used = (int)Math.Round(limit.PercentUsed);
                    var left = 100 - used;
                    var bar = BuildUnicodeBar(used, 24);
                    sb.AppendLine($"{bar} {left}% left");
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine($"Last updated {DateTime.Now:HH:mm}");

        return sb.ToString();
    }

    private string BuildUnicodeBar(int used, int width)
    {
        const char UsedChar = '█';
        const char FreeChar = '░';
        var usedCount = Math.Max(0, Math.Min(width, (used * width) / 100));
        var freeCount = width - usedCount;
        return new string(UsedChar, usedCount) + new string(FreeChar, freeCount);
    }
}
