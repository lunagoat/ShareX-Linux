# Bloat cleanup report

Scope: conservative static cleanup of the Linux port, with no changes to the new overlay projects. Read `docs/LINUX_PORT.md` and `docs/COMPATIBILITY.md`; the task's stricter no-display/no-release/no-network-except-restore rules took precedence. No app or published executable was launched. Only the requested headless test runners were executed.

## Changes

1. **Fix real build warnings.** `ShareX.Avalonia/Linux/LinuxScreenCapture.cs` and `NiriIpc.cs`: annotate the two nullable locals instead of assigning nullable values to non-nullable variables (four CS8600 diagnostics). `ScreenshotPortal.cs`: use the supported `Notification<T>` signal callback, checking `HasValue` before accessing `Exception`, with the original observer flags, context, result and error behavior. `tests/ShareX.Startup.Tests/Program.cs`: select the explicit PNG encoder overload (CS0618). That file and `tests/ShareX.Integration.Tests/Program.cs`: implement the two required tray interface events with empty accessors; these fakes never raise them (four CS0067 diagnostics). Estimated production-size effect: negligible; no runtime bug or public behavior change intended.
2. **Delete six unreferenced helper files.** `ShareX.HelpersLib/PingHelper.cs`, `PingResult.cs`, `TaskEx.cs`, `Native/WshShell.cs`, `Native/WindowsNativeWindow.cs`, and `ShareX.ImageEditor/Core/ImageEffects/Helpers/TypeExtensions.cs`. Searches found no external call sites or name-based lookups; WSH class/interface GUIDs also had no other references. These types are not uploader/effect subclasses discovered by reflection. Compiler validation after deletion passed. The last file contained unused enum/type description and string-truncation helpers duplicating helpers in `ShareX.HelpersLib/Extensions/{EnumExtensions,Extensions,StringExtensions}.cs`; the used implementations remain. Estimated effect: small reduction in IL, ReadyToRun code and metadata; measured only in the aggregate below.
3. **Remove confirmed unused private members.** Screen-recording/scrolling windows: three obsolete region-combination constants. OAuth/Dropbox/Puush: four unused endpoint/signature constants. Editor window/view/controllers: five unused private helper methods, an unused zoom-step constant and its now-orphaned arrow cursor field. Screen color picker: remove the write-only `_complete` field and assignment; event handlers still capture the constructor callback. Exact files/members appear below. Estimated effect: small code/metadata reduction and one fewer field per picker; no behavior change.
4. **Remove 63 unnecessary using directives in 40 files.** IDE0005 identified 59; three further imports duplicated generated global usings, and removing the unused version helper also orphaned its reflection import. Exact file/directive inventory below. Estimated published-size effect: zero. Required imports were checked by a clean compiler rebuild; analyzer advice alone was not accepted as proof.
5. **Remove commented-out code.** `ShareX.HelpersLib/SkiaColorMatrixManager.cs`: obsolete alternate grayscale constants. `ShareX.HelpersLib/UpdateChecker/GitHubUpdateChecker.cs`: three commented model properties. Comments documenting behavior and license blocks remain. Estimated binary-size effect: zero.
6. **Remove redundant/orphan package declarations.** `ShareX.HistoryLib/ShareX.HistoryLib.csproj`: remove direct `Microsoft.Data.Sqlite.Core` reference because `Microsoft.Data.Sqlite` already depends on it. `Directory.Packages.props`: remove its redundant version pin and unused `Vortice.Direct3D11` / `Vortice.MediaFoundation` versions (their source consumers are excluded `LegacyWindows` files). Restore produced exactly the same 69 resolved application libraries and versions. Measured dependency-graph effect: zero; estimated binary-size effect: zero. No project references were removed.

## Findings retained

1. **XAML callbacks:** `ShareX.HistoryLib/Views/HistoryWindow.axaml.cs:OnDateFilterChanged` and `ShareX.ImageEditor/Presentation/Views/EditorBuiltInToolbars.axaml.cs:OnZoomChanged`. IDE0051 reports both, but their `.axaml` files reference them. Deleting either would break markup behavior.
2. **Lifetime/concurrency fields:** `ShareX.Tools/Tools/NetworkMonitor/NetworkMonitorViewModel.cs:_monitoringTask` and `ShareX.NativeOverlay/Wayland/WaylandOverlaySession.cs:registry` are IDE0052 findings. Keep the task/native-proxy ownership references rather than assuming write-only fields have no lifetime significance; the native overlay is also explicitly protected scope.
3. **Unused parameters:** 65 IDE0060 findings remain (inventory below). Most are compatibility signatures, callbacks, command/provider APIs or currently ignored rendering options. Removing parameters is not dead-member deletion and can change source/API compatibility or mask incomplete behavior, so no signature refactoring was attempted.
4. **Overlay imports:** eight IDE0005 findings in `ShareX.OverlayAvaloniaLayer` remain to keep the new overlay projects untouched. Their unused imports have no published-size cost.
5. **Reflection-discovered types:** uploader service descriptors/custom uploader functions and editor/image-effect implementations have few or no direct type-name references. `Helpers.GetInstances<T>`, `UploaderFactory`, `ShareXCustomUploaderSyntaxParser`, and `DiscoveredEffectRegistry` instantiate them through reflection. Retained all of them. Extension classes also cannot be judged by references to the declaring class name.
6. **Required large/Windows-related dependencies:** ONNX Runtime is used by background removal. Vortice.Direct2D1 is referenced by `WindowsEmojiBitmapRenderer`, whose Linux Skia fallback and callers remain. ProtectedData supplies the public `DataProtectionScope` parameter type in the Linux DPAPI compatibility API. SystemEvents is used by guarded Windows tray theme subscriptions. Avalonia desktop/theme/font packages include backend and XAML/resource behavior. No further runtime package removal was proven safe.
7. **Project references:** retained all references after inspecting the project files. References satisfied through another project are not automatically unused; the native messaging host's `ReferenceOutputAssembly=false` edge is an intentional build dependency. Removing such edges would not prove a runtime assembly is removable.
8. **Windows/legacy code:** retain referenced `WindowsTrayIcon`, shared native declarations/event argument types, and the explicitly excluded `ShareX.ScreenRecordingLib/LegacyWindows` reference implementation. `ShareX.Setup` / `ShareX.Steam` are Windows packaging projects the port guidance explicitly says to leave alone. `NativeMethods.GetFileThumbnail` has no current caller and contains a Windows-only COM cleanup warning; the broader interop family was not pruned without a complete dependency audit. No active Linux failure was established. RegistryHive/DataProtectionScope enum warnings are compatibility-value uses, not Windows API execution; Unix permission warnings are valid Linux calls. CA1416 was neither suppressed nor otherwise changed.
9. **Other class-name-only candidates:** `ShareX/Linux/BrowserIntegration.cs`, `ShareX.HelpersLib/Media/FFmpegDownloader.cs`, `FFmpegGitHubDownloader.cs`, `Cryptographic/Translator.cs`, and `Settings/SafeStringEnumConverter.cs` warrant a separate feature/serialization audit. Name-only absence was not considered sufficient evidence to remove integration or compatibility surfaces. Retained rather than speculate about their intended callers.
10. **Resources, images and localization:** no removals. Cursor/flag/font assets, generated resource properties, XAML URIs and dynamically composed localization keys need a resource-aware reachability audit. In particular `EffectBrowserLocalization` and `ImageEffectsLocalization` call `ResourceManager.GetString` with constructed keys. The now-unreferenced PingResult localization properties/catalog entries were left with their generated catalogs. No clear resource deletion was established across all dynamic lookup paths.
11. **Publish warnings:** existing IL3000/IL3002 assembly-file/location diagnostics in overlay helper/debug-log code were retained; changing them would be a separate behavior fix and the overlay behavior is protected. This cleanup does not certify live desktop behavior.

## Validation and size

Environment: .NET SDK `10.0.402`, Linux x64, baseline Git HEAD `28cbe1f31`, 2026-10-20. Display/compositor/session-bus variables were unset for all test runs; the integration runner used `--headless` and a private `/tmp` runtime directory.

| Check | Result |
| --- | --- |
| `source scripts/linux/env.sh; dotnet build ShareX.sln -c Release` | Baseline and post-cleanup builds: 0 errors |
| Clean rebuild with `-warnaserror:CS0067,CS8600,CS0618,CS0169,CS0414,CS0649` | 0 errors; all requested warning classes clear |
| Final-source build with the same warnings-as-errors gate | 0 errors (incremental output: 0 warnings) |
| `tests/ShareX.OverlayHost.Tests/run.sh` | PASS, 121 checks |
| `tests/ShareX.OverlayRender.Tests/run.sh` | PASS, rendering equivalence/damage/pixel suite |
| `tests/ShareX.OverlayIntegration.Tests/run.sh --headless` | PASS, production request/controller and fake-helper fallback checks |
| `dotnet run --project tests/ShareX.Compositor.Tests -c Release` | PASS, 108 checks, 0 failures |
| Final IDE0051 / IDE0052 / IDE0005 audit | Only 2 markup callbacks, 2 lifetime fields and 8 protected overlay imports remain; verifier exits 2 for these intentionally retained findings |
| IDE0060 audit | 65 retained parameter findings, listed below |
| NuGet application library/version comparison | Normal-build restore: 69 resolved libraries before/after; no additions, removals or version changes |
| `git diff --check` | PASS |

Publishing additionally resolves the build-only `Microsoft.NET.ILLink.Tasks/10.0.13` package (70 library entries in publish assets); it is absent from the runtime dependency manifest. This profile-specific tooling entry is not a runtime dependency change from this cleanup.

The baseline solution build had 60 warnings: 4 CS8600, 2 CS0618, 4 CS0067 and 50 CA1416. The clean warning gate had the same 50 CA1416 warnings and no targeted compiler warnings. CA1416 was not hidden by the cleanups; incremental builds can print fewer warnings because unchanged projects are skipped.

Both size measurements use **`scripts/linux/publish.sh`**, unchanged: self-contained `linux-x64`, single-file, ReadyToRun enabled, compression/trimming disabled, no symbols. The executable was measured with `stat -c %s` and was never executed. No install or release script was run.

| `dist/linux-x64/ShareX` | Bytes |
| --- | ---: |
| Before | 243,507,045 |
| After | 243,478,820 |
| Reduction | **28,225** (27.56 KiB; **0.0116%**) |

This is an aggregate measurement, not a per-change attribution. Import/comment edits and dependency-declaration cleanup are expected to save no executable bytes; the small reduction is consistent with removing compiled dead code. The large runtime/native dependencies remain required, so no meaningful multi-megabyte reduction is claimed. No runtime bug fixes were introduced; existing headless regression suites were used to check preservation of behavior. Live desktop behavior was deliberately not exercised.

## Commit limitation

The first requested commit attempt failed with `fatal: Unable to create .../.git/index.lock: Read-only file system`. All edits remain uncommitted, as explicitly allowed by the task. No alternate clone/worktree or Git metadata was written. Intended separate commits:

- Fix nullable and obsolete API warnings and unused test events
- Remove unused private helpers and constants
- Remove unreferenced helper and Windows interop files
- Remove compiler-confirmed unnecessary imports
- Remove commented-out helper code
- Remove redundant SQLite and orphan package declarations
- Document conservative bloat audit and validation

Each intended message ends with:

```text
Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Private member inventory

| File | Removed member(s) | Evidence |
| --- | --- | --- |
| `ShareX.ScreenCaptureLib/Presentation/ScreenRecording/ScreenRecordWindow.axaml.cs` | `RegionOr`, `RegionDiff` | IDE0051; no other reference |
| `ShareX.ScreenCaptureLib/Presentation/ScrollingCapture/ScrollingCaptureRegionWindow.axaml.cs` | `RegionDiff` | IDE0051; no other reference |
| `ShareX.UploadersLib/OAuth/OAuthManager.cs` | `PlainTextSignatureType` | IDE0051; no string/reflection reference |
| `ShareX.UploadersLib/FileUploaders/Dropbox.cs` | `URLNotify` | IDE0051; no other reference |
| `ShareX.UploadersLib/FileUploaders/Puush.cs` | `PuushAPIHistoryURL`, `PuushAPIThumbnailURL` | IDE0051; no other reference |
| `ShareX.ImageEditor/Presentation/Views/EditorWindow.axaml.cs` | `GetVersionString` | IDE0051; no code/markup/reflection reference |
| `ShareX.ImageEditor/Presentation/Controllers/EditorZoomController.cs` | `GetDefaultCanvasCursor`, `ZoomStep`, `ArrowCursor` | IDE0051; cursor field only used by deleted helper |
| `ShareX.ImageEditor/Presentation/Controllers/EditorSelectionController.cs` | `GetDefaultCanvasCursor` | IDE0051; no code/markup/reflection reference |
| `ShareX.ImageEditor/Presentation/Views/EditorView.axaml.cs` | `SKColorToAvalonia` | IDE0051; no code/markup/reflection reference |
| `ShareX.ImageEditor/Presentation/Views/EditorView.ToolbarHandlers.cs` | `ApplyHighlightAlpha` | IDE0051; no code/markup/reflection reference |
| `ShareX.Tools/Linux/ScreenColorPicker.cs` | `_complete` | IDE0052; constructor argument remains captured by live handlers |

## Unnecessary import inventory

Each entry removes only imports; some files also have changes listed above. No binary-size saving is attributed to imports.

| File | Removed imports |
| --- | --- |
| `ShareX.Avalonia/Linux/DoctorDisplays.cs` | `System`, `System.Collections.Generic`, `System.IO`, `System.Linq` |
| `ShareX.Avalonia/Linux/LinuxCaptureOverlay.cs` | `System.Diagnostics` |
| `ShareX.Avalonia/Linux/X11CaptureWindows.cs` | `System.Text` |
| `ShareX.HelpersLib/Cryptographic/DPAPI.cs` | `System` |
| `ShareX.HelpersLib/Helpers/FileHelpers.cs` | `System.Diagnostics` |
| `ShareX.HelpersLib/Helpers/Helpers.cs` | `Microsoft.Win32`, `System.Media`, `System.Security.Principal` |
| `ShareX.HelpersLib/Input/InputManager.cs` | `System`, `System.Runtime.InteropServices`, `System.Text` |
| `ShareX.HelpersLib/Linux/DesktopEntry.cs` | `System` |
| `ShareX.HelpersLib/Linux/LinuxGlobalShortcutsPortal.cs` | `System.Linq` |
| `ShareX.HelpersLib/Native/NativeMethods_Helpers.cs` | `System.Text` |
| `ShareX.HistoryLib/Models/ImageHistoryModels.cs` | `DrawingSize = System.Drawing.Size` |
| `ShareX.ImageEditor/Linux/CursorBitmapRenderer.cs` | `System` |
| `ShareX.ImageEditor/Presentation/Controllers/EditorZoomController.cs` | `ShareX.ImageEditor.Core.Annotations` |
| `ShareX.ImageEditor/Presentation/Views/EditorView.ImageInsert.cs` | `System.Runtime.InteropServices` |
| `ShareX.ImageEditor/Presentation/Views/EditorWindow.axaml.cs` | `System.Reflection` |
| `ShareX.NativeMessagingHost/Linux/Program.cs` | `System.Threading`, `System.Threading.Tasks`, `ShareX.HelpersLib` |
| `ShareX.ScreenCaptureLib/Linux/RecordingOutlineWindow.cs` | `System` |
| `ShareX.ScreenCaptureLib/Linux/ScreenRecorder.cs` | `Image = SkiaSharp.SKBitmap` |
| `ShareX.ScreenCaptureLib/Presentation/ScreenRecording/ScreenRecordWindow.axaml.cs` | `System.Runtime.InteropServices` |
| `ShareX.ScreenCaptureLib/ScreenRecording/ScreenRecordingOptions.cs` | `System.Linq` |
| `ShareX.Tools/Linux/BorderlessWindowManager.cs` | `System.Globalization` |
| `ShareX.Tools/Tools/InspectWindow/InspectWindowService.cs` | `ShareX.HelpersLib`, `SkiaSharp`, `System.Runtime.InteropServices`, `ImageFormat = SkiaSharp.SKEncodedImageFormat` |
| `ShareX.Tools/Tools/InspectWindow/InspectWindowViewModel.cs` | `ShareX.HelpersLib` |
| `ShareX.Tools/Tools/MonitorTest/MonitorTestWindow.axaml.cs` | `ShareX.HelpersLib` |
| `ShareX.Tools/Tools/MouseHighlighter/MouseHighlighterService.cs` | `ShareX.HelpersLib` |
| `ShareX.Tools/Tools/Ruler/ScreenPixelBuffer.cs` | `ShareX.HelpersLib` |
| `ShareX.Tools/Tools/VideoThumbnailer/VideoThumbnailer.cs` | `System.Diagnostics` |
| `ShareX.UploadersLib/Linux/OAuthListener.cs` | `System.IO`, `System.Threading`, `System.Threading.Tasks` |
| `ShareX/CaptureHelpers/CaptureLastRegion.cs` | `Bitmap = SkiaSharp.SKBitmap` |
| `ShareX/CaptureHelpers/CaptureRegion.cs` | `System.Collections.Generic`, `System.Linq` |
| `ShareX/Linux/HotkeyNames.cs` | `ShareX.HelpersLib` |
| `ShareX/Linux/LinuxHotkeyBindingWindow.cs` | `System.Linq` |
| `tests/ShareX.Integration.Tests/MediaChecks.cs` | `ShareX.HelpersLib` |
| `tests/ShareX.Integration.Tests/NetworkChecks.cs` | `ShareX.HistoryLib` |
| `tests/ShareX.Integration.Tests/Program.cs` | `Newtonsoft.Json` |
| `tests/ShareX.Linux.Capture.Tests/E2EChecks.cs` | `ShareX.ScreenCaptureLib.Presentation.RegionCapture`, `System.Globalization` |
| `tests/ShareX.Linux.Capture.Tests/UiChecks.cs` | `Avalonia.Threading`, `ShareX.ScreenCaptureLib` |
| `tests/ShareX.Linux.Tools.Tests/Program.cs` | `System.Text` |
| `tests/ShareX.OverlayRender.Tests/Program.cs` | `ShareX.OverlayRender` |
| `tests/ShareX.Startup.Tests/Program.cs` | `ShareX.AvaloniaUI.Controls`, `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Threading`, `System.Threading.Tasks` |

## Retained analyzer parameter inventory

The unused-parameter diagnostics were reviewed, not promoted wholesale to errors: API/event signatures and rendering/provider semantics need separate decisions.

| File | Diagnostic |
| --- | --- |
| `ShareX/Linux/IntegrationHelpers.cs` | Remove unused parameter 'showInApp' if it is not part of a shipped public API |
| `ShareX/TaskHelpers.cs` | Remove unused parameter 'taskSettings' if it is not part of a shipped public API |
| `ShareX/Presentation/MainWindow/MainWindow.axaml.cs` | Remove unused parameter 'statuses' |
| `ShareX.HistoryLib/Views/HistoryWindow.axaml.cs` | Remove unused parameter 'e' |
| `ShareX.HistoryLib/Views/HistoryWindow.axaml.cs` | Remove unused parameter 'sender' |
| `ShareX.ScreenCaptureLib/Linux/ScreenRecorder.cs` | Remove unused parameter 'screenshot' if it is not part of a shipped public API |
| `ShareX.ScreenCaptureLib/Linux/ScreenRecorder.cs` | Remove unused parameter 'quality' if it is not part of a shipped public API |
| `ShareX.ScreenCaptureLib/Presentation/FFmpegOptions/FFmpegOptionsWindow.axaml.cs` | Remove unused parameter 'selectRecorderDevices' |
| `ShareX.HelpersLib/Cryptographic/DPAPI.cs` | Remove unused parameter 'scope' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Cryptographic/DPAPI.cs` | Remove unused parameter 'scope' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Cryptographic/DPAPI.cs` | Remove unused parameter 'scope' if it is not part of a shipped public API |
| `ShareX.HelpersLib/CursorData.cs` | Remove unused parameter 'background' if it is not part of a shipped public API |
| `ShareX.HelpersLib/CursorData.cs` | Remove unused parameter 'destination' if it is not part of a shipped public API |
| `ShareX.HelpersLib/CursorData.cs` | Remove unused parameter 'offset' if it is not part of a shipped public API |
| `ShareX.HelpersLib/CursorData.cs` | Remove unused parameter 'destination' if it is not part of a shipped public API |
| `ShareX.HelpersLib/CursorData.cs` | Remove unused parameter 'offset' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Native/WindowInfo.cs` | Remove unused parameter 'x' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Native/WindowInfo.cs` | Remove unused parameter 'height' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Native/WindowInfo.cs` | Remove unused parameter 'y' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Native/WindowInfo.cs` | Remove unused parameter 'insertAfter' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Native/WindowInfo.cs` | Remove unused parameter 'width' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Linux/TaskbarManager.cs` | Remove unused parameter 'currentValue' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Linux/TaskbarManager.cs` | Remove unused parameter 'state' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Linux/TaskbarManager.cs` | Remove unused parameter 'maximumValue' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Linux/RegistryHelpers.cs` | Remove unused parameter 'view' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Linux/LinuxNativeMethods.cs` | Remove unused parameter 'handle' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Linux/DWMManager.cs` | Remove unused parameter 'enable' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Linux/DesktopIconManager.cs` | Remove unused parameter 'show' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Helpers/ClipboardHelpers.cs` | Remove unused parameter 'checkContainsImage' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Helpers/ClipboardHelpers.cs` | Remove unused parameter 'checkContainsText' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Helpers/ClipboardHelpers.cs` | Remove unused parameter 'checkContainsFileDropList' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Helpers/SkiaImageHelpers.cs` | Remove unused parameter 'interpolationMode' if it is not part of a shipped public API |
| `ShareX.HelpersLib/Helpers/AvaloniaClipboard.cs` | Remove unused parameter 'dib' |
| `ShareX.UploadersLib/Presentation/DestinationSettings/DestinationSettingsPageBuilder.cs` | Remove unused parameter 'owner' |
| `ShareX.NativeMessagingHost/Linux/Program.cs` | Remove unused parameter 'args' |
| `ShareX.ImageEditor/Presentation/Views/EditorBuiltInToolbars.axaml.cs` | Remove unused parameter 'sender' |
| `ShareX.ImageEditor/Presentation/Effects/EffectDefinition.cs` | Remove unused parameter 'customEditorKey' if it is not part of a shipped public API |
| `ShareX.ImageEditor/Core/ImageEffects/Helpers/ImageHelpers.cs` | Remove unused parameter 'sampling' if it is not part of a shipped public API |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/CelticKnotBorderImageEffect.cs` | Remove unused parameter 'border' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/ChromeMetallicBorderImageEffect.cs` | Remove unused parameter 'newW' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/ChromeMetallicBorderImageEffect.cs` | Remove unused parameter 'newH' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/ChromeMetallicBorderImageEffect.cs` | Remove unused parameter 'newW' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/ChromeMetallicBorderImageEffect.cs` | Remove unused parameter 'newH' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/CircuitBoardBorderImageEffect.cs` | Remove unused parameter 'viaRingPaint' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/CircuitBoardBorderImageEffect.cs` | Remove unused parameter 'border' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/FilmStripFrameImageEffect.cs` | Remove unused parameter 'border' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/FrostIceBorderImageEffect.cs` | Remove unused parameter 'newH' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/FrostIceBorderImageEffect.cs` | Remove unused parameter 'newW' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/MarbleBorderImageEffect.cs` | Remove unused parameter 'newHeight' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/MarbleBorderImageEffect.cs` | Remove unused parameter 'newWidth' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/RainbowBorderImageEffect.cs` | Remove unused parameter 'border' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/RainbowBorderImageEffect.cs` | Remove unused parameter 'newH' |
| `ShareX.ImageEditor/Core/ImageEffects/Filters/RainbowBorderImageEffect.cs` | Remove unused parameter 'newW' |
| `ShareX.ImageEditor/Presentation/ViewModels/MainViewModel.EffectPreview.cs` | Remove unused parameter 'statusMessage' |
| `ShareX.ImageEditor/Presentation/ViewModels/MainViewModel.ImageState.cs` | Remove unused parameter 'context' |
| `ShareX.Tools/Tools/InspectWindow/InspectWindowService.cs` | Remove unused parameter 'handle' if it is not part of a shipped public API |
| `ShareX.Tools/Tools/InspectWindow/InspectWindowService.cs` | Remove unused parameter 'topLevel' if it is not part of a shipped public API |
| `ShareX.Tools/Linux/MouseHighlighterOverlayWindow.cs` | Remove unused parameter 'xid' |
| `ShareX.Tools/Tools/AnalyzeImage/OpenAILegacyProvider.cs` | Remove unused parameter 'reasoningEffort' |
| `ShareX.Tools/Tools/AnalyzeImage/OpenAILegacyProvider.cs` | Remove unused parameter 'textVerbosity' |
| `ShareX.Tools/Tools/AnalyzeImage/GeminiProvider.cs` | Remove unused parameter 'reasoningEffort' |
| `ShareX.Tools/Tools/AnalyzeImage/GeminiProvider.cs` | Remove unused parameter 'verbosity' |
| `ShareX.Tools/Tools/AnalyzeImage/OpenRouterProvider.cs` | Remove unused parameter 'verbosity' |
| `ShareX.Tools/Tools/AnalyzeImage/OpenRouterProvider.cs` | Remove unused parameter 'reasoningEffort' |
| `tests/ShareX.Startup.Tests/Program.cs` | Remove unused parameter 'args' |

## Retained overlay import inventory

| File | Findings |
| --- | --- |
| `ShareX.OverlayAvaloniaLayer/AnnotationOverlayExtension.cs` | 1 IDE0005 |
| `ShareX.OverlayAvaloniaLayer/AnnotationLayerHost.cs` | 4 IDE0005 |
| `ShareX.OverlayAvaloniaLayer/AnnotationLayerOptions.cs` | 1 IDE0005 |
| `ShareX.OverlayAvaloniaLayer/RetainedHeadlessFrames.cs` | 1 IDE0005 |
| `ShareX.OverlayAvaloniaLayer/AnnotationRenderResult.cs` | 1 IDE0005 |
