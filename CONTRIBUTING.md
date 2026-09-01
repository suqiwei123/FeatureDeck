# Contributing to FeatureDeck

Thanks for your interest in improving FeatureDeck! This project is a WinUI 3 GUI for [ViVe](https://github.com/thebookisclosed/ViVe), licensed under GPLv3. It touches Windows' internal feature-staging configuration, so please read the notes below before diving in.

## Ways to contribute

- **Report a bug** — open an issue using the bug template. Include Windows build number (e.g. 26200) and the crash log if the app crashed (`%LOCALAPPDATA%\FeatureDeck\crash.log`).
- **Request a feature** — open an issue using the feature template and describe the motivation.
- **Fix a bug / add a feature** — follow the pull request flow below.
- **Improve the dictionary** — if you know what an unnamed feature ID (`Unnamed feature #<ID>`) does, suggest its name; dictionary updates are always welcome.

## Development environment

- Windows 10 build 18963 or later (Windows 11 recommended, ideally 24H2/25H2 for the most recent feature IDs)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- No runtime installation needed — the project uses WindowsAppSDK self-contained deployment

## Build & run

```bash
cd src/FeatureDeck
dotnet build -c Release -p:Platform=x64
```

Or double-click `build_and_run.cmd` at the repo root (builds and launches with elevation).

Diagnostics probe (console):

```bash
cd src/ViVeProbe
dotnet run -c Debug -p:Platform=x64
```

## Project layout

```
src/
  FeatureDeck/          Main app (WinUI 3)
    Native/              Kernel layer: ntdll P/Invoke, structs, FeatureManager (ported from ViVe)
    Services/            Query merge, dictionary mapping, NTSTATUS translation, localization
    Models/              Data models
    ViewModels/          UI view model
    Converters/          XAML converters
    Assets/              Feature name dictionary
    Strings/             Localization resources (zh-CN / en-US)
  ViVeProbe/             Console diagnostics tool
```

## Code conventions

Please keep these rules; most come from bugs already fixed in this project:

1. **Localization** — never hardcode UI strings. XAML text goes through `x:Uid` + `Strings/<lang>/Resources.resw`; code strings go through `AppResources.Get/Format`.
   - One `x:Uid` name must be used on **one control type only**, and the resw entries for that Uid must only contain properties the control supports (e.g. a `TextBlock` may only have `.<Uid>.Text`; a `Button` only `.<Uid>.Content`). Mixing them causes a startup `XamlParseException`.
   - Keep zh-CN and en-US resw key sets identical.
2. **WinUI 3 collection pitfalls** — do not populate collection-type properties (`RadioButtons.Items`, etc.) in the constructor before the control is loaded; do it in `Loaded`.
3. **Language switching** — use `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride` (MRT Core). The old `Windows.Globalization` variant does **not** work in unpackaged apps. It must be set before any resource is loaded (in the `App` constructor) and is not persisted between sessions.
4. **Never touch `bin/` / `obj/` outputs** — they are gitignored; release zips are produced by `build_release.cmd`.
5. Follow existing C# style: file-scoped namespaces not required, but match surrounding code; use `this`-free, concise patterns found in the codebase.

## Pull request flow

1. **Fork** this repository and clone your fork.
2. Create a topic branch: `git checkout -b fix/description`.
3. Make your changes. Verify the app builds with **0 errors** and, if your change affects UI, launches correctly.
4. Keep the change small and focused. If it fixes an issue, reference it (`Fixes #123`).
5. Commit with a clear message and push to your fork.
6. Open a pull request against `main` using the PR template, and describe what changed and how you tested it.

### Checklist before opening a PR

- [ ] Builds with 0 errors (`dotnet build -c Release -p:Platform=x64`)
- [ ] No hardcoded UI strings; zh-CN and en-US resources both updated if needed
- [ ] No `bin/`/`obj/` files included
- [ ] Tested launching the app (and the scenario you changed)

## Research-purpose notice

This project modifies Windows feature-staging configuration, which is undocumented and unsupported by Microsoft. Changes are made at your own risk. Keep this in mind when reviewing/merging — prefer fixes that are conservative and well-tested.

## License

By contributing, you agree that your contributions are licensed under GPLv3 (see [LICENSE](LICENSE)).
