# Image Editor, Effects, and System.Drawing Audit Status (h-editor)

## Summary
- **System.Drawing.Common Package Audit**: COMPLETED. Removed `System.Drawing.Common` dependency completely. All runtime uses replaced with SkiaSharp. Clean build with 0 errors across the solution and test projects.
- **ShareX.ImageEffectsLib**: IN PROGRESS. Auditing and verifying all image effects (filters, adjustments, drawings, manipulations).
- **ShareX.ImageEditor**: PENDING. Verifying editor tools, after-capture pipeline, saving/copying/uploading/pinning/printing, Avalonia.Headless tests, live runs.
- **Fonts**: PENDING. Linux fontconfig rendering, font fallbacks, font pickers.

---

## Detailed Status Matrix

| Component / Feature | Status | Verification Method | Known Gaps / Notes |
|---|---|---|---|
| `System.Drawing.Common` audit & removal | Works | `Directory.Packages.props` & `ShareX.HelpersLib.csproj` package reference removed; solution & test suites rebuilt with 0 errors | Kept `System.Drawing.Primitives` (Size/Point/Rectangle/Color from net10.0 runtime) |
| Image Effects (Adjustments) | Pending | To be verified via automated test suite | |
| Image Effects (Filters) | Pending | To be verified via automated test suite | |
| Image Effects (Drawings & Watermarks) | Pending | To be verified via automated test suite | |
| Image Effects (Manipulations) | Pending | To be verified via automated test suite | |
| Image Editor - Tools (Shapes, Arrow, Text, Blur, Pixelate, Highlight, Step, Freehand, Crop, Resize, Cursor, Emoji, Image Insert) | Pending | Avalonia.Headless tests + live runs | |
| Image Editor - Undo/Redo | Pending | Automated headless test | |
| Image Editor - Export/Save (PNG, JPG, WEBP, GIF) | Pending | File output checks | |
| Image Editor - Clipboard (Copy to wl-clipboard) | Pending | wl-paste check | |
| Image Editor - Actions (Upload, Pin, Print, Wallpaper) | Pending | Integration checks | |
| Fonts - FontConfig rendering & fallbacks | Pending | Headless + visual inspection | |
| Fonts - Font pickers listing installed fonts | Pending | Font manager check | |
