using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage.Bands;

internal static class BandIcons
{
    // Claude logo, white on dark theme and dark on light theme. CmdPal does not resolve
    // fill="currentColor" in SVGs to the theme foreground, so both variants are explicit.
    public const string Outline = "outline";
    // Brand-colored logo, used as the "running low" warning
    public const string Filled = "filled";
    // Segoe Fluent calendar glyph for the weekly item; glyphs follow the theme automatically
    public const string Calendar = "calendar";

    private static IconInfo Create(string kind) => kind switch
    {
        Outline => IconHelpers.FromRelativePaths(
            "Assets\\icons\\claude-outline-light.svg",
            "Assets\\icons\\claude-outline-dark.svg"),
        Filled => IconHelpers.FromRelativePath("Assets\\icons\\claude-filled.svg"),
        Calendar => new IconInfo("\uE787"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    // Only reassign the icon when it actually changes, so the dock does not redraw it every refresh
    public static void Apply(ListItem item, ref string? appliedKind, string kind)
    {
        if (kind == appliedKind)
        {
            return;
        }
        try
        {
            item.Icon = Create(kind);
            appliedKind = kind;
        }
        catch { /* fallback: leave existing icon */ }
    }
}
