# PhasmaStrap changelog
<!-- One "## <version>" section per release, newest first. The News page shows the section for
     the running version under "What's new", and the release on GitHub uses the same text. -->

## 2.17.4
- **Fixed: auto-update often didn't update.**
  - It skipped the update whenever another PhasmaStrap was running (the tray icon, the settings window or a game), which is most of the time.
  - The new version couldn't replace the installed one while it was running, and gave up silently. It now swaps it in anyway.
  - A failed or cut-off download was kept and retried forever. Downloads are now checked against GitHub's size and checksum.
  - If an update still fails, you now get a message instead of nothing.

## 2.17.3
- **Fixed: "FastFlag profile is not active" when it was.** PhasmaStrap now checks the flags file the open Roblox actually started with, instead of a note that could go stale when Roblox was still closing during a launch.
- **Fixed: the proxy said everything was patched when Roblox wasn't using it.**
  - The status now says whether Roblox really connected through the proxy, and tells you to restart Roblox when it was opened before its certificate bundle was patched.
  - Opening the Networking page no longer quietly creates a new certificate.
  - Every PhasmaStrap process picks up a new certificate instead of signing with an old one.
  - The "Roblox rejected the proxy certificate" notification only says "re-added" when it re-added something.
  - Fixed: the proxy would have stopped working in its certificate's second year.

## 2.17.2
- **Fixed: the settings window couldn't be resized** after it reopened maximized and was then made smaller.

## 2.17.1
- **Monitors show their real names** in the in-game resolution settings (e.g. "1. 25G3ZM · 1920x1080 @ 240Hz · main") instead of "Generic PnP Monitor", numbered like Windows' display settings.

## 2.17.0

### Overlays
- **Fixed: the overlay sometimes didn't show up.** With Instant Replay on, the replay recorder and the overlay both asked Windows to capture the screen, and a process only gets one per monitor - whichever came second failed. The overlay now uses the recorder's picture instead, and the FPS counter and crosshair don't need a screen capture at all any more.
- **FPS is now the game's real frame rate** - the frames the game actually shows, not how often the overlay redrew itself. (It can't read higher than your monitor's refresh rate.)
- **Customise the HUD** (Rendering › Overlays › HUD look and position): position (any corner or the middle of the top or bottom), distance from the edges, background opacity (down to no background), colours, size, rounded corners, one-line layout, labels on or off, text shadow - with a live preview.

### Instant Replay and screenshots
- **Clips up to 5 minutes.**
- **Exact resolution and frame rate**: 1080p always records 1920 × 1080 (even in a smaller window), and the frame rate you pick is the clip's real frame rate. 1440p added.
- **Your microphone, unprocessed**: no noise suppression or other Windows processing, and you can pick which microphone to record.
- **Thumbnails** for saved clips, and the lists update by themselves when a new clip or screenshot is saved.
- **Captures window**: every screenshot and clip in one place, with search, filters (game, date, type), sorting and **rename**.
- **Screenshot an area**: optionally, the screenshot hotkey freezes the game and you drag over the part you want.
- **Edit button** on the "Screenshot saved" and "Replay saved" notifications.
- **Make GIFs from screenshots** (screenshot editor › Make GIF): zoom, pan, or a slideshow of several screenshots.
- The Capture page is split into tabs: Screenshots, Instant Replay and Storage.
- Fixed: small or cropped screenshots saved from the editor came out black.
- With graphics card encoding, the "takes a few seconds" notification is gone - clips save instantly.

### Other fixes
- **In-game resolution** (Rendering › Performance): the monitor and resolution lists work again, and you can give a game its own resolution.
- **Discord › RPC Advanced** has settings now: join button type, custom text per game, Roblox Studio on your profile and translation.
- **The settings window reopens at the size and place you left it**, maximized if it was.
- **The News page shows what changed** - including this list.

## 2.16.1
- **Private server filters**: search by game, server name or owner's username; owner list with your friends first; game, status and sorting filters.
- **Recently played shows place names**, e.g. "Blade Ball · Pro Server", instead of the same name with different playtimes.

## 2.16.0
- **Stream-safe mode**: OBS gets its own copy of the game with chat and names hidden.
- **Low-end mode**: one click for lighter graphics and Roblox tuning, with an exact undo.
- Discord's own Join button, private servers, a Roblox version manager and an account guard.
- A new FastFlag editor with profiles and per-game flags, shorter share codes, and a crosshair editor.
