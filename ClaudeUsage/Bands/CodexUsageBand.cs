using System.Globalization;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using ClaudeUsage.Pages;

namespace ClaudeUsage.Bands;

internal sealed class CodexUsageBand
{
    private readonly CodexUsageClient _client;
    private readonly WrappedDockItem _dockItem;
    private readonly ListItem _sessionItem;
    private readonly ListItem _weeklyItem;
    private readonly CodexUsagePage _page;

    public WrappedDockItem DockItem => _dockItem;
    public int RefreshSeconds => 30;

    public CodexUsageBand(CodexUsagePage page)
    {
        _client = page._client;
        _page = page;
        _sessionItem = new ListItem(page);
        _weeklyItem = new ListItem(page);
        _dockItem = new WrappedDockItem([_sessionItem, _weeklyItem], "claudeusage.dock.codex", "Codex Usage");
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var result = await _client.GetAsync();

            if (result.Reason != UsageFailureReason.Ok || result.Snapshot == null)
            {
                var errorMessage = result.Reason switch
                {
                    UsageFailureReason.NoCredentials => "sign in to Codex",
                    UsageFailureReason.HttpError => $"API error {result.HttpStatusCode}",
                    UsageFailureReason.NetworkError => "offline",
                    UsageFailureReason.ParseError => "parse error",
                    _ => "error fetching usage"
                };
                SetText(_sessionItem, "Codex —", errorMessage);
                SetText(_weeklyItem, "—", errorMessage);
                ApplyIcons(sessionLow: false, weeklyLow: false);
                return;
            }

            var snapshot = result.Snapshot;
            var sessionLeft = (int)Math.Round(snapshot.SessionLeftPercent);
            var weeklyLeft = (int)Math.Round(snapshot.WeeklyLeftPercent);
            var sessionReset = snapshot.SessionResetsAt.ToLocalTime();
            var weeklyReset = snapshot.WeeklyResetsAt.ToLocalTime();

            SetText(_sessionItem, $"{sessionLeft}%", $"5h · resets {sessionReset:HH:mm}");
            SetText(_weeklyItem, $"{weeklyLeft}%",
                $"week · resets {weeklyReset.ToString("ddd HH:mm", CultureInfo.InvariantCulture)}");

            ApplyIcons(sessionLow: sessionLeft < 20, weeklyLow: weeklyLeft < 20);
        }
        catch
        {
            SetText(_sessionItem, "Codex —", "error fetching usage");
            SetText(_weeklyItem, "—", "error fetching usage");
            ApplyIcons(sessionLow: false, weeklyLow: false);
        }
    }

    private static void SetText(ListItem item, string title, string subtitle)
    {
        item.Title = title;
        item.Subtitle = subtitle;
    }

    private string? _sessionIconKind;
    private string? _weeklyIconKind;

    private void ApplyIcons(bool sessionLow, bool weeklyLow)
    {
        BandIcons.Apply(_sessionItem, ref _sessionIconKind, sessionLow ? BandIcons.Filled : BandIcons.Outline, "codex");
        BandIcons.Apply(_weeklyItem, ref _weeklyIconKind, weeklyLow ? BandIcons.Filled : BandIcons.Calendar, "codex");
    }
}
