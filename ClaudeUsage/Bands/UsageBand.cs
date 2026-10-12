using System.Globalization;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using ClaudeUsage.Pages;

namespace ClaudeUsage.Bands;

internal sealed class UsageBand
{
    private readonly UsageClient _client;
    private readonly WrappedDockItem _dockItem;
    private readonly ListItem _sessionItem;
    private readonly ListItem _weeklyItem;

    public WrappedDockItem DockItem => _dockItem;
    public int RefreshSeconds => 30;

    public UsageBand(UsageClient client, UsagePage page)
    {
        _client = client;
        _sessionItem = new ListItem(page);
        _weeklyItem = new ListItem(page);
        _dockItem = new WrappedDockItem([_sessionItem, _weeklyItem], "claudeusage.dock.usage", "Claude Usage");
        _client.Updated += Render;
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            Render(await _client.GetAsync());
        }
        catch
        {
            SetText(_sessionItem, "Claude —", "error fetching usage");
            SetText(_weeklyItem, "—", "error fetching usage");
            ApplyIcons(sessionLow: false, weeklyLow: false);
        }
    }

    private void Render(UsageResult result)
    {
        if (result.Snapshot == null)
        {
            var failure = DescribeFailure(result, verbose: true);
            SetText(_sessionItem, "Claude —", failure);
            SetText(_weeklyItem, "—", failure);
            ApplyIcons(sessionLow: false, weeklyLow: false);
            return;
        }

        var snapshot = result.Snapshot;
        var sessionLeft = (int)Math.Round(snapshot.SessionLeftPercent);
        var weeklyLeft = (int)Math.Round(snapshot.WeeklyLeftPercent);

        if (result.IsStale)
        {
            // Keep the last known values visible, but make clear they are outdated
            var fetchedLocal = snapshot.FetchedAt.ToLocalTime();
            var stale = $"stale {fetchedLocal:HH:mm} · {DescribeFailure(result, verbose: false)}";
            SetText(_sessionItem, $"{sessionLeft}%", stale);
            SetText(_weeklyItem, $"{weeklyLeft}%", stale);
            ApplyIcons(sessionLow: false, weeklyLow: false);
            return;
        }

        var sessionReset = snapshot.SessionResetsAt.ToLocalTime();
        var weeklyReset = snapshot.WeeklyResetsAt.ToLocalTime();

        SetText(_sessionItem, $"{sessionLeft}%", $"5h · resets {sessionReset:HH:mm}");
        SetText(_weeklyItem, $"{weeklyLeft}%",
            $"week · resets {weeklyReset.ToString("ddd HH:mm", CultureInfo.InvariantCulture)}");

        ApplyIcons(sessionLow: sessionLeft < 20, weeklyLow: weeklyLeft < 20);
    }

    private static string DescribeFailure(UsageResult result, bool verbose) => result.Reason switch
    {
        UsageFailureReason.NoCredentials => "sign in to Claude Code",
        UsageFailureReason.TokenExpired => verbose ? "open Claude Code to refresh login" : "open Claude Code",
        UsageFailureReason.HttpError when result.HttpStatusCode == 429 =>
            verbose && result.RetryAt != null ? $"rate limited · retry {result.RetryAt.Value.ToLocalTime():HH:mm}" : "rate limited",
        UsageFailureReason.HttpError => $"API error {result.HttpStatusCode}",
        UsageFailureReason.NetworkError => "offline",
        UsageFailureReason.ParseError => "parse error",
        _ => "error fetching usage"
    };

    // Only assign changed values, so CmdPal is not asked to redraw identical text every tick
    private static void SetText(ListItem item, string title, string subtitle)
    {
        if (item.Title != title)
        {
            item.Title = title;
        }
        if (item.Subtitle != subtitle)
        {
            item.Subtitle = subtitle;
        }
    }

    private string? _sessionIconKind;
    private string? _weeklyIconKind;

    private void ApplyIcons(bool sessionLow, bool weeklyLow)
    {
        BandIcons.Apply(_sessionItem, ref _sessionIconKind, sessionLow ? BandIcons.Filled : BandIcons.Outline);
        BandIcons.Apply(_weeklyItem, ref _weeklyIconKind, weeklyLow ? BandIcons.Filled : BandIcons.Calendar);
    }
}
