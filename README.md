# DMG Launcher

Third-party launcher for [Duel Monsters Genesis](https://www.byond.com/games/EternalDuelistSoul/DuelMonstersGenesis) (Windows).

- Checks GitHub Releases for a newer launcher and offers to update itself.
- Makes sure BYOND is installed (otherwise sends you to the BYOND download page) and running (3 tries, then quits); closes when BYOND closes.
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

## integrated-client branch (prototype)
Based on the approach of the MIT-licensed [SS13 Launcher](https://github.com/spacestation13/launcher):
- **Sign-in:** BYOND's own login page (secure.byond.com) inside the launcher (WebView2). The password goes to byond.com
  only. Each login is BYOND's cookie in its own private profile (`%LOCALAPPDATA%\DMG-Launcher\logins`), kept for 30
  days. Once a key is stored the login screen offers a drop-down of stored keys plus "Add new login"; Options has
  "Forget BYOND logins", and resetting the launcher forgets them too. No password is ever stored.
  The launcher reads the key and the `webid` from DMG's hub Play link.
- **Managed BYOND:** the official zip from byond.com (community mirror as fallback), updated from `version.txt`, kept in
  `%LOCALAPPDATA%\DMG-Launcher\byond\<version>` and trimmed to the files needed to play. No installer or admin rights.
- **Joining:** starts that copy's DreamSeeker with the world address. BYOND's own "Your Game Is Starting" screen
  (the non-member wait) runs in full; once the game window opens the launcher closes the pager DreamSeeker started.
  Nothing of BYOND's is modified, decompiled or skipped.
- If an installed BYOND is already open, the launcher asks you to quit it first (BYOND runs one pager at a time).
- **BYOND versions (Options):** "Check for BYOND updates at launch" (on by default, follows the latest public release
  from `version.txt`); a drop-down of every build in BYOND's archive (byond.com/download/build/, newest two major
  versions) to downgrade or pin a build; "Reinstall & Relaunch" downloads the current build again and restarts.
- A "Joining World" overlay shows progress while BYOND starts the game.
- **News:** the latest posts from duelmonstersgenesis.com (WordPress REST API), below Server List.

## Art and fonts
Everything in `Assets/` comes from the DMG team's own files; no generated art.
- `logo.png` / `logo.ico`: the DMG 20th logo.
- `background.jpg`: `icons/skin/main/skin_logo_login_bg.png` from the game, tinted blue-purple.
- `holactie.png`: the login-screen art chosen in the launcher design.
- `Fonts/`: the card fonts packed with the game (`icons/skin/rgui/fonts`), renamed. Their name tables were trimmed to the
  Windows English records only, because Avalonia crashes on names that exist in some languages but not others.

## Build
```
dotnet run -- --selftest
dotnet publish -c Release -o out
```
Pushing a `v*` tag builds `DMG-Launcher.exe` and attaches it to a GitHub release, which is what the updater downloads.
