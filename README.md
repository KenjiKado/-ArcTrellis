# ArcTrellis

ArcTrellis is a native, local-first visual story planner for 64-bit Windows 10 and Windows 11. It is a clean-room application in C# and WPF: no JavaScript, browser engine, npm dependency, account, telemetry, or cloud service is used.

## What is included

- Drag-and-drop visual timeline with books, chapters, plotlines, colored cards, zoom, and print
- Outline editor with sections/acts, chapter reordering, summaries, and word counts
- Detailed scene cards with status, POV, setting, tags, custom fields, draft notes, and editing notes
- Series dashboard, writing progress, per-book timelines, and a whole-series spine view
- Searchable characters, places, and notes with categories, tags, images-by-path, and custom sheet fields
- Character/world relationship records
- Five editable starter templates, including a fully populated worked example
- Local `.arctrellis` JSON project format, rotating backups, crash-recovery autosave, and structural undo/redo
- Markdown outline import
- Microsoft Word `.docx`, Markdown, CSV, and Scrivener project-folder exports
- Light/dark themes, keyboard shortcuts, file association, and per-user installer/uninstaller

## Install for end users

Run `ArcTrellis-Setup-1.1.11-win-x64.exe`. The installer does not need administrator rights by default. It creates an optional desktop shortcut, registers `.arctrellis` files, and includes an uninstaller in Windows Settings. Both the installer and app support English and Russian; the app remembers the selected language.

## Build the release on Windows

Requirements:

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`)

Open PowerShell in this directory and run:

```powershell
.\build-installer.ps1
```

The script restores, runs the dependency-free smoke test suite, publishes a self-contained Windows executable, compiles the installer, and writes a SHA-256 checksum. Outputs are placed in `artifacts\installer`.

## Project layout

- `src/ArcTrellis.Core` — cross-platform data, persistence, search, templates, import, and export
- `src/ArcTrellis.App` — native WPF desktop UI
- `templates` — editable bundled starter projects
- `tests/ArcTrellis.SmokeTests` — package-free executable test suite
- `installer` — Inno Setup definition
- `docs` — user and feature documentation

## Privacy and data

ArcTrellis makes no network requests. A saved project is a readable local JSON document. Up to 20 timestamped backups are kept beside it in `ArcTrellis Backups`. Recovery copies and user-created templates live under `%LOCALAPPDATA%\ArcTrellis`.

## Clean-room status

ArcTrellis is an independent application and is not affiliated with Plottr. It implements common story-planning workflows from publicly described behavior and does not contain Plottr code, branding, proprietary file formats, or proprietary template text.

## License

MIT. See `LICENSE.txt`.
