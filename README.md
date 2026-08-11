# ClaudeUsage

![Windows 11](https://img.shields.io/badge/Windows-11%2B-0078D4)
![PowerToys](https://img.shields.io/badge/PowerToys-0.9%2B-blue)
![MIT](https://img.shields.io/badge/License-MIT-green)

**See how much Claude Code and Codex CLI you have left — right in your Windows dock.**

A PowerToys Command Palette extension that pins your Claude Code and Codex (ChatGPT) subscription limits to the CmdPal dock as live percentage bands, each with its logo so you can tell them apart at a glance. Click either one for the full picture:

![Claude usage stats page showing session, weekly and per-model progress bars](docs/ClaudeUsage.png)

## Features

- **⚡ Dock bands**: live indicator of your 5-hour session (% left) for both Claude Code and Codex CLI, with weekly % and reset time in the subtitle
- **📊 Stats page**: session, weekly, and per-model limits as progress bars, with reset countdowns and a one-click refresh
- **🎨 Smart color alerts**: the icon fills solid when less than 20% of your session remains
- **🔒 Zero telemetry**: talks only to Anthropic's / OpenAI's official usage endpoints with the tokens Claude Code and Codex CLI already store locally; nothing else leaves your machine

## Requirements

- Windows 11 (22H2 or later)
- PowerToys Command Palette (v0.9+)
- Claude Code and/or Codex CLI signed in with a valid OAuth token (either one works standalone)

## Installation

The easiest way to install ClaudeUsage is using our pre-built releases:

1. Download the latest `ClaudeUsage-Release.zip` from the [Releases](../../releases) page.
2. Extract the ZIP file to a folder.
3. Right-click `install.ps1` and select **Run with PowerShell**.
4. Allow the UAC prompt. This will safely install the package and its certificate.
5. Open Command Palette (Win+Alt+Space).
6. Enable "Claude usage" and/or "Codex Usage" in Dock settings to see the live band(s).

### Building from source

If you want to build it yourself:

1. Clone/download this repo
2. Run `build-and-install.ps1` from a normal PowerShell prompt
3. On first install, UAC prompts once to trust the dev certificate; updates install without elevation
4. Open Command Palette (Win+Alt+Space)
5. Enable "Claude usage" and/or "Codex Usage" in Dock settings to see the live band(s)

If you get a "script disabled" error running either script: `Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser`

## How it Works

- **Claude**: reads your Claude Code OAuth token from `%USERPROFILE%\.claude\.credentials.json` and fetches live usage data from `https://api.anthropic.com/api/oauth/usage` every 5 minutes.
- **Codex**: reads your Codex CLI OAuth token from `%USERPROFILE%\.codex\auth.json` and fetches live usage data from `https://chatgpt.com/backend-api/wham/usage` every 5 minutes.

No token is logged or displayed. Both endpoints are internal/undocumented (the same ones each CLI's own UI uses to show its rate limits), so they can change without notice.

## Troubleshooting

Each dock band reports its state instead of numbers when something is wrong:

| Band subtitle | Meaning |
|---|---|
| `sign in to Claude Code` | `%USERPROFILE%\.claude\.credentials.json` not found — sign in to Claude Code |
| `sign in to Codex` | `%USERPROFILE%\.codex\auth.json` not found — sign in with `codex login` |
| `API error {status}` | The usage endpoint returned an error |
| `offline` | Network unreachable or request timed out |
| `parse error` | Response could not be parsed |

For a full trace, create an empty file `%TEMP%\claudeusage-debug.on` and check `%TEMP%\claudeusage-debug.log` — it records the request/response flow but never the token. Delete the flag file to stop logging.

## Development

- Edit source files in `ClaudeUsage/` (C# 12, .NET 9.0-windows)
- Bump the version in `Package.appxmanifest`, then run `build-and-install.ps1`
- Check PowerShell output for build errors

## License

MIT — see [LICENSE](LICENSE).
