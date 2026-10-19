# Area c-recording: Screen recording end to end

Status of screen recording implementation on Linux (Wayland / Hyprland).

| Feature | Status | Verification | Gaps |
| --- | --- | --- | --- |
| MP4 Region recording | works | Verified with `tests/ShareX.Recording.Smoke -- mp4` and `ffprobe`: 640x360 @ 15fps, 4.07s duration, H.264 video. Extracted frame visually verified with `view_file`. | None |
| MP4 Fullscreen recording | works | Verified with `tests/ShareX.Recording.Smoke -- fullscreen` and `ffprobe`: 2560x1440 @ 15fps, 4.07s duration, H.264 video matching physical monitor output. Extracted frame matches desktop layout. | None |
| GIF output | works | Verified with `tests/ShareX.Recording.Smoke -- gif` and `ScreenRecordManager` UI flow: 640x360 @ 15fps, palettegen/paletteuse dual-pass dither filters applied. | None |
| Pause / Resume | works | Verified both via `ScreenRecorder` multi-segment capture and `ScreenRecordWindow` UI button: elapsed timer freezes while paused, resume continues cleanly, segments seamlessly concatenated via FFmpeg concat protocol. | None |
| Stop | works | Verified interactive stop (`recorder.RequestStop()`), stop while paused (`stop-paused`), and UI stop button; recording terminates immediately and produces valid finalized media. | None |
| Cursor on / off | works | Verified `IncludeCursor = true` (passes `-c` to grim) vs `IncludeCursor = false` (omits `-c`); extracted frames confirmed. | None |
| System audio + Microphone tracks | works | Verified audio capture via PulseAudio / PipeWire (`effect_input.sony_mv1_eq.monitor` sink and `Mono_Mic` source). Multi-track audio mixed via `amix` filter into stereo AAC stream, synchronized with video. | None |
| Window recording | works | Verified with `hyprctl clients -j` window address parsing (`0x...` hex conversion); grim targets exact window geometry (1272x1428) across segments; graceful stop and finalization if window closes mid-recording. | XWayland window IDs use logical coordinates when captured through grim |
| FFmpeg custom arguments & presets | works | Verified custom arguments mode (`$fps$`, `$output$`), codec presets `h264_nvenc`, `libx264`, `libx265` (`hevc` / `tag:v hvc1`), `libvpx-vp9`, and GIF palette configuration. Fast stream-copy (`-c copy`) used when transcoding is unnecessary. | None |
| Duration limit | works | Verified automatic stop at exact duration (tested 2s in `ScreenRecordManager` and 5.00s in `ShareX.Recording.Smoke -- duration`). | None |
| Recording bar & Countdown UI | works | Verified `FFmpegOptionsWindow`, `ScreenRecorderBarWindow` (setup bar), `RecordingOutlineWindow` (dashed region border), and `ScreenRecordWindow` (countdown & active recording controls). Visual snapshots taken via `grim` (`options.png`, `setup.png`, `countdown.png`, `controls.png`). | Hyprland floating window positioning applies logical pixel coordinates |

## Key Fixes Applied
1. **Hardware Acceleration (NVENC probe & format):**
   - Fixed encoder probe frame dimension in `ScreenRecorder.SelectEncoder` from 64x64 to 256x256 (Nvidia NVENC requires minimum 146x146, error code 234).
   - Added automatic fallback to `libx264` when recording regions smaller than NVENC hardware minimums.
   - Added explicit `-pix_fmt yuv420p` to NVENC capture segments to avoid default planar RGB (`gbrp`) which fails in web players.
2. **Window Handle & Geometry Tracking:**
   - Handled `0x` hex prefixes in `RecordingTargets.ActiveWindow` and `RecordingTargets.Window`.
   - Handled empty `{}` JSON from `hyprctl activewindow -j`.
   - Added try-catch in window tracking loop so if the target window closes, the recorder gracefully stops and finalizes the recording instead of crashing.
3. **FFmpeg Concatenation & Finalization:**
   - For single-segment recordings without custom transcoding, use `-c copy` and `-movflags +faststart` for instant lossless output.
   - For custom commands, copy single segments directly or concat multi-segments without overwriting custom codec parameters.
   - Fixed codec preset conflicts by avoiding duplicate `-c:v libx264` arguments when extra arguments specify other codecs (e.g., `libx265`, `libvpx-vp9`).
   - Wired GIF options (`GIFStatsMode`, `GIFDither`, `GIFBayerScale`) into FFmpeg palette filters in `ScreenRecordingOptions.cs`.
4. **Recording Bar and Outline UI on Wayland:**
   - Set distinct title on `RecordingOutlineWindow` (`"ShareX - Recording Region Outline"`) and removed bar title overwrite so window lookup and placement in Hyprland do not confuse the outline with the control bar.
   - Added window lookup retry in `RecordingWindowPlatform.Place` to reliably match new windows in `hyprctl clients -j`.
   - Guarded against zero/negative dimensions in `ScreenRecorderBarWindow.PositionNearRegion`.
   - Updated `FFmpegOptionsWindow` file picker pattern from Windows `"*.exe"` to Linux `["*"]`.
5. **Clean Process Lifecycle:**
   - Ensured all background ffmpeg and grim subprocesses terminate upon stop/pause/abort without orphan processes.
