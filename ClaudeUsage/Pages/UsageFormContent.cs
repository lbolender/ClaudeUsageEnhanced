using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage.Pages;

/// <summary>
/// AdaptiveCard form content that wires the card's "Refresh now" submit
/// action back into the page. FormContent.SubmitForm is the hook CmdPal
/// calls when an Action.Submit in the card is triggered.
/// </summary>
internal sealed class UsageFormContent : FormContent
{
    private readonly Action _onSubmit;

    public UsageFormContent(string templateJson, Action onSubmit)
    {
        TemplateJson = templateJson;
        DataJson = "{}";
        StateJson = "{}";
        _onSubmit = onSubmit;
    }

    public override ICommandResult SubmitForm(string payload, string state)
    {
        // The only action in the card is "Refresh now", so any submit = refresh.
        // Ignore the payload; refresh runs off-thread and signals a re-render.
        _onSubmit();
        return CommandResult.KeepOpen();
    }
}
