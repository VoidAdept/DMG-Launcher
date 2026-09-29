# DMG Launcher

Third-party launcher for [Duel Monsters Genesis](https://www.byond.com/games/EternalDuelistSoul/DuelMonstersGenesis) (Windows).

- Checks GitHub Releases for a newer launcher and offers to update itself.
- Makes sure BYOND is installed (offers the official installer) and running (3 tries, then quits).
- Mirrors the BYOND pager's login. The launcher never asks for or stores your password; "Create an Account" opens BYOND's sign-up page.
- **Server List**: DMG servers from the BYOND hub, with player counts and a Join button.
- **Friends**: track BYOND keys and see which DMG server they're on, with a Join button.
- **Options**: version, update check, reset settings, clear stored data (`%APPDATA%\DMG-Launcher`).

Joining hands the server's `byond://` address to BYOND, exactly like the hub's Play button. BYOND's normal loading and membership screens are never skipped.

## Not supported
- Password-protected flag (the BYOND hub doesn't publish it).
- General online status for friends (BYOND has no public API for it; friends show as online only while on a DMG server).
- Logging in to BYOND from inside the launcher.
- Linux / macOS.

## Build
```
dotnet run -- --selftest
dotnet publish -c Release -o out
```
Pushing a `v*` tag builds `DMG-Launcher.exe` and attaches it to a GitHub release, which is what the updater downloads.
