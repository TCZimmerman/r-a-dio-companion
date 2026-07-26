# R/a/dio Companion v1

A small native Windows desktop companion app for
[r/a/d.io](https://r-a-d.io/).

R/a/dio Companion is an unofficial third-party client and is not
affiliated with or endorsed by r-a-d.io.

## Features

-   Live playback from `https://relay1.r-a-d.io/main.mp3`
-   Connects to the audio stream only while playing; stopping playback
    closes the connection
-   Lightweight live metadata updates via the station SSE endpoint
-   Displays current artist/title, source/tags, DJ name and avatar
-   Handles animated GIF avatars even when the server reports them as
    `.png`
-   `LIVE` indicator for human DJs and `BOT` indicator for Hanyuu-sama
-   Non-seekable progress bar based on station timestamps
-   Click the progress bar or current track title to copy the song name
-   Click source/tags to search the first tag on Google
-   Previous and next tracks in the compact view
-   Expanded history and queue views (up to five entries each)
-   Click any history or queue item to copy it
-   App-level volume control
-   Global Play/Pause and Stop media keys where supported by Windows
-   Remembers window position, volume, theme, always-on-top and
    lock-position settings
-   Optional launch on Windows startup
-   Classic, Blue and Light themes
-   Standalone audio playback using the bundled LibVLC runtime

## Building

1.  Install the Windows x64 **.NET 8 SDK**.
2.  Run `build.ps1` from PowerShell:

``` powershell
-ExecutionPolicy Bypass -File .\build.ps1
```

The published application is self-contained and does not require the
.NET runtime or VLC to be installed separately.

## Usage notes

-   Drag any empty area of the window to move it.
-   The `⋮` menu contains Always on top, Lock position, Start with
    Windows, themes and Exit.
-   Playback starts stopped when the app launches.
-   Album artwork is not included in v1.
-   The app uses LibVLCSharp for reliable internet radio playback.

## Settings

Settings are stored at:

`%LOCALAPPDATA%\RadioCompanion\settings.json`

## Building via GitHub Actions

The repository includes a GitHub Actions workflow if you do not want to
install the SDK locally.

1.  Open the repository's **Actions** tab.
2.  Select **Build Windows app**.
3.  Choose **Run workflow**.
4.  Download the `RadioCompanion-win-x64` artifact once the build
    finishes.

## Licence and notices

See: - `LICENSE` - `NOTICE.md`
