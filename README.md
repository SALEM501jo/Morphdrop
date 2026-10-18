# ShiftDrop

Convert files on Windows without opening an app: start dragging a file, press **Shift**, and drop it on a format
in the wheel that appears. The new file lands next to the original.

<p align="center"><img src="docs/wheel.png" width="360" alt="The ShiftDrop wheel over a file being dragged"></p>

> **Status: early prototype (v0.1).** The gesture, the wheel and image conversion with Windows' own codecs work.
> More formats and tools are on the roadmap below.

## What works today

- **Drag + Shift** anywhere a file can be dragged (File Explorer, the desktop) opens the wheel under the cursor.
- **Image conversion** between PNG, JPG, ICO, BMP, TIFF and GIF. It can also read WebP, HEIC and AVIF when the
  matching free codec extension from the Microsoft Store is installed.
  - Transparent areas become white in formats without transparency (JPG, BMP, GIF).
  - Photos are turned upright using their EXIF orientation.
  - ICO files hold every standard size (16–256 px) without scaling small images up.
- **Never overwrites anything**: if `photo.png` exists, you get `photo (1).png`.
- Drop on the hub (centre) or drag away to cancel. Drag a few files at once to convert them all.

## How it works

| Piece | What it does |
| --- | --- |
| [`Input/DragWatcher`](src/ShiftDrop/Input/DragWatcher.cs) | A low-level mouse hook on its own thread notices "button held + moved past the drag threshold", then watches for a Shift press. Shift held *before* the drag keeps Explorer's native meaning (force move). |
| [`Overlay/WheelWindow`](src/ShiftDrop/Overlay/WheelWindow.xaml.cs) | A topmost, non-activating, layered WPF window that is an OLE drop target. It appears under the cursor as an invisible "ghost" and only shows itself when the drag really enters it carrying files, so text selection or window moves never show it. |
| [`Actions/`](src/ShiftDrop/Actions) | What each slice does (`IFileAction`), chosen from the first file's type and run in the background. |
| [`Imaging/`](src/ShiftDrop/Imaging) | Decoding and encoding with WIC (built into Windows), plus a small ICO writer. |

ShiftDrop always answers drops with *Copy*, never *Move*, so the source never deletes your original.

## Build and run

Requirements: Windows 10 or 11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet test
dotnet run --project src/ShiftDrop
```

ShiftDrop lives in the notification area (tray). Right-click its icon to pause it, open the log, or exit.
The log is at `%LOCALAPPDATA%\ShiftDrop\shiftdrop.log`.

To render the wheel to an image without showing any window: `ShiftDrop.exe --render-wheel wheel.png`.

## Roadmap

1. ~~Gesture, wheel and built-in image conversion~~ (v0.1)
2. WebP / AVIF / HEIC output, compress and resize (Magick.NET)
3. PDF ↔ images, a second ring of tools (crop, add background, redact, metadata)
4. Video and audio (FFmpeg, downloaded on first use)
5. Settings: start with Windows, choose the modifier key, light/dark theme
6. Installer, winget and signed releases

## Known limitations

- Windows blocks drag and drop from apps running as administrator to normal apps, so the wheel can't receive those drops.
- Files dragged out of ZIP folders or phones (virtual files) aren't supported yet.

## License

[MIT](LICENSE)
