using System.Text.Json;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage.Pages;

/// <summary>
/// Stats page rendered as a single styled AdaptiveCard (FormContent).
/// Visual hierarchy comes from size/weight/ColumnSet alignment plus colored
/// emoji (TextBlock.color does not render in CmdPal's host, so we avoid it).
/// The card's "Refresh now" action is wired via <see cref="UsageFormContent"/>.
/// </summary>
internal sealed class UsagePage : ContentPage
{
    private readonly UsageClient _client = new();
    private UsageFormContent? _form;

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

        var form = _form ??= new UsageFormContent(BuildCardJson(snapshot, failureReason, httpStatus), Refresh);
        form.DataJson = "{}";
        return [form];
    }

    /// <summary>
    /// Invoked by the card's "Refresh now" submit action. Forces a fresh
    /// fetch off the calling thread, then pushes an updated card template
    /// and raises ItemsChanged so the host re-renders (belt and suspenders:
    /// covers both the PropChange path and the GetContent re-call path).
    /// </summary>
    internal void Refresh()
    {
        _ = Task.Run(async () =>
        {
            UsageResult result;
            try
            {
                result = await _client.GetAsync(force: true);
            }
            catch
            {
                result = new UsageResult(UsageFailureReason.NetworkError);
            }

            var form = _form;
            if (form != null)
            {
                form.TemplateJson = BuildCardJson(result.Snapshot, result.Reason, result.HttpStatusCode);
            }
            RaiseItemsChanged(0);
        });
    }

    // ── AdaptiveCard builder ──────────────────────────────────────────────

    private string BuildCardJson(UsageSnapshot? snapshot, UsageFailureReason failureReason, int? httpStatus)
    {
        var body = new List<object>();

        body.Add(TextBlock("Claude usage", size: "Large", weight: "Bolder"));

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
            body.Add(TextBlock(errorMsg, spacing: "small", subtle: true));
        }
        else
        {
            var sessionLeft = (int)Math.Round(snapshot.SessionLeftPercent);
            var weeklyLeft = (int)Math.Round(snapshot.WeeklyLeftPercent);
            var sessionReset = snapshot.SessionResetsAt.ToLocalTime();
            var weeklyReset = snapshot.WeeklyResetsAt.ToLocalTime();

            body.AddRange(Metric("Session (5h)", left: sessionLeft, bar: BuildUnicodeBar(100 - sessionLeft), spacing: "medium"));
            body.AddRange(Metric("Week (all models)", left: weeklyLeft, bar: BuildUnicodeBar(100 - weeklyLeft), spacing: "medium"));

            foreach (var limit in snapshot.Limits.Where(l => l.Kind == "weekly_scoped"))
            {
                if (string.IsNullOrEmpty(limit.ModelDisplayName)) continue;
                var used = (int)Math.Round(limit.PercentUsed);
                var left = 100 - used;
                body.AddRange(Metric($"Week — {limit.ModelDisplayName}", left: left, bar: BuildUnicodeBar(used), spacing: "medium"));
            }

            body.Add(TextBlock($"Session resets {sessionReset:HH:mm} · Week resets {weeklyReset:ddd HH:mm} · Updated {DateTime.Now:HH:mm}", spacing: "large", subtle: true));
        }

        var root = new Dictionary<string, object?>
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "Action.Submit",
                    ["title"] = "Refresh now",
                    ["data"] = new Dictionary<string, object?> { ["action"] = "refresh" }
                }
            }
        };

        return JsonSerializer.Serialize(root);
    }

    /// <summary>A metric = label on one line, then the bar + percentage on one adjacent line.</summary>
    private IEnumerable<object> Metric(string label, int left, string bar, string spacing)
    {
        yield return TextBlock(label, weight: "Bolder", spacing: spacing);
        yield return TextBlock($"{StatusEmoji(left)}  {bar}  {left}% left", size: "Medium", fontType: "Monospace", spacing: "small");
    }

    private static string StatusEmoji(int leftPercent) => leftPercent switch
    {
        >= 50 => "🟢",
        >= 20 => "🟡",
        _ => "🔴"
    };

    private static Dictionary<string, object?> TextBlock(string text,
        string? size = null, string? weight = null, string? align = null,
        string? spacing = null, string? fontType = null, bool subtle = false)
    {
        var d = new Dictionary<string, object?>
        {
            ["type"] = "TextBlock",
            ["text"] = text,
            ["wrap"] = true
        };
        if (size != null) d["size"] = size;
        if (weight != null) d["weight"] = weight;
        if (align != null) d["horizontalAlignment"] = align;
        if (spacing != null) d["spacing"] = spacing;
        if (fontType != null) d["fontType"] = fontType;
        if (subtle) d["isSubtle"] = true;
        return d;
    }

    private static string BuildUnicodeBar(int used, int width = 24)
    {
        const char UsedChar = '█';
        const char FreeChar = '░';
        var usedCount = Math.Max(0, Math.Min(width, (used * width) / 100));
        var freeCount = width - usedCount;
        return new string(UsedChar, usedCount) + new string(FreeChar, freeCount);
    }
}
