// GPL-3.0-or-later.
#nullable enable

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ShareX.OverlayAvaloniaLayer;

public readonly record struct LayerFrame(IntPtr Address, int Width, int Height, int Stride);

public static class RetainedHeadlessFrames
{
    public static RetainedFrameSurface Attach(TopLevel window)
    {
        FieldInfo field = typeof(Avalonia.Headless.AvaloniaHeadlessPlatform).Assembly
            .GetType("Avalonia.Headless.HeadlessWindowImpl")?
            .GetField("<Surfaces>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Headless surface ABI changed.");
        if (field.FieldType != typeof(IPlatformRenderSurface[]))
            throw new InvalidOperationException("Headless surface ABI changed.");
        var surface = new RetainedFrameSurface();
        field.SetValue(window.PlatformImpl, new IPlatformRenderSurface[] { surface.CreatePlatformSurface() });
        return surface;
    }

    public class SurfaceProxy : DispatchProxy
    {
        public RetainedFrameSurface? Owner;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "CreateFramebufferRenderTarget" => Owner!.CreateRenderTarget(),
                "get_IsReady" => true,
                _ => null
            };
        }
    }

    public sealed class RetainedFrameSurface : IDisposable
    {
        public double RasterMilliseconds;
        public int RenderCount;
        private long _lockStarted;
        private IntPtr _buffer;
        private PixelSize _size;
        private int _rowBytes;
        private bool _rendered;

        public unsafe void Dispose()
        {
            if (_buffer != IntPtr.Zero)
            {
                NativeMemory.AlignedFree((void*)_buffer);
                _buffer = IntPtr.Zero;
            }
        }

        public IFramebufferPlatformSurface CreatePlatformSurface()
        {
            IFramebufferPlatformSurface proxy = DispatchProxy.Create<IFramebufferPlatformSurface, SurfaceProxy>();
            ((SurfaceProxy)(object)proxy).Owner = this;
            return proxy;
        }

        public IFramebufferRenderTarget CreateRenderTarget()
        {
            ConstructorInfo ctor = typeof(FuncFramebufferRenderTarget).GetConstructors().First(c => c.GetParameters().Length == 2);
            Type delegateType = ctor.GetParameters()[0].ParameterType;
            ParameterInfo[] invokeParameters = delegateType.GetMethod("Invoke")!.GetParameters();
            ParameterExpression sceneInfo = Expression.Parameter(invokeParameters[0].ParameterType, "sceneInfo");
            ParameterExpression properties = Expression.Parameter(invokeParameters[1].ParameterType, "properties");
            ParameterExpression retained = Expression.Variable(typeof(bool), "retained");
            ParameterExpression locked = Expression.Variable(typeof(ILockedFramebuffer), "locked");
            Expression body = Expression.Block(
                new[] { retained, locked },
                Expression.Assign(locked, Expression.Call(Expression.Constant(this), nameof(LockCore),
                    null, Expression.Property(sceneInfo, "Size"), Expression.Property(sceneInfo, "Scaling"), retained)),
                Expression.Assign(properties, Expression.New(typeof(FramebufferLockProperties).GetConstructor(new[] { typeof(bool) })!, retained)),
                locked);
            Delegate lockDelegate = Expression.Lambda(delegateType, body, sceneInfo, properties).Compile();
            return (IFramebufferRenderTarget)ctor.Invoke(new object[] { lockDelegate, true });
        }

        public bool TryGetFrame(out LayerFrame frame)
        {
            frame = default;
            if (_buffer == IntPtr.Zero || !_rendered) return false;
            frame = new LayerFrame(_buffer, _size.Width, _size.Height, _rowBytes);
            return true;
        }

        public unsafe ILockedFramebuffer LockCore(PixelSize size, double scaling, out bool retained)
        {
            _lockStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_buffer == IntPtr.Zero || _size != size)
            {
                if (_buffer != IntPtr.Zero) NativeMemory.AlignedFree((void*)_buffer);
                _size = size;
                _rowBytes = size.Width * 4;
                _buffer = (IntPtr)NativeMemory.AlignedAlloc((nuint)((long)_rowBytes * size.Height), 64);
            }

            NativeMemory.Clear((void*)_buffer, (nuint)((long)_rowBytes * size.Height));
            retained = false;

            return new Locked(this, new Vector(96 * scaling, 96 * scaling));
        }

        private sealed class Locked(RetainedFrameSurface owner, Vector dpi) : ILockedFramebuffer
        {
            public IntPtr Address => owner._buffer;
            public PixelSize Size => owner._size;
            public int RowBytes => owner._rowBytes;
            public Vector Dpi => dpi;
            public PixelFormat Format => PixelFormat.Bgra8888;
            public AlphaFormat AlphaFormat => AlphaFormat.Premul;
            public void Dispose()
            {
                owner.RasterMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(owner._lockStarted).TotalMilliseconds;
                owner.RenderCount++;
                owner._rendered = true;
            }
        }
    }
}
