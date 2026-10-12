using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using ClaudeUsage.Bands;
using ClaudeUsage.Pages;

namespace ClaudeUsage;

public sealed partial class ClaudeUsageCommandsProvider : CommandProvider
{
    private readonly UsagePage _usagePage;
    private readonly UsageBand _usageBand;
    private Timer? _refreshTimer;

    public ClaudeUsageCommandsProvider()
    {
        Id = "ClaudeUsage";
        DisplayName = "ClaudeUsage";
        Icon = IconHelpers.FromRelativePath("Assets\\icons\\claude-filled.svg");

        _usagePage = new UsagePage();
        _usageBand = new UsageBand(_usagePage);

        // Redraw band every 30 seconds; UsageClient serves from cache and hits the API at most every 3 minutes
        _refreshTimer = new Timer(async _ =>
        {
            await _usageBand.RefreshAsync();
            RaiseItemsChanged(0);
        }, null, TimeSpan.FromSeconds(_usageBand.RefreshSeconds), TimeSpan.FromSeconds(_usageBand.RefreshSeconds));
    }

    public override ICommandItem[] TopLevelCommands() =>
    [
        new CommandItem(_usagePage) { Title = "Claude usage", Icon = IconHelpers.FromRelativePath("Assets\\icons\\claude-filled.svg") },
    ];

    public override ICommandItem[]? GetDockBands()
    {
        return [_usageBand.DockItem];
    }
}
