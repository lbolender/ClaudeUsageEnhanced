# CmdPal Extension Research Notes

> Research log compiled from a live UI redesign attempt on the **ClaudeUsage** PowerToys Command Palette extension.
> Covers: CmdPal extension architecture, reflection-derived API surface, AdaptiveCard rendering constraints, and the failed UI iterations.

---

## 1. Project architecture

**ClaudeUsage** is a PowerToys Command Palette (CmdPal) dock-band extension.

Key files:

| File | Purpose |
|------|---------|
| `ClaudeUsage/Program.cs` | Extension host runner (`ExtensionHostRunner.RunAsync`) |
| `ClaudeUsage/Package.appxmanifest` | MSIX identity (`Mathias.ClaudeUsage`), COM server registration, app extension registration |
| `ClaudeUsage/ClaudeUsageExtension.cs` | Implements `IExtension`; returns `ClaudeUsageCommandsProvider` for `ProviderType.Commands` |
| `ClaudeUsage/ClaudeUsageCommandsProvider.cs` | Extends `CommandProvider`; exposes one top-level command (`UsagePage`) and one dock band (`UsageBand`) |
| `ClaudeUsage/Bands/UsageBand.cs` | The live dock band (`Claude 93%`) refreshed every 30 seconds |
| `ClaudeUsage/Pages/UsagePage.cs` | The detail/stats page (the "dropdown") opened by clicking the top-level command or the band |
| `ClaudeUsage/Pages/UsageFormContent.cs` | (Created and reverted) Custom `FormContent` subclass to handle the card's submit action |

### Runtime model

- The extension is an **MSIX package** signed with a locally-generated self-signed dev certificate (`CN=Mathias-Dev`).
- The manifest declares a **COM out-of-process server** (`ClaudeUsage.exe -RegisterProcessAsComServer`).
- When CmdPal activates the extension, it spawns `ClaudeUsage.exe` as a COM server. This process shows up in Task Manager as **Claude Usage** / `ClaudeUsage.exe`.
- Killing the process doesn't uninstall the package, but uninstalling the app from **Settings → Apps** removes it entirely.
- The build/install path is source-based: `build-and-install.ps1` builds, signs, and installs the MSIX. It works for any developer because each developer generates their own `CN=Mathias-Dev` self-signed cert locally.

---

## 2. NuGet packages and versions used

```xml
<PackageReference Include="Microsoft.CommandPalette.Extensions" Version="0.9.260303001" />
<PackageReference Include="JPSoftworks.CommandPalette.Extensions.Toolkit" Version="0.9.0" />
```

- `Microsoft.CommandPalette.Extensions` ships both a managed toolkit (`Microsoft.CommandPalette.Extensions.Toolkit.dll`) and a native WinRT projection (`Microsoft.CommandPalette.Extensions.dll`).
- `JPSoftworks.CommandPalette.Extensions.Toolkit` is a separate community toolkit that adds the `ExtensionHostRunner`, logging, and shutdown helpers. It does **not** contain `ContentPage`/`FormContent`/`MarkdownContent`.
- The source repo for `JPSoftworks.CommandPalette.Extensions.Toolkit` is `https://github.com/jiripolasek/JPSoftworks.CommandPalette.Extensions.Toolkit`.

---

## 3. Reflection-derived API surface

Because the official docs and samples are sparse, the exact API surface was discovered by reflecting on the installed NuGet DLLs with a throwaway .NET console project. The key findings are below.

### `ContentPage` (Microsoft toolkit)

```csharp
namespace Microsoft.CommandPalette.Extensions.Toolkit;

public abstract class ContentPage : Page
{
    public virtual IContent[] GetContent();
    public void RaiseItemsChanged(int totalCount);          // <-- page refresh signal

    public virtual IDetails Details { get; set; }
    public virtual IContextItem[] Commands { get; set; }

    public event TypedEventHandler<object, IItemsChangedEventArgs> ItemsChanged;
}
```

- `GetContent()` is the override point for the page body.
- `RaiseItemsChanged(0)` tells the host to re-render the page. The host then re-calls `GetContent()`.
- **There is no form-submit hook on `ContentPage` itself.** Form submission lives on `FormContent`.

### `FormContent` (Microsoft toolkit)

```csharp
namespace Microsoft.CommandPalette.Extensions.Toolkit;

public class FormContent : BaseObservable
{
    public virtual string TemplateJson { get; set; }   // AdaptiveCard template JSON
    public virtual string DataJson { get; set; }       // AdaptiveCard data context JSON
    public virtual string StateJson { get; set; }      // Additional state JSON

    public virtual ICommandResult SubmitForm(string payload, string state);  // Action.Submit hook
    public virtual ICommandResult SubmitForm(string payload);                // convenience overload
}
```

- `Action.Submit` buttons in an AdaptiveCard invoke `FormContent.SubmitForm`.
- The `payload` string is the JSON-serialized `data` object from the submit action (plus any input values).
- The `ICommandResult` return controls CmdPal behavior after the submit.

### `CommandResult` factories

```csharp
namespace Microsoft.CommandPalette.Extensions.Toolkit;

public class CommandResult : ICommandResult
{
    public static CommandResult KeepOpen();          // keep the page open
    public static CommandResult Dismiss();
    public static CommandResult GoHome();
    public static CommandResult GoBack();
    public static CommandResult Hide();
    public static CommandResult GoToPage(GoToPageArgs args);
    public static CommandResult ShowToast(string message);
    // ...
}
```

For a refresh button, `CommandResult.KeepOpen()` is the correct return.

---

*The source repo for `JPSoftworks.CommandPalette.Extensions.Toolkit` is `https://github.com/jiripolasek/JPSoftworks.CommandPalette.Extensions.Toolkit`.*

---

## 4. AdaptiveCard rendering constraints discovered

CmdPal's AdaptiveCard renderer is **not a full-featured AdaptiveCard host**. Several properties that are valid in the spec do not render or cause issues:

| Feature | Status | Notes |
|---------|--------|-------|
| `TextBlock.color` | **Does not render** | `good`, `warning`, `attention` are ignored. Do not rely on color. |
| `TextBlock.fontType` / `fontType` | Uncertain | Set to `Monospace` for the bar; it is harmless if ignored. |
| `ColumnSet` | Works | Can be used for left/right aligned rows. |
| `FactSet` | Works | Renders as a formal key/value table. |
| Progress bar | **Not available** | No native progress bar element in AdaptiveCard 1.5. Use Unicode block characters (`█`/`░`) or similar. |
| Colored inline elements | **Not possible** | Since `color` is ignored, the only reliable color in text is Unicode emoji (🟢/🟡/🔴). |

Because of the `color` limitation, the only practical way to add color-coded status indicators is emoji.

---

## 5. What the original UsagePage looked like

The original implementation returned two content objects:

1. `MarkdownContent` — a plain markdown string containing:
   - `# Claude usage` header
   - `Max 5× · session resets HH:mm` subtitle
   - `## Session (5h)` and a Unicode block bar with `NN% left`
   - `## Week (all models)` and its bar
   - Per-model bars
   - `Last updated HH:mm`
2. `FormContent` — containing only an `Action.Submit` button titled `"Refresh now"`.

The refresh button was **unwired** in the original code: `FormContent.SubmitForm` was never overridden, so the button did nothing.

The original was functional but visually plain (monochrome, all text, no clickable hierarchy).

---

## 6. UI redesign iterations and failures

### Attempt 1: Single AdaptiveCard with separated layout

- Converted the whole page to one `FormContent` AdaptiveCard.
- For each metric, used a `ColumnSet` row: label on the left, big percentage on the far right.
- Put the Unicode bar on a second full-width row below.
- Added a `FactSet` footer with reset times and `Updated` time.

**Why it failed:**
- The percentage was visually far away from the bar (the user's main complaint).
- The header subtitle already showed `session resets HH:mm`, and the `FactSet` repeated it, causing duplication.
- The overall look felt more cluttered than the original.

### Attempt 2: Adjacent bar + percentage on one line

- Kept the label on its own line.
- Put the colored emoji, the Unicode bar, and the percentage on a single TextBlock line:  
  `🟢  ███████░░░░░░░░░░░░░  33% left`
- Removed the duplicate `FactSet` and replaced it with a single subtle footer line.

**Why it still failed:**
- The user found the colored dots jarring and the overall layout still worse than the original.
- Unicode block bars are inherently limited — they cannot match a polished native progress bar.
- The emoji-based color scheme looked out of place in CmdPal's host.

**Lesson:** CmdPal's AdaptiveCard host is too constrained to produce a visually polished progress-bar UI. Any design that relies on text-only graphics or emoji tends to look amateur compared to a native app. A better strategy is to make small, targeted tweaks to the original markdown layout rather than full redesigns.

---

## 7. Tooling and build gotchas

### 7.1 PowerShell execution policy

Running `build-and-install.ps1` directly failed with:

```
File ... cannot be loaded because running scripts is disabled on this system.
```

**Fix:** Run through `pwsh` or `powershell` with `-ExecutionPolicy Bypass`:

```powershell
pwsh -ExecutionPolicy Bypass -File .\build-and-install.ps1
```

### 7.2 Em dash in `build-and-install.ps1` breaks PowerShell 5.1 parsing

The script contains an em dash (`—`) inside a throw string:

```powershell
throw "Elevated install failed (exit code $($proc.ExitCode)) — package NOT updated"
```

Windows PowerShell 5.1 failed to parse this with:

```
Unexpected token 'package' in expression or statement.
```

**Fix:** Use PowerShell 7 (`pwsh`) for the build script, which handles UTF-8/script parsing correctly.

### 7.3 Same-version reinstall block

Windows blocks reinstalling an MSIX with the same identity/version but different contents:

```
Deployment failed with HRESULT: 0x80073CFB
The provided package is already installed, and reinstallation of the package was blocked.
```

**Fix:** Bump the version in `Package.appxmanifest` before each reinstall, or uninstall the old package first:

```powershell
Remove-AppxPackage -Package 'Mathias.ClaudeUsage_0.1.5.0_neutral__xn3vbfvyt5h8j'
```

### 7.4 Public release binary advice

Do **not** attach prebuilt MSIX binaries to a GitHub release. The MSIX is signed with a personal self-signed cert (`CN=Mathias-Dev`). Other users would have to trust that arbitrary cert. The intended distribution is source-only via `build-and-install.ps1`.

To ship a real binary release, a CA-issued code-signing certificate would be required.

---

## 8. Reproduction of the reflection-based API discovery

If you need to verify or extend the API findings, create a temporary .NET console project and reference the same package:

```xml
<!-- cmdpal-inspect.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows10.0.26100.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CommandPalette.Extensions" Version="0.9.260303001" />
  </ItemGroup>
</Project>
```

```csharp
using System.Reflection;
using Microsoft.CommandPalette.Extensions.Toolkit;

Print(typeof(ContentPage));
Print(typeof(FormContent));
Print(typeof(MarkdownContent));
Print(typeof(CommandResult));

static void Print(Type t)
{
    Console.WriteLine($"\n=== {t.FullName} ===");
    foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        Console.WriteLine($"  {m.MemberType} {m}");
}
```

Run:

```powershell
dotnet run -c Release
```

---

## 9. Open questions and recommendations for future UI work

1. **Can CmdPal render a real progress bar?**  
   No native progress element exists in the exposed toolkit. Any progress indicator must be text-based (Unicode blocks, emoji, or images). If the team ever exposes an image-generation API or a custom XAML host, this could change.

2. **How to make a small improvement instead of a full redesign?**
   - Keep the original markdown structure.
   - Add a subtle status indicator if desired, but keep it small.
   - If colors are wanted, use emoji sparingly (e.g., one dot next to the header, not per line).

3. **Wiring the Refresh button without redesigning the page:**
   - Subclass `FormContent` and override `SubmitForm(string payload, string state)`.
   - Trigger an async forced fetch, then call `RaiseItemsChanged(0)` on the owning `ContentPage`.
   - Keep the original `MarkdownContent` and return the `FormContent` alongside it, or consolidate into one card. This is a low-risk change.

4. **Should `TextBlock.color` be re-tested?**  
   It failed in the current CmdPal version, but a future PowerToys update might honor it. Re-test if CmdPal is updated.

5. **What does Claude's actual usage UI look like?**  
   The user asked for "same view as Claude uses". We don't have a reference image. If a screenshot or description of Claude's web/desktop usage page is available, it would be the best starting point for a future redesign.

---

## 10. Sources

- `Microsoft.CommandPalette.Extensions.Toolkit.dll` (version 0.9.260303001) — reflected directly.
- `JPSoftworks.CommandPalette.Extensions.Toolkit` NuGet page and GitHub repo (`jiripolasek/JPSoftworks.CommandPalette.Extensions.Toolkit`).
- PowerToys Command Palette SDK documentation referenced in the NuGet README.
- Adaptive Card schema documentation (`https://adaptivecards.io/schemas/adaptive-card.json`).

---

*Last updated: 2026-07-15*
