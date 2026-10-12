using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage.Bands;

internal static class BandIcons
{
    // Brand logo, white on dark theme and dark on light theme. CmdPal does not resolve
    // fill="currentColor" in SVGs to the theme foreground, so both variants are explicit.
    public const string Outline = "outline";
    // Brand-colored logo, used as the "running low" warning
    public const string Filled = "filled";
    // Segoe Fluent calendar glyph for the weekly item; glyphs follow the theme automatically
    public const string Calendar = "calendar";

    private static IconInfo Create(string kind, string brand) => kind switch
    {
        Outline => IconHelpers.FromRelativePaths(
            $"Assets\\icons\\{brand}-outline-light.svg",
            $"Assets\\icons\\{brand}-outline-dark.svg"),
        Filled => IconHelpers.FromRelativePath($"Assets\\icons\\{brand}-filled.svg"),
        Calendar => new IconInfo(""),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    // Only reassign the icon when it actually changes, so the dock does not redraw it every refresh
    public static void Apply(ListItem item, ref string? appliedKind, string kind, string brand)
    {
        if (kind == appliedKind)
        {
            return;
        }
        try
        {
            item.Icon = Create(kind, brand);
            appliedKind = kind;
        }
        catch { /* fallback: leave existing icon */ }
    }
}
