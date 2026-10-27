# Clipmunk 🐿

A fast screenshot and annotation tool for Windows, in the spirit of Snagit and Shottr.

- **Print Screen** or **Win+Shift+S** freezes the screen. **Drag** to select an area or **click** a window. A pixel magnifier shows the exact pixel and its color, and the **arrow keys** nudge the cursor 1 px (Shift: 10 px).
- A toolbar at the top (or keys **1–4**) switches between **Rectangle**, **Freeform** (draw around anything; outside the shape stays transparent), **Window** and **Full screen**, like the Snipping Tool. Clipmunk remembers the last mode.
- Win+Shift+S replaces the Snipping Tool. To give it back to Windows, untick **Win+Shift+S opens Clipmunk** in the tray menu.
- When you release, the capture is **copied to the clipboard** and **saved to your library**.
- The editor opens with tools you can change later: arrow, line, rectangle, ellipse, pen, highlighter, text, numbered steps, stickers, pixelate/redact, and crop. Every object stays selectable, movable, resizable and recolorable.
- Edits **save automatically** and update the clipboard as you go.
- The **Library** (click the tray icon) shows every screenshot, grouped by day. You can search them, double-click to re-edit (annotations are still editable), drag them into Slack/Outlook/Teams, copy them, or delete them to the Recycle Bin.

## Google Drive library

If Google Drive for desktop is installed, Clipmunk saves to `My Drive\Clipmunk` by default, so screenshots show up on every computer signed in to the same Drive. Annotations stay editable everywhere. They live in a hidden `.clipmunk` folder next to the images, which Drive syncs too.

To change the folder, use the tray menu → **Change library folder…** (or the folder button in the Library).
A **Shared drive** folder works too if your team wants a common library.

## Install (for colleagues)

1. Download `Clipmunk.zip` from the latest [release](https://github.com/fredmond0-gravis/clipmunk/releases/latest) and unzip it anywhere.
2. Double-click **Install Clipmunk.cmd**. It installs for your user only (no admin needed), adds a Start-menu shortcut, and starts Clipmunk with Windows.
3. Press **Print Screen**.

The first time you run it, Windows SmartScreen may say *"Windows protected your PC"*, because the exe isn't code-signed. Click **More info → Run anyway**.

Clipmunk needs only what ships with Windows 10/11 (.NET Framework 4.8). There is no runtime to install.

> If Print Screen still opens the Snipping Tool, turn off *Settings → Accessibility → Keyboard → "Use the Print screen key to open screen capture"*.

### Updates

From version 1.3 on, Clipmunk updates itself. It checks this repo's latest GitHub release shortly after it starts, then every 6 hours. When there's a newer version, a notification appears and **Update to Clipmunk x.y** shows at the top of the tray menu. One click downloads it, verifies its SHA-256 checksum, swaps the exe in place and restarts. Open editors are saved first.

- **Check for updates…** in the tray menu checks right away.
- Untick **Check for updates automatically** to turn the background check off.
- `Clipmunk.exe --update-now` installs the latest release without asking, which is handy for scripted rollouts.

Copies older than 1.3 have no updater, so install 1.3 once by hand.

## Keyboard shortcuts (editor)

| Key | Action | Key | Action |
|---|---|---|---|
| V | Select / move | Ctrl+C | Copy image |
| A | Arrow | Ctrl+V | Paste image or text as an object |
| L | Line | Ctrl+Z / Ctrl+Y | Undo / redo |
| R | Rectangle | Ctrl+D | Duplicate selected |
| E | Ellipse | Del | Delete selected |
| P | Pen | Arrows | Nudge selected (Shift = 10 px) |
| H | Highlighter | Ctrl+wheel | Zoom |
| T | Text (double-click text to edit) | Ctrl+0 / Ctrl+1 | Fit / 100 % |
| N | Step number | Ctrl+N | New capture |
| K | Sticker | Ctrl+L | Library |
| B | Pixelate / redact | Ctrl+Shift+S | Export as… |
| C | Crop | Shift while drawing | Square / 45° snap |
| O | Rotate image (drag; Shift = 45° steps) | Ctrl+R | Ruler |
| | | Ctrl+Shift+C | Ask Claude (new chat with this screenshot) |

**Ask Claude** (next to Copy) puts the screenshot on the clipboard and opens a new chat in the Claude desktop app, or claude.ai in your browser if the app isn't installed. It then pastes the image once Claude is in front. If the paste doesn't land (e.g. Claude was slow to start), press Ctrl+V. The ▾ menu switches between app and browser and turns auto-paste off.

**Rotate (O)**: drag anywhere on the image to turn it, and hold Shift to snap to 45° steps. Quarter turns are lossless. Any other angle enlarges the canvas and leaves the corners transparent. Lines and pen strokes turn with the image. Boxes, text, steps and stickers stay upright and move along.

**Ruler (Ctrl+R)**: an on-screen ruler marked in image pixels. It never appears in the saved image. Drag it to move it. Drag a ↻ knob or use the mouse wheel over it to turn it, and hold Shift to snap to 45°. A line or arrow you start just outside one of its long edges is laid against that edge. While you draw or resize a line, its length in pixels is shown.

Palette dots: **left-click** sets the color, **right-click** sets the fill.

## Building

You don't need an SDK or Visual Studio. The C# compiler that ships with Windows is enough:

```
build.cmd             → dist\Clipmunk.exe
package.cmd           → dist\Clipmunk.zip (exe + installer + uninstaller + this README)
```

### Releasing a new version

Every push to `main` is built by GitHub Actions; the zip is attached to the workflow run.
To publish a release colleagues can download, bump `Version` in `src/Program.cs` (and the assembly versions), then:

```
git tag v1.4.0
git push origin v1.4.0
```

The workflow builds it and creates a GitHub Release with `Clipmunk.zip` attached. Within a few hours every running Clipmunk offers the update. The tag must match the exe's version: the updater refuses a download whose version differs from the release tag.

To test the updater without publishing, set `CLIPMUNK_UPDATE_FEED` to the URL of a release JSON (a `file:///` URL works). Clipmunk will read that instead of GitHub.

### Self-test

`dist\Clipmunk.exe --selftest <folder>` renders the capture overlay, editor and library to PNGs. It also checks that annotations survive a save/reopen. It never touches your real settings or library.

## Layout

| File | What it does |
|---|---|
| `src/TrayContext.cs` | Tray icon, capture flow, library window, startup setting |
| `src/Capture.cs` | Print Screen hook, screen grab, window detection, selection overlay + magnifier |
| `src/EditorForm.cs`, `src/EditorCanvas.cs` | Editor window, tools, undo/redo, zoom, inline text |
| `src/Shapes.cs` | Editable annotation objects and their save format |
| `src/LibraryForm.cs` | Library grid (thumbnails, search, drag-out, delete) |
| `src/Storage.cs` | Settings, Google Drive detection, documents, clipboard |
| `src/Updater.cs` | Self-update from GitHub Releases |
| `src/ClaudeHandoff.cs` | "Ask Claude" — new Claude chat with the screenshot |
| `src/Theme.cs`, `src/Art.cs`, `src/Stickers.cs` | Dark theme, vector icons, stickers, app icon |

## License

MIT. See [LICENSE](LICENSE).
