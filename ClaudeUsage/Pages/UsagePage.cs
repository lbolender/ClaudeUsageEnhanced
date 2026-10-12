using System.Globalization;
using System.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage.Pages;

internal sealed class UsagePage : ContentPage
{
    private readonly UsageClient _client;

    public UsagePage(UsageClient client)
    {
        _client = client;
        Id = "claudeusage.page.usage";
        Name = "Claude Usage";
        Title = "Claude usage stats";
        Icon = IconHelpers.FromRelativePath("Assets\\icons\\claude-filled.svg");
    }

    public override IContent[] GetContent()
    {
        UsageResult result;
        try
        {
            result = _client.GetAsync(force: false).Result;
        }
        catch
        {
            result = new UsageResult(UsageFailureReason.ParseError);
        }

        return [new MarkdownContent(BuildMarkdown(result)), new RefreshForm(this)];
    }

    // Fetch in the background so the button returns immediately; the host re-calls GetContent afterwards
    private void RefreshNow() => _ = Task.Run(async () =>
    {
        try
        {
            await _client.GetAsync(force: true);
        }
        catch { /* GetContent reports the failure */ }
        RaiseItemsChanged(0);
    });

    private sealed class RefreshForm : FormContent
    {
        private readonly UsagePage _page;

        public RefreshForm(UsagePage page)
        {
            _page = page;
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
            """;
            DataJson = "{}";
        }

        public override ICommandResult SubmitForm(string payload, string state)
        {
            _page.RefreshNow();
            return CommandResult.KeepOpen();
        }
    }

    private static string DescribeFailure(UsageResult result)
    {
        var retry = result.RetryAt != null ? $" Next attempt at {result.RetryAt.Value.ToLocalTime():HH:mm}." : "";
        return result.Reason switch
        {
            UsageFailureReason.NoCredentials => "No credentials found. Sign in to Claude Code to start tracking usage.",
            UsageFailureReason.TokenExpired => "Login token expired. Open Claude Code (CLI or VS Code extension) once to refresh it — the value will update automatically.",
            UsageFailureReason.HttpError when result.HttpStatusCode == 429 => $"Rate limited by the API.{retry}",
            UsageFailureReason.HttpError => $"API error {result.HttpStatusCode}.{retry}",
            UsageFailureReason.NetworkError => $"Network error. Please check your connection.{retry}",
            UsageFailureReason.ParseError => $"Failed to parse usage data.{retry}",
            _ => "Failed to fetch usage data."
        };
    }

    private string BuildMarkdown(UsageResult result)
    {
        var snapshot = result.Snapshot;
        if (snapshot == null)
        {
            return $"# Claude usage\n\n{DescribeFailure(result)}";
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Claude usage");
        sb.AppendLine();

        if (result.IsStale)
        {
            sb.AppendLine($"> ⚠ Showing data from {snapshot.FetchedAt.ToLocalTime():HH:mm} — {DescribeFailure(result)}");
            sb.AppendLine();
        }

        var sessionResets = $"session resets {FormatReset(snapshot.SessionResetsAt, "HH:mm")}";
        sb.AppendLine(snapshot.Plan != null ? $"{snapshot.Plan} · {sessionResets}" : sessionResets);
        sb.AppendLine();

        // Session limit
        sb.AppendLine("## Session (5h)");
        var sessionPercent = (int)Math.Round(snapshot.SessionLeftPercent);
        var sessionBar = BuildUnicodeBar(100 - sessionPercent, 24);
        sb.AppendLine($"{sessionBar} {sessionPercent}% left");
        sb.AppendLine();

        // Weekly limits
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

        sb.AppendLine($"Last updated {snapshot.FetchedAt.ToLocalTime():HH:mm}");

        return sb.ToString();
    }

    private static string FormatReset(DateTime? resetsAtUtc, string format) =>
        resetsAtUtc?.ToLocalTime().ToString(format, CultureInfo.InvariantCulture) ?? "—";

    private string BuildUnicodeBar(int used, int width)
    {
        const char UsedChar = '█';
        const char FreeChar = '░';
        var usedCount = Math.Max(0, Math.Min(width, (used * width) / 100));
        var freeCount = width - usedCount;
        return new string(UsedChar, usedCount) + new string(FreeChar, freeCount);
    }
}
