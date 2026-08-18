using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using ClaudeUsage.Pages;

namespace ClaudeUsage.Bands;

internal sealed class CodexUsageBand
{
    private readonly CodexUsageClient _client;
    private readonly WrappedDockItem _dockItem;
    private readonly ListItem _bandItem;
    private readonly CodexUsagePage _page;

    public WrappedDockItem DockItem => _dockItem;
    public int RefreshSeconds => 30;

    public CodexUsageBand(CodexUsagePage page)
    {
        _client = page._client;
        _page = page;
        _bandItem = new ListItem(page);
        _dockItem = new WrappedDockItem([_bandItem], "claudeusage.dock.codex", "Codex Usage");
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
                SetTitle("Codex —", errorMessage);
                ApplyIcon(quiet: true);
                return;
            }

            var snapshot = result.Snapshot;
            var sessionLeft = (int)Math.Round(snapshot.SessionLeftPercent);
            var weeklyLeft = (int)Math.Round(snapshot.WeeklyLeftPercent);
            var resetLocal = snapshot.SessionResetsAt.ToLocalTime();

            var title = $"{sessionLeft}%";
            var subtitle = $"wk {weeklyLeft}% · resets {resetLocal:HH:mm}";

            SetTitle(title, subtitle);

            var isLow = sessionLeft < 20;
            ApplyIcon(quiet: !isLow);
        }
        catch
        {
            SetTitle("Codex —", "error fetching usage");
            ApplyIcon(quiet: true);
        }
    }

    private void SetTitle(string title, string? subtitle = null)
    {
        _bandItem.Title = title;
        if (subtitle != null)
        {
            _bandItem.Subtitle = subtitle;
        }
    }

    private string? _appliedIconPath;

    private void ApplyIcon(bool quiet)
    {
        var path = $"Assets\\icons\\codex-{(quiet ? "outline" : "filled")}.svg";
        if (path == _appliedIconPath)
        {
            return;
        }
        try
        {
            _bandItem.Icon = IconHelpers.FromRelativePath(path);
            _appliedIconPath = path;
        }
        catch { /* fallback: leave existing icon */ }
    }
}
