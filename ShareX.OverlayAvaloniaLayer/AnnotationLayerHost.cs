// GPL-3.0-or-later.
#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShareX.AvaloniaUI.Theming;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Integration;
using ShareX.ImageEditor.Presentation.Controls;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEditor.Presentation.Views;
using ShareX.ScreenCaptureLib.Localization;
using SkiaSharp;

namespace ShareX.OverlayAvaloniaLayer;

public enum PointerAction
{
    Move,
    Down,
    Up,
    Wheel
}

public sealed class AnnotationLayerHost : IDisposable
{
    private readonly Window _window;
    private readonly RetainedHeadlessFrames.RetainedFrameSurface _retainedSurface;
    private readonly Grid _rootGrid;
    private readonly EditorView _editorWorkspace;
    private readonly Canvas _toolbarCanvas;
    private readonly Grid _captureToolbar;
    private readonly AnnotationToolbar _annotationToolbar;
    private readonly Button _regionToolButton;
    private readonly AnnotationLayerOptions _options;
    private MainViewModel _viewModel = null!;

    private Rect _desktopBounds;
    private Rect? _activeMonitorBounds;
    private double _topInset;
    private Rect _selection;
    private bool _isRegionToolActive = true;
    private bool _disposed;

    private byte[] _previousFrame = [];
    private double _positionedToolbarWidth = double.NaN;
    private Rect? _positionedMonitor;
    private double _positionedTopInset = double.NaN;

    public Window Window => _window;
    public EditorView Workspace => _editorWorkspace;
    public MainViewModel ViewModel => _viewModel;
    public RetainedHeadlessFrames.RetainedFrameSurface FrameSurface => _retainedSurface;
    public bool IsRegionToolActive => _isRegionToolActive;
    public bool HasAnnotations => _viewModel?.HasAnnotations ?? false;
    public Rect Selection => _selection;
    public Rect DesktopBounds => _desktopBounds;

    private AnnotationLayerHost(Rect desktopBounds, AnnotationLayerOptions options)
    {
        _desktopBounds = desktopBounds;
        _options = options;

        _window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Width = desktopBounds.Width,
            Height = desktopBounds.Height
        };

        if (options.Theme != null)
        {
            _window.RequestedThemeVariant = options.Theme;
        }

        _retainedSurface = RetainedHeadlessFrames.Attach(_window);
        _window.SetRenderScaling(options.RenderScaling);

        _rootGrid = new Grid
        {
            Background = Brushes.Transparent,
            Width = desktopBounds.Width,
            Height = desktopBounds.Height
        };

        _editorWorkspace = new EditorView
        {
            Background = Brushes.Transparent,
            Width = desktopBounds.Width,
            Height = desktopBounds.Height
        };

        _toolbarCanvas = new Canvas
        {
            Background = null,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _captureToolbar = new Grid
        {
            Classes = { "capture-toolbar" },
            MaxWidth = desktopBounds.Width
        };
        _captureToolbar.LayoutUpdated += OnCaptureToolbarLayoutUpdated;

        _annotationToolbar = new AnnotationToolbar
        {
            MainToolbarBorderThickness = new Thickness(1),
            MainToolbarCornerRadius = new CornerRadius(0, 0, 4, 4),
            ShowEditingActions = false
        };

        _regionToolButton = new Button
        {
            Classes = { "toolbar-button", "active" },
            Content = LucideIcons.scan
        };
        ToolTip.SetTip(_regionToolButton, Strings.RegionCaptureWindow_RectangleRegionTab);
        _regionToolButton.Click += (s, e) => ActivateRegionTool();

        _annotationToolbar.MainToolbarLeadingContent = _regionToolButton;
        _captureToolbar.Children.Add(_annotationToolbar);
        _toolbarCanvas.Children.Add(_captureToolbar);

        _rootGrid.Children.Add(_editorWorkspace);
        _rootGrid.Children.Add(_toolbarCanvas);
        _window.Content = _rootGrid;

        InitializeViewModel();
        _window.Show();
        Tick();

        InitializeWorkspace();
        ActivateRegionTool();
        Tick();
    }

    public static AnnotationLayerHost Create(Rect desktopBounds, AnnotationLayerOptions? options = null, ThemeVariant? theme = null)
    {
        options ??= new AnnotationLayerOptions();
        if (theme != null)
        {
            options.Theme = theme;
        }

        return new AnnotationLayerHost(desktopBounds, options);
    }

    private void InitializeWorkspace()
    {
        _editorWorkspace.ConfigureForFullscreenWorkspace();

        var fallback = _window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_TransparencyFallback");
        if (fallback != null)
        {
            fallback.IsVisible = false;
            fallback.Background = Brushes.Transparent;
        }

        if (_editorWorkspace.Content is Grid rootGrid && rootGrid.Children.Count > 0 && rootGrid.Children[0] is Grid contentGrid)
        {
            contentGrid.Background = Brushes.Transparent;
        }

        var attach = typeof(EditorView).GetMethod("AttachViewModel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        attach?.Invoke(_editorWorkspace, [_viewModel]);

        var scrollViewer = _editorWorkspace.FindControl<ScrollViewer>("CanvasScrollViewer");
        if (scrollViewer?.Content is LayoutTransformControl ltc)
        {
            ltc.HorizontalAlignment = HorizontalAlignment.Left;
            ltc.VerticalAlignment = VerticalAlignment.Top;
            if (ltc.Child is Grid grid)
            {
                grid.HorizontalAlignment = HorizontalAlignment.Left;
                grid.VerticalAlignment = VerticalAlignment.Top;
            }
        }

        var previewFrame = _editorWorkspace.FindControl<Border>("PreviewFrame");
        if (previewFrame?.Parent is Grid containerGrid && containerGrid.Children.Count > 0 && containerGrid.Children[0] is Border checkered)
        {
            checkered.IsVisible = false;
            checkered.Background = Brushes.Transparent;
            checkered.BorderBrush = Brushes.Transparent;
        }

        if (previewFrame != null)
        {
            previewFrame.Background = Brushes.Transparent;
        }

        var canvasControl = _editorWorkspace.FindControl<SKCanvasControl>("CanvasControl");
        if (canvasControl != null)
        {
            canvasControl.IsVisible = false;
        }

        int imgW = (int)Math.Max(1, _desktopBounds.Width);
        int imgH = (int)Math.Max(1, _desktopBounds.Height);
        var dummy = new SKBitmap(imgW, imgH, SKColorType.Bgra8888, SKAlphaType.Premul);
        dummy.Erase(SKColors.Transparent);
        _editorWorkspace.LoadWorkspaceImage(dummy);

        var canvasField = typeof(EditorView).GetField("_canvasControl", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        canvasField?.SetValue(_editorWorkspace, null);

        var spotlightControl = _editorWorkspace.FindControl<SpotlightOverlayControl>("SpotlightOverlayControl");
        if (spotlightControl?.Parent is Panel parentPanel)
        {
            parentPanel.Children.Remove(spotlightControl);
        }
    }

    private void InitializeViewModel()
    {
        _viewModel = new MainViewModel(_options.ImageEditorOptions)
        {
            ShowFileMenu = false,
            ShowTaskButtons = false,
            ShowBottomToolbar = false,
            ShowToolbars = false,
            ShowStartScreen = false,
            UseContinueWorkflow = false
        };

        _viewModel.SetHostToolbarFilter(item =>
            _options.EnableAnnotations &&
            item.Tool.HasValue &&
            item.Tool.Value is not EditorTool.Crop and not EditorTool.CutOut);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ToolSelectionRequested += OnAnnotationToolSelected;

        _editorWorkspace.DataContext = _viewModel;
        _annotationToolbar.DataContext = _viewModel.ToolbarAdapter;
        _editorWorkspace.AttachHostAnnotationToolbar(_annotationToolbar);
        _annotationToolbar.IsVisible = _options.EnableAnnotations;
        _annotationToolbar.ShowToolOptions = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveTool))
        {
            ActivateAnnotationTool();
        }
    }

    private void OnAnnotationToolSelected(object? sender, EditorTool tool)
    {
        ActivateAnnotationTool();
    }

    public void ActivateRegionTool()
    {
        _isRegionToolActive = true;
        _regionToolButton.Classes.Set("active", true);
        _viewModel?.SetHostToolbarToolsActive(false);
        _annotationToolbar.ShowToolOptions = false;
        _editorWorkspace.CancelActiveInteractionOrSelection();
        _editorWorkspace.IsHitTestVisible = false;
    }

    public void ActivateAnnotationTool()
    {
        if (!_options.EnableAnnotations) return;

        _isRegionToolActive = false;
        _regionToolButton.Classes.Set("active", false);
        _viewModel?.SetHostToolbarToolsActive(true);
        _annotationToolbar.ShowToolOptions = true;
        _editorWorkspace.IsHitTestVisible = true;
        _editorWorkspace.Focus();
    }

    public void SelectTool(EditorTool tool)
    {
        _viewModel.ToolbarAdapter.SelectTool(tool);
        ActivateAnnotationTool();
    }

    public void SetSelection(Rect selection)
    {
        _selection = selection;
        if (selection.Width > 0 && selection.Height > 0)
        {
            _editorWorkspace.Clip = new RectangleGeometry(selection);
        }
        else
        {
            _editorWorkspace.Clip = null;
        }
    }

    public void SetActiveMonitor(Rect monitorBounds, double topInset = 0)
    {
        _activeMonitorBounds = monitorBounds;
        _topInset = topInset;
        UpdateToolbarPosition();
    }

    private void OnCaptureToolbarLayoutUpdated(object? sender, EventArgs e)
    {
        UpdateToolbarPosition();
    }

    private void UpdateToolbarPosition()
    {
        if (!_activeMonitorBounds.HasValue) return;

        Rect monitor = _activeMonitorBounds.Value;
        double toolbarWidth = _captureToolbar.Bounds.Width > 0 ? _captureToolbar.Bounds.Width : _captureToolbar.DesiredSize.Width;
        if (!double.IsFinite(toolbarWidth) || toolbarWidth <= 0 ||
            (_positionedMonitor == monitor &&
             Math.Abs(_positionedTopInset - _topInset) < 0.01 &&
             Math.Abs(toolbarWidth - _positionedToolbarWidth) < 0.01))
        {
            return;
        }

        _positionedMonitor = monitor;
        _positionedTopInset = _topInset;
        _positionedToolbarWidth = toolbarWidth;
        double monitorCenterX = monitor.X + monitor.Width / 2d;
        double monitorTop = monitor.Y + _topInset;

        Canvas.SetLeft(_captureToolbar, monitorCenterX - toolbarWidth / 2d);
        Canvas.SetTop(_captureToolbar, monitorTop);
    }

    public bool HitTest(Point globalLogicalPoint)
    {
        if (!_isRegionToolActive)
        {
            return true;
        }

        return IsPointOverToolbar(globalLogicalPoint);
    }

    public bool IsPointOverToolbar(Point point)
    {
        if (!_captureToolbar.IsVisible) return false;

        double left = Canvas.GetLeft(_captureToolbar);
        double top = Canvas.GetTop(_captureToolbar);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        double width = _captureToolbar.Bounds.Width > 0 ? _captureToolbar.Bounds.Width : _captureToolbar.DesiredSize.Width;
        double height = _captureToolbar.Bounds.Height > 0 ? _captureToolbar.Bounds.Height : _captureToolbar.DesiredSize.Height;

        Rect toolbarRect = new(left, top, width, height);
        if (toolbarRect.Contains(point)) return true;

        if (_viewModel?.IsModalOpen == true) return true;

        return false;
    }

    public void SendPointer(PointerAction action, Point logicalPoint, MouseButton button = MouseButton.None, RawInputModifiers modifiers = RawInputModifiers.None, Vector wheelDelta = default)
    {
        switch (action)
        {
            case PointerAction.Move:
                _window.MouseMove(logicalPoint, modifiers);
                break;
            case PointerAction.Down:
                _window.MouseDown(logicalPoint, button, modifiers);
                break;
            case PointerAction.Up:
                _window.MouseUp(logicalPoint, button, modifiers);
                break;
            case PointerAction.Wheel:
                _window.MouseWheel(logicalPoint, wheelDelta, modifiers);
                break;
        }
    }

    public void SendPointerMove(Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        _window.MouseMove(point, modifiers);
    }

    public void SendPointerDown(Point point, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        _window.MouseDown(point, button, modifiers);
    }

    public void SendPointerUp(Point point, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        _window.MouseUp(point, button, modifiers);
    }

    public void SendPointerWheel(Point point, Vector delta, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        _window.MouseWheel(point, delta, modifiers);
    }

    public void SendKey(Key key, RawInputModifiers modifiers = RawInputModifiers.None, bool isDown = true, string? text = null)
    {
        if (isDown)
        {
            _window.KeyPress(key, modifiers, PhysicalKey.None, text ?? "");
        }
        else
        {
            _window.KeyRelease(key, modifiers, PhysicalKey.None, text ?? "");
        }
    }

    public void SendTextInput(string text)
    {
        _window.KeyTextInput(text);
    }

    public void Undo()
    {
        if (_viewModel.UndoCommand.CanExecute(null))
        {
            _viewModel.UndoCommand.Execute(null);
        }
    }

    public void Redo()
    {
        if (_viewModel.RedoCommand.CanExecute(null))
        {
            _viewModel.RedoCommand.Execute(null);
        }
    }

    public AnnotationRenderResult Render()
    {
        long start = Stopwatch.GetTimestamp();
        _retainedSurface.RasterMilliseconds = 0;
        _retainedSurface.RenderCount = 0;

        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        double renderTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        double rasterTime = _retainedSurface.RasterMilliseconds;
        double avaloniaTime = Math.Max(0, renderTime - rasterTime);

        if (!_retainedSurface.TryGetFrame(out var frame))
        {
            return new AnnotationRenderResult
            {
                HasChanges = false,
                DirtyRegions = Array.Empty<PixelRect>(),
                Frame = default,
                TotalMilliseconds = renderTime,
                AvaloniaMilliseconds = avaloniaTime,
                RasterMilliseconds = rasterTime
            };
        }

        long diffStart = Stopwatch.GetTimestamp();
        var (dirtyRegions, enclosingBounds, changedBytes) = ComputeDamage(frame);
        double diffTime = Stopwatch.GetElapsedTime(diffStart).TotalMilliseconds;
        double totalTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        return new AnnotationRenderResult
        {
            HasChanges = dirtyRegions.Count > 0,
            DirtyRegions = dirtyRegions,
            EnclosingBounds = enclosingBounds,
            Frame = frame,
            TotalMilliseconds = totalTime,
            AvaloniaMilliseconds = avaloniaTime,
            RasterMilliseconds = rasterTime,
            DiffMilliseconds = diffTime,
            ChangedBytes = changedBytes
        };
    }

    private unsafe (List<PixelRect> Regions, PixelRect Bounds, long Bytes) ComputeDamage(LayerFrame frame)
    {
        int totalBytes = frame.Stride * frame.Height;
        if (_previousFrame.Length != totalBytes)
        {
            _previousFrame = new byte[totalBytes];
        }

        var source = new ReadOnlySpan<byte>((void*)frame.Address, totalBytes);
        var regions = new List<PixelRect>();

        int left = frame.Width, right = 0, top = frame.Height, bottom = 0;
        int bandStart = -1;
        int bandLeft = frame.Width, bandRight = 0;
        long changedBytes = 0;

        for (int y = 0; y < frame.Height; y++)
        {
            int rowOffset = y * frame.Stride;
            var row = source.Slice(rowOffset, frame.Width * 4);
            var prior = _previousFrame.AsSpan(rowOffset, frame.Width * 4);

            if (row.SequenceEqual(prior))
            {
                if (bandStart >= 0)
                {
                    regions.Add(new PixelRect(bandLeft, bandStart, bandRight - bandLeft, y - bandStart));
                    bandStart = -1;
                    bandLeft = frame.Width;
                    bandRight = 0;
                }
                continue;
            }

            var pixels = MemoryMarshal.Cast<byte, uint>(row);
            var old = MemoryMarshal.Cast<byte, uint>(prior);
            int x = 0, end = frame.Width - 1;
            while (pixels[x] == old[x]) x++;
            while (pixels[end] == old[end]) end--;

            left = Math.Min(left, x);
            right = Math.Max(right, end + 1);
            top = Math.Min(top, y);
            bottom = y + 1;

            if (bandStart < 0)
            {
                bandStart = y;
            }
            bandLeft = Math.Min(bandLeft, x);
            bandRight = Math.Max(bandRight, end + 1);
        }

        if (bandStart >= 0)
        {
            regions.Add(new PixelRect(bandLeft, bandStart, bandRight - bandLeft, frame.Height - bandStart));
        }

        if (top < bottom && left < right)
        {
            for (int y = top; y < bottom; y++)
            {
                int index = y * frame.Stride + left * 4;
                int size = (right - left) * 4;
                source.Slice(index, size).CopyTo(_previousFrame.AsSpan(index, size));
                changedBytes += size;
            }

            return (regions, new PixelRect(left, top, right - left, bottom - top), changedBytes);
        }

        return (regions, default, 0);
    }

    public SKBitmap? RenderResult(SKBitmap screenshot, Rect selection)
    {
        ArgumentNullException.ThrowIfNull(screenshot);

        var canvasControl = _editorWorkspace.FindControl<SKCanvasControl>("CanvasControl");
        bool wasVisible = canvasControl?.IsVisible ?? false;
        try
        {
            if (canvasControl != null)
            {
                var canvasField = typeof(EditorView).GetField("_canvasControl", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                canvasField?.SetValue(_editorWorkspace, canvasControl);
                canvasControl.IsVisible = true;
            }

            _editorWorkspace.LoadWorkspaceImage(screenshot);
            return _editorWorkspace.GetSnapshot(selection);
        }
        finally
        {
            if (canvasControl != null)
            {
                var canvasField = typeof(EditorView).GetField("_canvasControl", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                canvasField?.SetValue(_editorWorkspace, null);
                canvasControl.IsVisible = wasVisible;
            }
        }
    }

    private static void Tick()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _captureToolbar.LayoutUpdated -= OnCaptureToolbarLayoutUpdated;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.ToolSelectionRequested -= OnAnnotationToolSelected;
            _viewModel.Dispose();
        }

        _editorWorkspace.DetachHostAnnotationToolbar(_annotationToolbar);
        _editorWorkspace.DisposeWorkspace();
        _window.Close();
        Tick();
        _retainedSurface.Dispose();
    }
}
