# FeatureDeck

**[English](README.md) · [简体中文](README.zh-CN.md)**

**A control panel for Windows' hidden feature switches.**

Inside Windows there are thousands of feature switches (Feature Staging) rolled out gradually via A/B testing — Microsoft uses them to ship new UI and features in batches, and you'll never find them in Settings. FeatureDeck lays those 2800+ configurations out in a single table: look up names, understand what they do, flip their state, and revert everything with one click if something goes wrong.

It is a WinUI 3 front-end for [ViVe](https://github.com/thebookisclosed/ViVe): it keeps the original kernel calls, adds the official name dictionary, search & filtering, batch operations, and a set of accidental-change protections — and fixes the access-violation crash the original always hits on Windows 11 24H2/25H2.

<small>Originally "ViVe 图形化工具" (ViVeTool.GUI), forked from [thebookisclosed/ViVe](https://github.com/thebookisclosed/ViVe).</small>

| | |
|---|---|
| **Upstream** | [thebookisclosed/ViVe](https://github.com/thebookisclosed/ViVe) (by @thebookisclosed) |
| **This fork adds** | WinUI 3 GUI, feature name dictionary, search & filter, dual-store switching, batch operations, crash fix for 24H2/25H2 |
| **Tech stack** | C# / WinUI 3 / .NET 8 (WindowsAppSDK 1.8, unpackaged self-contained) |
| **License** | GPLv3 (same as upstream, see [LICENSE](LICENSE)) |

> This fork is based on the public source of ViVe (GPLv3) and is licensed under GPLv3 as well. **For research purposes only.** Modifying system feature configurations is risky; proceed at your own discretion.

## Features (current version v0.1, milestones M0–M4)

| Feature | Description |
|---|---|
| Overview | Shows all system feature configurations (ID, name, priority, state, type, variant) — 2800+ entries measured on this machine |
| Name dictionary | Bundled official `FeatureDictionary.pfs`; numeric IDs are translated to readable names automatically |
| Search / filter | Real-time search by name or ID; filter by "modified / editable / experimental / subscribed" |
| Dual stores | Switch between or operate on both  Runtime (takes effect immediately) and  Boot (takes effect after restart) |
| Enable / disable | Single-row or batch operations; a pending-restart marker is set automatically after writing to the boot store |
| Reset | Reset user overrides per row; "reset all" requires a confirmation |
| Protection | Entries managed by the system image (Immutable) are greyed out to prevent rejected writes |
| Bilingual UI | Simplified Chinese / English; follows the system language by default — a language picker pops up automatically when the system language is unsupported, and you can always switch manually (takes effect after restart) |
| Boot store repair | One-click repair of a corrupted Last Known Good store header |

## Requirements

- Windows 10 build 18963 or later (all Windows 11 versions work)
- **Administrator** privileges required (needed for writing configurations and registry changes)

## Build & Run

Option 1: double-click `build_and_run.cmd` in the repo root (builds and launches automatically).

Option 2: build manually

```bash
cd src/FeatureDeck
dotnet build -c Release -p:Platform=x64
```

Launch (a UAC elevation prompt will appear):

```
src\FeatureDeck\bin\x64\Release\net8.0-windows10.0.19041.0\FeatureDeck.exe
```

WindowsAppSDK self-contained deployment is enabled — **no runtime installation needed**, just copy the output directory and run.

## Release

Pre-built packages are published on the [Releases](https://github.com/suqiwei123/FeatureDeck/releases) page. To build a release zip locally:

```
build_release.cmd 0.1.0
```

Output: `dist\FeatureDeck-v0.1.0-win-x64.zip` (self-contained, extract and run).

## Project Structure

```
src/
  FeatureDeck/          Main app (WinUI 3)
    Native/              Kernel layer: ntdll P/Invoke, struct bitfields, FeatureManager (ported from ViVe)
    Services/            Service layer: query merge, dictionary mapping, NTSTATUS translation
    Models/              Data models
    ViewModels/          Main UI view model
    Converters/          XAML converters
    Assets/              Official feature name dictionary
  ViVeProbe/             Console probe (diagnostics tool to verify the kernel layer)
```

## Known Notes

1. **24H2/25H2 compatibility fix**: the original ViVe's "pass null buffer first, then get count" call pattern triggers an access-violation crash on Windows 11 24H2/25H2. This project uses the standard "pre-allocate buffer + capacity in/out" pattern, verified working.
2. Protected priorities (ImageDefault / EKB / ImageDefaultEditionOverride / Security / ImageOverride) cannot be written and are greyed out in the UI.
3. Writes to the "Boot" store require a system restart to take effect.
4. Querying does not require administrator privileges, but writing does.
5. The same feature ID may map to multiple priority entries; the table lists them as separate rows by (ID, priority).

## CLI Diagnostics

If the kernel layer misbehaves, use the probe to locate the problem quickly:

```bash
cd src/ViVeProbe
dotnet run -c Debug -p:Platform=x64
```
