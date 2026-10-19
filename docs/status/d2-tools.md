# WP5 / d2-tools — ShareX.Tools Native Linux Port Status

Status of ShareX Tools implementation on Linux (Wayland / Hyprland).

| Tool / Feature | Status | Verification | Gaps / Wayland Degradation |
| --- | --- | --- | --- |
| **Colour picker** | works | Tested `ScreenColorPicker.Sample` coordinate sampling against known test bitmap; launched `ColorPickerWindow`, rendered headlessly and grim-captured on live Hyprland desktop. | None |
| **Ruler** | works | Launched `RulerWindow`, verified layout, bounds, coordinate measurement rendering headlessly and grim-captured on live Hyprland. | None |
| **Pin to screen** | works | Fixed 64-bit window handle overflow in `TaskHelpers.cs`. Verified `PinToScreenManager.Pin` & `CloseAll`, `PinToScreenWindow` (active with real test image), `PinToScreenOptionsWindow`, and `PinToScreenStartupWindow` both headlessly and live on Hyprland. Pinned topmost state verified via `hyprctl dispatch pin`. | Non-Hyprland Wayland compositors lack standard topmost protocol; degrades gracefully with user notification. |
| **QR code** | works | Verified `QRCodeViewModel` generating QR bitmaps and scanning barcodes from test images; launched `QRCodeWindow` headlessly and live on Hyprland. | None |
| **Hash checker** | works | Verified `HashCheckerViewModel` hashing test byte buffers across all supported algorithms (MD5, SHA1, SHA256, SHA384, SHA512, CRC32) and matching hash text; launched `HashCheckerWindow` headlessly and live on Hyprland. | None |
| **OCR (Tesseract)** | works | Verified `OCRHelper.AvailableLanguages`, `OCRHelper.OCR` pipeline, auto-copy to clipboard, and `OCRWindow` previewing test image and OCR results; captured headlessly and via grim on live Hyprland. | None |
| **Inspect window** | partial | Fixed 64-bit compositor address parsing. Verified `InspectWindowViewModel` enumerating live compositor windows via Hyprland IPC (`hyprctl clients -j`) and `DesktopTools.GetWindows()`; launched `InspectWindowWindow` and `InspectWindowPickerOverlay`. | On Wayland, compositors do not expose child control hierarchies (Win32 HWND controls); degrades with informative UI message explaining child controls cannot be inspected on Wayland. |
| **Directory indexer** | works | Verified `DirectoryIndexerViewModel` generating directory listings for test trees across all formats (HTML, JSON, XML, TXT, CSV); launched `DirectoryIndexerWindow` headlessly and live on Hyprland. | None |
| **Image combiner** | works | Verified `SkiaImageHelpers.CombineImages` (Horizontal and Vertical layouts) using real test images; verified `ImageCombinerViewModel` and `ImageCombinerWindow` headlessly and live on Hyprland. | None |
| **Image splitter** | works | Verified `SkiaImageHelpers.SplitImage` slicing images into a 2x2 grid of image tiles; verified `ImageSplitterViewModel` and `ImageSplitterWindow` headlessly and live on Hyprland. | None |
| **Image thumbnailer** | works | Verified `SkiaImageHelpers.CreateThumbnail` generating thumbnails; verified `ImageThumbnailerViewModel` and `ImageThumbnailerWindow` headlessly and live on Hyprland. | None |
| **Image resizer** | works | Verified Skia resizing and `ImageResizerWindow` headlessly and live on Hyprland. | None |
| **Image converter** | works | Verified multi-format conversion (PNG, JPEG, WebP, BMP) and `ImageConverterWindow` headlessly and live on Hyprland. | None |
| **Image comparer** | works | Verified pixel diff computation between test images and `ImageComparerWindow` headlessly and live on Hyprland. | None |
| **Image watermark** | works | Verified Skia watermark text and image overlays, and `ImageWatermarkWindow` headlessly and live on Hyprland. | None |
| **Image analyzer** | works | Verified `AnalyzeImageViewModel` image stats analysis, `AnalyzeImageWindow`, and `AnalyzeImageOptionsWindow` headlessly and live on Hyprland. | None |
| **Metadata viewer & stripper** | works | Verified `MetadataService.ReadMetadataAsync` on PNG/JPEG and `StripMetadataAsync` (EXIF stripping with image fidelity preserved); verified `MetadataWindow` headlessly and live on Hyprland. | None |
| **Icon converter** | works | Verified ICO encoding from test images and `IconConverterWindow` headlessly and live on Hyprland. | None |
| **Animated GIF maker** | works | Verified FFmpeg GIF creation from image sequences and `AnimatedGifMakerWindow` headlessly and live on Hyprland. | None |
| **Animated GIF trimmer** | works | Verified FFmpeg GIF trimming and `AnimatedGifTrimmerWindow` headlessly and live on Hyprland. | None |
| **Video converter** | works | Verified FFmpeg video transcoding and `VideoConverterWindow` headlessly and live on Hyprland. | None |
| **Video thumbnailer** | works | Verified FFmpeg video contact sheet generation and `VideoThumbnailerWindow` headlessly and live on Hyprland. | None |
| **Video player** | works | Verified `MediaPlayer` backed by native `libmpv` (loading video, playback position advance, pause, seek, frame-by-frame stepping, audio muting/volume, and frame extraction); verified `VideoPlayerWindow` headlessly and live on Hyprland. | None |
| **Background remover** | works | Verified ONNX Runtime CPU inference (`U2Netp` / `BiRefNet`) on test images with cache persistence; verified `BackgroundRemoverWindow` headlessly and live on Hyprland. | None |
| **Borderless window** | works | Verified `BorderlessWindowManager.ToggleBorderlessWindow` toggling fullscreen mode via Hyprland IPC roundtrip; verified `BorderlessWindowWindow` headlessly and live on Hyprland. | Requires window manager IPC support (Hyprland / X11). |
| **Mouse highlighter** | works | Verified `MouseHighlighterService` spawning halo overlay window and tracking coordinates; verified `MouseHighlighterWindow` settings headlessly and live on Hyprland. | Global mouse hook relies on Linux evdev / desktop environment hooks. |
| **Monitor test** | works | Verified `MonitorTestViewModel` cycling all 5 monitor test patterns/modes and `MonitorTestWindow` headlessly and live on Hyprland. | None |
| **Network monitor** | works | Verified localhost socket connectivity probe and `NetworkMonitorWindow` headlessly and live on Hyprland. | None |
| **Clipboard viewer** | works | Verified `ClipboardViewerViewModel` format enumeration (text/plain, UTF8_STRING, UnicodeText) and preview; verified `ClipboardViewerWindow` headlessly and live on Hyprland. | None |
| **Remote storage browser** | works | Verified `RemoteStorageBrowserViewModel` directory navigation, folder expansion, parent path traversal, and `RemoteStorageBrowserWindow` headlessly and live on Hyprland. | None |

## Key Fixes & Linux Port Details
1. **64-Bit Pointer & Address Handling in Hyprland (`TaskHelpers.cs`):**
   - Hyprland window handles are 64-bit pointers (e.g. `0x5de8c8a14380`). Calling `handle.ToInt32()` threw `System.OverflowException`.
   - Updated window handle checks to `handle != IntPtr.Zero` and formatted addresses as `0x{handle.ToInt64():x}`.
   - Wired Hyprland IPC `pin` dispatcher (`hyprctl dispatch pin address:<addr>`) for `MakeActiveWindowTopMost`.
   - Added graceful notification fallback on non-Hyprland Wayland environments where topmost dispatch is unsupported.
2. **Access Modifiers for Cross-Project Linux Helpers (`LinuxCommand.cs`):**
   - Changed `ShareX.HelpersLib.Linux.LinuxCommand` to `public static` so window management and IPC dispatch can be reused from `ShareX` and `ShareX.ScreenCaptureLib`.
3. **Graceful Wayland Degradation for Window Inspection:**
   - Under Wayland, client processes are sandboxed from inspecting internal widget trees of other windows.
   - `InspectWindow` accurately enumerates compositor windows with geometry, title, and process PID via Hyprland IPC (`hyprctl clients -j`), while gracefully presenting an informative banner that child controls cannot be queried on Wayland.
4. **Comprehensive Automated Test Suite (`tests/ShareX.Linux.Tools.Tests`):**
   - Built a comprehensive test harness covering all 30 tools and viewmodels, including real Skia image manipulation, FFmpeg video conversions, OCR language checks, Hash checks, QR codes, libmpv playback, ONNX inference, and Hyprland IPC roundtrips.
   - Verified that all 35 tool windows instantiate, layout, and render cleanly both headlessly (Avalonia Headless) and live on Hyprland with `grim` screen captures.
