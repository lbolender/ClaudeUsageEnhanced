using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using ClaudeUsage.Bands;
using ClaudeUsage.Pages;

namespace ClaudeUsage;

public sealed partial class ClaudeUsageCommandsProvider : CommandProvider
{
    private readonly UsageClient _client = new();
    private readonly UsagePage _usagePage;
    private readonly UsageBand _usageBand;
    private readonly Timer _refreshTimer;

    public ClaudeUsageCommandsProvider()
    {
        Id = "ClaudeUsage";
        DisplayName = "ClaudeUsage";
        Icon = IconHelpers.FromRelativePath("Assets\\icons\\claude-filled.svg");

        _usagePage = new UsagePage(_client);
        _usageBand = new UsageBand(_client, _usagePage);

        // Redraw band every 30 seconds; UsageClient serves from cache and hits the API at most every 3 minutes.
        // The band items push their own property changes to CmdPal. RaiseItemsChanged here would make the
        // host reload all top-level items and visibly redraw the whole dock on every tick.
        _refreshTimer = new Timer(_ => _ = _usageBand.RefreshAsync(), null,
            TimeSpan.FromSeconds(_usageBand.RefreshSeconds), TimeSpan.FromSeconds(_usageBand.RefreshSeconds));
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
