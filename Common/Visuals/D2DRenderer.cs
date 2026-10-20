using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinRT;

namespace KyappuTone.Common.Visuals;

/// <summary>
/// Direct3D 11 + Direct2D renderer for a XAML <see cref="SwapChainPanel"/>.
/// Owns the composition swap chain, the device context and one reusable brush.
/// </summary>
public sealed class D2DRenderer : IDisposable
{
    private readonly SwapChainPanel _panel;

    private ID3D11Device? _d3dDevice;
    private IDXGIDevice? _dxgiDevice;
    private IDXGISwapChain1? _swapChain;
    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _context;
    private ID2D1Bitmap1? _target;
    private ID2D1SolidColorBrush? _brush;
    private ISwapChainPanelNative? _panelNative;

    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>Creates a renderer bound to a panel. Call <see cref="Resize"/> before drawing.</summary>
    public D2DRenderer(SwapChainPanel panel)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
    }

    /// <summary>True once a swap chain and back-buffer target exist.</summary>
    public bool IsReady => _swapChain is not null && _target is not null;

    /// <summary>Creates devices and the composition swap chain at the given pixel size.</summary>
    public void Initialize(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_swapChain is not null) return;

        _width = Math.Max(1, pixelWidth);
        _height = Math.Max(1, pixelHeight);

        _d3dDevice = D3D11.D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            new[] { Vortice.Direct3D.FeatureLevel.Level_11_0 });

        _dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        using var adapter = _dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();

        var description = new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
            Flags = SwapChainFlags.None,
        };

        _swapChain = factory.CreateSwapChainForComposition(_d3dDevice, description, null);

        _panelNative = _panel.As<ISwapChainPanelNative>();
        _panelNative.SetSwapChain(_swapChain.NativePointer);

        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        _d2dDevice = _d2dFactory.CreateDevice(_dxgiDevice);
        _context = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        _brush = _context.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));

        CreateTarget();
    }

    /// <summary>Resizes the swap-chain buffers when the panel size changed.</summary>
    public void Resize(int pixelWidth, int pixelHeight)
    {
        if (_disposed || _swapChain is null || _context is null) return;

        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        if (pixelWidth == _width && pixelHeight == _height && _target is not null) return;

        _width = pixelWidth;
        _height = pixelHeight;

        _context.Target = null;
        _target?.Dispose();
        _target = null;

        _swapChain.ResizeBuffers(2, (uint)_width, (uint)_height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
        CreateTarget();
    }

    /// <summary>Draws one frame and presents it.</summary>
    public void Render(IVisualizerStyle style, ReadOnlySpan<float> bands, TimeSpan position)
    {
        if (_disposed || !IsReady) return;
        ArgumentNullException.ThrowIfNull(style);

        _context!.BeginDraw();
        _context.Clear(new Color4(0.03f, 0.04f, 0.07f, 0.55f));
        style.Draw(_context, _brush!, _width, _height, bands, position);

        var result = _context.EndDraw();
        if (result.Failure) return;

        _swapChain!.Present(1, PresentFlags.None);
    }

    private void CreateTarget()
    {
        using var surface = _swapChain!.GetBuffer<IDXGISurface>(0);
        var properties = new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96f,
            96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);

        _target = _context!.CreateBitmapFromDxgiSurface(surface, properties);
        _context.Target = _target;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _brush?.Dispose();
        _brush = null;

        _context?.Target = null;
        _target?.Dispose();
        _target = null;

        _context?.Dispose();
        _context = null;
        _d2dDevice?.Dispose();
        _d2dDevice = null;
        _d2dFactory?.Dispose();
        _d2dFactory = null;

        _swapChain?.Dispose();
        _swapChain = null;
        _dxgiDevice?.Dispose();
        _dxgiDevice = null;
        _d3dDevice?.Dispose();
        _d3dDevice = null;

        _panelNative = null;
    }

    [ComImport]
    [Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISwapChainPanelNative
    {
        [PreserveSig]
        int SetSwapChain(IntPtr swapChain);
    }
}
