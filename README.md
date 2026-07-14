# UsageBar

A Windows system-tray monitor for your **AI coding usage** — see how much of your
Codex and Claude limits you have left, and when they reset, without logging into
any dashboard.

Inspired by [steipete/CodexBar](https://github.com/steipete/CodexBar) (macOS only).
UsageBar is an independent Windows implementation and is **not affiliated with
Anthropic or OpenAI**.

## Features

- **Codex** — session (5h) and weekly usage %, reset countdowns, and token totals,
  read straight from the local Codex CLI logs (`~/.codex/sessions/**/*.jsonl`). Exact,
  no network needed.
- **Claude** — accurate session / weekly / per-model usage % (the same numbers the
  Claude desktop app's Usage tab shows), fetched from your own signed-in claude.ai
  session inside the app. Falls back to token & cost estimates computed from local
  transcripts (`~/.claude/projects/**/*.jsonl`) when you're not logged in.
- **Tray flyout** with color-coded bars, auto-refresh every 45s.
- **`budget.json`** — a small, always-fresh file at
  `%LOCALAPPDATA%\CodexBarWin\budget.json` with remaining %, reset ETAs and an
  `advice.level` hint, so other tools (or an agent) can read your remaining budget
  in one cheap file read and scale work accordingly.
- **Start with Windows** toggle (tray right-click).
- Custom tray/app icon (`src/CodexBarWin.App/assets/usagebar.ico`, 16/32/48/256px), no runtime GDI drawing needed.

## Requirements

- Windows 10/11 (x64)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) —
  preinstalled on current Windows; only needed for the accurate Claude %.
- .NET 10 SDK (to build from source)

## Build & run

```powershell
git clone <your-fork-url> UsageBar
cd UsageBar
dotnet build -c Release
.\src\CodexBarWin.App\bin\Release\net10.0-windows\CodexBarWin.exe
```

A `codexbarwin` CLI is also included for a one-shot text summary:

```powershell
dotnet run --project src/CodexBarWin.Cli
```

## Publish a standalone distributable

To build a single self-contained `.exe` that runs on a machine without the
.NET runtime installed:

```powershell
dotnet publish src/CodexBarWin.App/CodexBarWin.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

`IncludeNativeLibrariesForSelfExtract=true` is required so the WebView2 loader
native DLL is bundled into the single file instead of expected alongside it.
The output is `publish\CodexBarWin.exe` — copy/rename it to `publish\UsageBar.exe`
for distribution (a self-contained single-file exe works under any file name).
**Distribute `publish\UsageBar.exe`.**

## Claude login (one-time)

The first time UsageBar needs the accurate Claude %, a login window opens — sign in
to **claude.ai** there. The session is stored only in UsageBar's own WebView2 profile
(`%LOCALAPPDATA%\CodexBarWin\WebView2`). UsageBar reuses **your own** browser session
to call claude.ai's usage endpoint; it never reads another app's cookies or tokens,
and nothing is sent anywhere except claude.ai itself.

## Notes & caveats

- The Claude % relies on an **undocumented** claude.ai endpoint. It works today but
  could change or break at any time; the local token/cost fallback keeps working
  regardless.
- Unsigned builds trigger a SmartScreen warning on first launch (More info → Run anyway).
- UsageBar only reads local files and your own claude.ai session — it does not send
  your data to any third party.

## License

MIT — see [LICENSE](LICENSE).
