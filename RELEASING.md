# Releasing the Windows installer

VRoom is downloaded as one file, `VRoom-Setup.exe`. It is never committed to git: it lives on GitHub Releases, and the website button always points at the newest one.

```text
Unity build  ->  Inno Setup  ->  VRoom-Setup.exe  ->  GitHub Release  ->  website button
Builds/Windows/             Builds/Installer/       releases/latest/download/VRoom-Setup.exe
```

## Automatic: every change on `master`

`.github/workflows/windows-installer.yml` runs on every push to `master` (changes to `docs/`, `Blender/` and `*.md` are skipped). It builds the Windows player with Unity, packs the installer with Inno Setup and publishes a new release `v0.1.<run number>`, so the download link always gives the latest version. The installer's version is that same number.

Add these secrets once (GitHub: repository > Settings > Secrets and variables > Actions):

| Secret | What it is |
|---|---|
| `UNITY_LICENSE` | the content of a Unity Personal licence file (`.ulf`). How to get it: https://game.ci/docs/github/activation |
| `UNITY_EMAIL`, `UNITY_PASSWORD` | the Unity account that owns that licence |
| `GOOGLE_OAUTH_CLIENT_JSON` | the whole text of `Assets/VRoom/Firebase/google-oauth-client.json` (Google sign-in in the installed game) |

Until the secrets are there, the build job fails and nothing is published. The previous release stays online.

Check a run in the Actions tab; start one by hand with **Run workflow**. Do not delete the latest release: without one the website button gives a 404.

## By hand, on this computer

1. Close the Unity editor (a project opens in one Unity at a time).
2. Install Inno Setup 6 once: `winget install JRSoftware.InnoSetup`.
3. In PowerShell, from the repo root:

```powershell
Tools/installer/build-installer.ps1 -Version 0.1.5
```

It runs `VRoom > Build Windows` from the command line (the same code the workflow uses), then Inno Setup. The result is `Builds/Installer/VRoom-Setup.exe` (and `VRoom-Windows.zip`). Test it: install, start VRoom from the Start menu, sign in, uninstall.

To publish it yourself: on GitHub, Releases > Draft a new release, tag `v0.1.5`, attach both files, Publish (not a pre-release). Keep the file name `VRoom-Setup.exe`.

You can also build the player from the editor: menu **VRoom > Build Windows** writes `Builds/Windows/VRoom.exe`.

## How Google sign-in gets into the game

`google-oauth-client.json` is git-ignored. `BuildWindows` copies it into the streaming assets for the build and removes it afterwards; the game reads it from there. In CI the file is written from the `GOOGLE_OAUTH_CLIENT_JSON` secret before the build.

## SmartScreen warning

The installer is not code signed, so Windows shows "Windows protected your PC" on first run: **More info**, then **Run anyway**. The website says this next to the download button.

## Rules to keep the link working

- Never rename `VRoom-Setup.exe`.
- Do not commit installers or builds (`Builds/` is git-ignored).
- Do not delete the latest release.
