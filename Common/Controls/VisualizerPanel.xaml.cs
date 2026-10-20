using KyappuTone.Common.Visuals;
using KyappuTone.Common.Visuals.Styles;
using KyappuTone.Core.Audio;
using Microsoft.UI.Dispatching;

namespace KyappuTone.Common.Controls;

/// <summary>
/// Hosts the Direct2D visualizer. A 60 Hz dispatcher timer reads the engine's
/// FFT snapshot and repaints the swap chain; the timer stops when unloaded.
/// </summary>
public sealed partial class VisualizerPanel : UserControl
{
    private const int BarCount = 64;
    private const int FftSize = 2048;
    private const float Decay = 0.82f;

    private readonly IVisualizerStyle[] _styles = { new BarsStyle(), new WaveformStyle(), new MirrorStyle() };
    private readonly float[] _magnitudes = new float[FftSize / 2];
    private readonly float[] _mapped = new float[BarCount];
    private readonly float[] _bars = new float[BarCount];

    private D2DRenderer? _renderer;
    private DispatcherQueueTimer? _timer;
    private int _styleIndex;
    private bool _renderingEnabled = true;

    /// <summary>Creates the visualizer host.</summary>
    public VisualizerPanel()
    {
        InitializeComponent();

        foreach (var style in _styles) StylePicker.Items.Add(style.Name);
        StylePicker.SelectedIndex = 0;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SwapPanel.SizeChanged += OnSwapPanelSizeChanged;
    }

    /// <summary>Starts or stops the 60 Hz render loop (low-power mode).</summary>
    public bool IsRenderingEnabled
    {
        get => _renderingEnabled;
        set
        {
            _renderingEnabled = value;
            if (value) _timer?.Start();
            else _timer?.Stop();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureRenderer();

        if (_timer is null)
        {
            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.IsRepeating = true;
            _timer.Tick += RenderTick;
        }

        if (_renderingEnabled) _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer?.Stop();

    private void OnSwapPanelSizeChanged(object sender, SizeChangedEventArgs e) => EnsureRenderer();

    private void EnsureRenderer()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        if (_renderer is null)
        {
            _renderer = new D2DRenderer(SwapPanel);
            _renderer.Initialize(PixelWidth, PixelHeight);
            return;
        }

        _renderer.Resize(PixelWidth, PixelHeight);
    }

    private int PixelWidth => Math.Max(1, (int)Math.Round(ActualWidth * (XamlRoot?.RasterizationScale ?? 1.0)));

    private int PixelHeight => Math.Max(1, (int)Math.Round(ActualHeight * (XamlRoot?.RasterizationScale ?? 1.0)));

    private void RenderTick(DispatcherQueueTimer sender, object args)
    {
        if (_renderer is null) return;

        var fft = MusicPlayer.Instance.VisualizerFft;
        if (fft is not null && fft.TryRead(_magnitudes))
        {
            VisualizerBands.Map(_magnitudes, fft.SampleRate, BarCount, _mapped);
        }
        else
        {
            Array.Clear(_mapped);
        }

        for (var band = 0; band < BarCount; band++)
        {
            _bars[band] = MathF.Max(_mapped[band], _bars[band] * Decay);
        }

        _renderer.Render(_styles[_styleIndex], _bars, TimeSpan.FromTicks(MusicPlayer.Instance.CurTimeTicks));
    }

    private void StylePicker_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StylePicker.SelectedIndex >= 0 && StylePicker.SelectedIndex < _styles.Length)
            _styleIndex = StylePicker.SelectedIndex;
    }
}
