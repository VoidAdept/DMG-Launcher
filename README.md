# DMG Launcher

A third-party launcher for [Duel Monsters Genesis](https://www.byond.com/games/EternalDuelistSoul/DuelMonstersGenesis),
the free Yu-Gi-Oh! fan game on BYOND. Windows only.

## Features
- **Sign-in:** BYOND's own login page (secure.byond.com) opens inside the launcher. Your password goes to byond.com only;
  the launcher never sees or stores it. Each login is kept for 30 days, and you can store several BYOND keys and pick
  one from a drop-down.
- **Its own BYOND:** on first start the launcher asks permission, then downloads the official BYOND client from
  byond.com and keeps it in its own folder. No installer, and any BYOND you already have is left alone. On PCs without
  Microsoft's DirectX 9 components, the first join runs BYOND's own DirectX setup, which asks Windows for permission once.
- **Server List:** DMG servers from the BYOND hub, with player counts and a Join button.
- **News:** the latest posts from [duelmonstersgenesis.com](https://duelmonstersgenesis.com/).
- **Friends:** track BYOND keys and see which DMG server they're on.
- **Options:** launcher updates, BYOND version (check for updates at launch, pin or downgrade to any archived build,
  reinstall), forget stored logins, reset settings, clear stored data.
- **Self-update:** checks GitHub Releases for a newer launcher and offers to update itself.

Joining a world starts BYOND's own game window with the server's address, exactly like the hub's Play button.
BYOND's normal loading screens and the free-account "Your Game Is Starting" wait always run in full. Nothing of BYOND's
is modified, decompiled or skipped.

## Install a release
1. Open the repository's [Releases](../../releases) page and download `DMG-Launcher.exe` from the latest release.
2. Put it in any folder you like (for example `Documents\DMG Launcher`) and run it. There is no installer, and the
   .NET runtime is built into the exe.
3. Windows SmartScreen may warn about an unrecognised app because the exe isn't code-signed. Choose
   **More info → Run anyway**.
4. On first start, agree to the BYOND download, then sign in to BYOND.

**Requirements:** Windows 10 or 11 (64-bit) and the Microsoft Edge WebView2 Runtime, which Windows 11 and up-to-date
Windows 10 already include (otherwise get it from [Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/)).

**Updating:** the launcher checks for new releases on start (Options can turn this off) and replaces itself.

**Uninstalling:** delete the exe, then these folders:
- `%APPDATA%\DMG-Launcher`: settings and friends list.
- `%LOCALAPPDATA%\DMG-Launcher`: the launcher's BYOND copy, stored BYOND logins and `launcher.log`.

## Build it yourself
You need Windows 10/11 (64-bit), [Git](https://git-scm.com/) and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
git clone https://github.com/VoidAdept/DMG-Launcher.git
cd DMG-Launcher
```

Run it from source:
```
dotnet run
```

Run the self-test (server-list parsing, key names and update-version checks; no network or UI):
```
dotnet run -- --selftest
```

Build the single-file exe (self-contained, win-x64) into `out\`:
```
dotnet publish -c Release -o out
```
The result is `out\DMG-Launcher.exe`; copy it anywhere and run it as above.

### Making a release
Bump `<Version>` in `DMGLauncher.csproj`, commit, then push a tag such as `v0.3.0`. The GitHub workflow runs the
self-test, publishes the exe with that version and attaches it to a GitHub release, which is what the launcher's
self-update downloads.

## Where things are stored
| Path | Contents |
| --- | --- |
| `%APPDATA%\DMG-Launcher\settings.json` | Settings and friends. Stored BYOND keys are encrypted with Windows DPAPI (readable only by your Windows account); no passwords |
| `%LOCALAPPDATA%\DMG-Launcher\byond\<version>` | The launcher's copy of the official BYOND client |
| `%LOCALAPPDATA%\DMG-Launcher\logins` | One browser profile per stored BYOND login (BYOND's own cookie, encrypted by WebView2 with DPAPI), kept 30 days |
| `%LOCALAPPDATA%\DMG-Launcher\launcher.log` | Diagnostic log; BYOND keys and session ids are masked |

## Not supported
- Password-protected flag on servers (the BYOND hub doesn't publish it).
- General online status for friends (BYOND has no public API for it; friends show as online only while on a DMG server).
- Linux and macOS.

## Licence
The launcher's **source code** is released under the [MIT License](LICENSE).

The MIT License does **not** cover the files in `Assets/`:
- `witch.png`, `cosmic.png`: artwork by Void (@AVoidAdept), all rights reserved.
- `logo.png`, `logo-trim.png`, `logo.ico`, `background.jpg`, `streaks.png`: Duel Monsters Genesis artwork from the
  game's own files.
- `Fonts/`: the duelmonstersgenesis.com fonts (Alegreya SC, Vollkorn SC, Crimson Pro, Oxanium digits), under the
  [SIL Open Font License 1.1](Assets/Fonts/OFL.txt).

Approach for BYOND sign-in and joining based on the MIT-licensed
[SS13 Launcher](https://github.com/spacestation13/launcher). Built with [Avalonia](https://avaloniaui.net/) (MIT) and
Microsoft Edge WebView2.

*Duel Monsters Genesis* is a 100% free fan game, not affiliated with Konami® or the Yu-Gi-Oh! Trading Card Game®.
“Yu-Gi-Oh!” and all related names, characters and images are the property of Konami Corporation®, Studio Dice,
Shueisha, TV Tokyo & Kazuki Takahashi. BYOND is a trademark of BYOND Software.
[Full disclaimer](https://duelmonstersgenesis.com/disclaimer/)
