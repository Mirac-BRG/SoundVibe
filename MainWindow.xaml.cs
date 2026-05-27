using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Linq;
using System.Windows.Data;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using TagLib;

// --- ÇAKIŞMA ÖNLEYİCİ ALIAS'LAR ---
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Image = System.Windows.Controls.Image;
using Rectangle = System.Windows.Shapes.Rectangle;
using TextBox = System.Windows.Controls.TextBox;

using NAudio.Wave;
using NAudio.Dsp;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;

namespace SoundVibe
{
    public struct Particle
    {
        public double X, Y;
        public double VX, VY;
        public double Life, MaxLife;
        public double Size;
        public int Type;
        public bool IsAlive => Life > 0;
    }

    public sealed class VisualizerHost : FrameworkElement
    {
        private readonly DrawingVisual _visual = new DrawingVisual();
        private const int HISTORY_DEPTH = 12;
        private float[][] _fftHistory = new float[HISTORY_DEPTH][];
        private int _historyIndex = 0;
        private double _rotBass = 0;
        private double _rotMid = 0;
        private double _rotHigh = 0;
        private const int MAX_PARTICLES = 300;
        private Particle[] _particles = new Particle[MAX_PARTICLES];
        private Random _rnd = new Random();
        private SolidColorBrush _crtScanlineBrush;
        private SolidColorBrush _particleGlowBrush;

        public VisualizerHost()
        {
            AddVisualChild(_visual);
            AddLogicalChild(_visual);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

            for (int i = 0; i < HISTORY_DEPTH; i++)
                _fftHistory[i] = new float[64];

            _crtScanlineBrush = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
            _crtScanlineBrush.Freeze();

            _particleGlowBrush = new SolidColorBrush(Color.FromArgb(200, 209, 59, 138));
            _particleGlowBrush.Freeze();
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _visual;

        public void UpdateThemeBrushes(Color particleColor, Color crtColor)
        {
            _particleGlowBrush = new SolidColorBrush(Color.FromArgb(200, particleColor.R, particleColor.G, particleColor.B));
            _particleGlowBrush.Freeze();

            _crtScanlineBrush = new SolidColorBrush(crtColor);
            _crtScanlineBrush.Freeze();
        }

        public void ResetHistory()
        {
            for (int i = 0; i < HISTORY_DEPTH; i++)
                Array.Clear(_fftHistory[i], 0, _fftHistory[i].Length);
            _historyIndex = 0;
            _rotBass = 0; _rotMid = 0; _rotHigh = 0;
        }

        public void DrawBars(float[] data, double width, double height, Brush brush, bool mirror = false)
        {
            int count = data.Length;
            double bw = width / count;

            using var dc = _visual.RenderOpen();
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

            for (int i = 0; i < count; i++)
            {
                double intensity = Math.Sqrt(data[i]) * 350.0;
                intensity = Math.Max(4.0, Math.Min(height * 0.92, intensity));

                double x = i * bw + 1;
                double w = Math.Max(2.0, bw - 2.0);

                if (mirror)
                {
                    double cy = height / 2.0;
                    dc.DrawRectangle(brush, null, new Rect(x, cy - intensity / 2.0, w, intensity));
                }
                else
                {
                    dc.DrawRectangle(brush, null, new Rect(x, height - intensity, w, intensity));
                }
            }
        }

        public void DrawCyberWave(float[] data, double width, double height, Brush fillBrush, Pen pen)
        {
            int count = data.Length;
            if (count < 2) return;
            double bw = width / count;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, height), true, true);
                for (int i = 0; i < count; i++)
                {
                    double y = (height / 2.0) - (Math.Sqrt(data[i]) * 300.0);
                    ctx.LineTo(new Point(i * bw, y), true, false);
                }
                ctx.LineTo(new Point(width, height), true, false);
            }
            geo.Freeze();
            using var dc = _visual.RenderOpen();
            dc.PushOpacity(0.22);
            dc.DrawGeometry(fillBrush, null, geo);
            dc.Pop();
            dc.DrawGeometry(null, pen, geo);
        }

        public void DrawClassicWave(float[] data, double width, double height, Pen pen)
        {
            int count = data.Length;
            if (count < 2) return;
            double bw = width / count;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, height / 2.0), false, false);
                for (int i = 0; i < count; i++)
                {
                    double y = (height / 2.0) - (Math.Sqrt(data[i]) * 300.0);
                    ctx.LineTo(new Point(i * bw, y), true, false);
                }
            }
            geo.Freeze();
            using var dc = _visual.RenderOpen();
            dc.DrawGeometry(null, pen, geo);
        }

        public void DrawClassicCircular(float[] data, double width, double height, Pen pen, double bass, double rotationAngle)
        {
            Array.Copy(data, _fftHistory[_historyIndex], Math.Min(data.Length, 64));
            using var dc = _visual.RenderOpen();
            int count = data.Length;
            double cx = width / 2.0;
            double cy = height / 2.0;
            double maxAllowedRadius = 380.0;

            double staticBaseRadius = 45.0;
            double pulse = bass * 15.0;
            double baseRadius = staticBaseRadius + pulse;
            if (baseRadius > 110.0) baseRadius = 110.0;
            for (int step = 0; step < HISTORY_DEPTH; step++)
            {
                int hIdx = (_historyIndex + 1 + step) % HISTORY_DEPTH;
                float[] pastData = _fftHistory[hIdx];

                double opacity = (step + 1.0) / HISTORY_DEPTH;
                dc.PushOpacity(opacity);
                for (int i = 0; i < count; i++)
                {
                    double angle = (i * 2 * Math.PI) / count + rotationAngle;
                    double decay = 1.0 - (step / (double)HISTORY_DEPTH);
                    decay = Math.Pow(decay, 1.5);

                    double intensity = Math.Sqrt(pastData[i]) * 600.0 * decay;
                    double targetRadius = baseRadius + intensity;
                    if (targetRadius > maxAllowedRadius) targetRadius = maxAllowedRadius;
                    double x1 = cx + Math.Cos(angle) * baseRadius;
                    double y1 = cy + Math.Sin(angle) * baseRadius;
                    double x2 = cx + Math.Cos(angle) * targetRadius;
                    double y2 = cy + Math.Sin(angle) * targetRadius;
                    dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
                }
                dc.Pop();
            }
            _historyIndex = (_historyIndex + 1) % HISTORY_DEPTH;
        }

        public void DrawSmoothedBlocks(float[] data, double width, double height, Brush brush)
        {
            Array.Copy(data, _fftHistory[_historyIndex], Math.Min(data.Length, 64));
            using var dc = _visual.RenderOpen();
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

            int count = data.Length;
            double bw = width / count;
            double w = Math.Max(2.0, bw - 2.0);
            for (int i = 0; i < count; i++)
            {
                float avgData = 0;
                for (int j = 0; j < HISTORY_DEPTH; j++) avgData += _fftHistory[j][i];
                avgData /= HISTORY_DEPTH;
                double intensity = Math.Sqrt(avgData) * 350.0;
                intensity = Math.Max(4.0, Math.Min(height * 0.92, intensity));
                dc.DrawRectangle(brush, null, new Rect(i * bw + 1, height - intensity, w, intensity));
            }
            _historyIndex = (_historyIndex + 1) % HISTORY_DEPTH;
        }

        public void DrawFractureBars(float[] data, double width, double height, Brush brush, double bass)
        {
            int count = data.Length;
            double bw = width / count;
            using var dc = _visual.RenderOpen();
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            for (int i = 0; i < count; i++)
            {
                double intensity = Math.Sqrt(data[i]) * 350.0;
                intensity = Math.Max(4.0, Math.Min(height * 0.92, intensity));

                double baseY = height - intensity;
                double w = Math.Max(2.0, bw - 2.0);
                int segments = (int)(intensity / 10);
                if (segments < 1) segments = 1;

                double segHeight = intensity / segments;
                for (int s = 0; s < segments; s++)
                {
                    double xOffset = 0;
                    if (bass > 0.8 && _rnd.NextDouble() > 0.5)
                        xOffset = (_rnd.NextDouble() - 0.5) * bass * 15.0;
                    double x = (i * bw + 1) + xOffset;
                    double y = baseY + (s * segHeight);
                    dc.DrawRectangle(brush, null, new Rect(x, y + 1, w, segHeight - 2));
                }
            }
        }

        public void DrawLiquidWave(float[] data, double width, double height, Pen pen)
        {
            int count = data.Length;
            if (count < 2) return;
            double bw = width / (count - 1);

            using var dc = _visual.RenderOpen();
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, height / 2.0), false, false);
                Point prevPoint = new Point(0, height / 2.0);

                for (int i = 1; i < count; i++)
                {
                    double y = (height / 2.0) - (Math.Sqrt(data[i]) * 300.0);
                    Point targetPoint = new Point(i * bw, y);

                    Point controlPoint1 = new Point(prevPoint.X + bw / 2, prevPoint.Y);
                    Point controlPoint2 = new Point(targetPoint.X - bw / 2, targetPoint.Y);

                    ctx.BezierTo(controlPoint1, controlPoint2, targetPoint, true, true);
                    prevPoint = targetPoint;
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        public void DrawOrbitalGalaxy(float[] data, double width, double height, Pen pen, double bass)
        {
            Array.Copy(data, _fftHistory[_historyIndex], Math.Min(data.Length, 64));
            _rotBass += 0.006 + (bass * 0.02);
            _rotMid -= 0.012;
            _rotHigh += 0.025;

            using var dc = _visual.RenderOpen();
            int count = data.Length;
            double cx = width / 2.0;
            double cy = height / 2.0;

            double staticBaseRadius = 95.0;
            double pulse = bass * 22.0;
            double baseRadius = staticBaseRadius + pulse;
            for (int step = 0; step < HISTORY_DEPTH; step++)
            {
                int hIdx = (_historyIndex + 1 + step) % HISTORY_DEPTH;
                float[] pastData = _fftHistory[hIdx];

                double decay = (step + 1.0) / HISTORY_DEPTH;
                decay = Math.Pow(decay, 3.5);

                dc.PushOpacity(decay);
                for (int i = 0; i < count; i++)
                {
                    double intensity = Math.Sqrt(pastData[i]) * 450.0;
                    double angleOffset = _rotMid;
                    if (i < 5) angleOffset = _rotBass;
                    else if (i > count / 2) angleOffset = _rotHigh;
                    double angle = (i * 2 * Math.PI) / count + angleOffset;
                    double organicShift = Math.Sin(angle * 3 + _rotMid) * 15.0;
                    double targetRadius = baseRadius + (intensity * decay) + organicShift;
                    double x1 = cx + Math.Cos(angle) * baseRadius;
                    double y1 = cy + Math.Sin(angle) * baseRadius;
                    double x2 = cx + Math.Cos(angle) * targetRadius;
                    double y2 = cy + Math.Sin(angle) * targetRadius;
                    dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
                }
                dc.Pop();
            }
            _historyIndex = (_historyIndex + 1) % HISTORY_DEPTH;
        }

        public void DrawAudioTunnel(float[] data, double width, double height, Pen pen, double bass)
        {
            Array.Copy(data, _fftHistory[_historyIndex], Math.Min(data.Length, 64));
            using var dc = _visual.RenderOpen();

            double cx = width / 2.0;
            double cy = height / 2.0;
            for (int step = 0; step < HISTORY_DEPTH; step++)
            {
                int hIdx = (_historyIndex + 1 + step) % HISTORY_DEPTH;
                float[] pastData = _fftHistory[hIdx];

                double depthScale = (step + 1.0) / HISTORY_DEPTH;
                double zScale = Math.Pow(depthScale, 2.0);

                dc.PushOpacity(zScale);
                dc.PushTransform(new ScaleTransform(zScale, zScale, cx, cy));

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    bool isFirst = true;
                    for (int i = 0; i < pastData.Length; i++)
                    {
                        double angle = (i * 2 * Math.PI) / pastData.Length;
                        double intensity = Math.Sqrt(pastData[i]) * 200.0;
                        double r = 150.0 + intensity;

                        double x = cx + Math.Cos(angle) * r;
                        double y = cy + Math.Sin(angle) * r;

                        if (isFirst) { ctx.BeginFigure(new Point(x, y), true, true); isFirst = false; }
                        else { ctx.LineTo(new Point(x, y), true, false); }
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);

                dc.Pop();
                dc.Pop();
            }
            _historyIndex = (_historyIndex + 1) % HISTORY_DEPTH;
        }

        public void DrawParticleReactor(float[] data, double width, double height, Brush brush, double bass, double delta)
        {
            using var dc = _visual.RenderOpen();
            double cx = width / 2.0;
            double cy = height / 2.0;
            if (bass > 0.9)
            {
                int particlesToSpawn = (int)(bass * 15);
                for (int i = 0; i < MAX_PARTICLES && particlesToSpawn > 0; i++)
                {
                    if (!_particles[i].IsAlive)
                    {
                        double angle = _rnd.NextDouble() * Math.PI * 2;
                        double speed = 2.0 + (_rnd.NextDouble() * bass * 15.0);

                        _particles[i].X = cx + Math.Cos(angle) * 30;
                        _particles[i].Y = cy + Math.Sin(angle) * 30;
                        _particles[i].VX = Math.Cos(angle) * speed;
                        _particles[i].VY = Math.Sin(angle) * speed;
                        _particles[i].Life = 1.0;
                        _particles[i].MaxLife = 0.5 + _rnd.NextDouble();
                        _particles[i].Size = 2.0 + _rnd.NextDouble() * 5.0;
                        _particles[i].Type = 0;
                        particlesToSpawn--;
                    }
                }
            }

            for (int i = 0; i < MAX_PARTICLES; i++)
            {
                if (_particles[i].IsAlive)
                {
                    _particles[i].X += _particles[i].VX;
                    _particles[i].Y += _particles[i].VY;
                    _particles[i].VX *= 0.96;
                    _particles[i].VY *= 0.96;
                    if (_particles[i].Type == 0) _particles[i].VY += 0.2;

                    _particles[i].Life -= delta;
                    double opacity = Math.Max(0, _particles[i].Life / _particles[i].MaxLife);
                    dc.PushOpacity(opacity);

                    double currentSize = _particles[i].Size + (bass * 2.0);
                    dc.DrawEllipse(_particleGlowBrush, null, new Point(_particles[i].X, _particles[i].Y), currentSize, currentSize);

                    dc.Pop();
                }
            }

            double coreSize = 20 + (bass * 30);
            dc.DrawEllipse(brush, null, new Point(cx, cy), coreSize, coreSize);
        }

        public void DrawampCRT(float[] data, double width, double height, Brush brush)
        {
            int count = 32;
            if (data.Length < count * 2) return;
            double bw = width / count;

            using var dc = _visual.RenderOpen();
            for (int i = 0; i < count; i++)
            {
                double rawVal = (data[i * 2] + data[i * 2 + 1]) / 2.0;
                double intensity = Math.Sqrt(rawVal) * 300.0;
                intensity = Math.Max(4.0, Math.Min(height * 0.92, intensity));
                intensity = Math.Round(intensity / 10.0) * 10.0;
                double x = i * bw + (bw * 0.1);
                double w = bw * 0.8;
                dc.DrawRectangle(brush, null, new Rect(x, height - intensity, w, intensity));
            }

            for (double y = 0; y < height; y += 4)
                dc.DrawRectangle(_crtScanlineBrush, null, new Rect(0, y, width, 2));
        }

        public void DrawMiniBars(float[] data, double width, double height, Brush brush)
        {
            int count = data.Length;
            double bw = width / count;
            using var dc = _visual.RenderOpen();
            for (int i = 0; i < count; i++)
            {
                double intensity = Math.Sqrt(data[i]) * 150.0;
                intensity = Math.Max(3.0, Math.Min(height, intensity));
                dc.DrawRectangle(brush, null,
                    new Rect(i * bw + 0.5, height - intensity, Math.Max(1.5, bw - 1.0), intensity));
            }
        }

        public void Clear() { using var dc = _visual.RenderOpen(); }
    }

    // =================================================================
    //  MAIN WINDOW & THEME ENGINE
    // =================================================================
    public partial class MainWindow : Window
    {
        private int _currentTheme = 0;
        private bool isMiniPlayer = false;
        private bool isUpdatingSlider = false;
        private double _circularRotationAngle = 0.0;
        private Stopwatch _renderTimer = new Stopwatch();
        private Stopwatch _uiTimer = new Stopwatch();
        private bool _isRendering = false;
        private double _lastFrameDelta = 1.0 / 60.0;

        private string _lastFolderPath = ""; // YENİ: Klasör kaydetmek için değişken

        private const int NUM_BARS = 64;
        private const int MINI_BARS = 32;
        private float[] smoothedFft = new float[NUM_BARS];

        private VisualizerHost? _mainVisHost;
        private VisualizerHost? _miniVisHost;
        private LinearGradientBrush? _visBrush;
        private LinearGradientBrush? _miniVisBrush;
        private Pen? _wavePen;

        private TextBlock? _txtNowPlayingTitle;
        private TextBlock? _txtNowPlayingArtist;
        private TextBlock? _txtMiniTitle;
        private TextBlock? _txtMiniArtist;
        private TextBlock? _txtMetaData;
        private TextBlock? _txtCurrentTime;
        private TextBlock? _txtTotalTime;
        private TextBlock? _txtWidgetLyrics;
        private TextBlock? _txtBigKaraokeLyrics;
        private TextBlock? _txtQueueCount;
        private TextBlock? _txtEqPresetName;

        private TextBlock? _txtSidebarNowPlaying;
        private TextBlock? _txtSidebarArtist;

        private Image? _imgAlbumCover;
        private Image? _imgMiniCover;
        private Image? _imgMiniPlayerCover;
        private RotateTransform? _vinylRotation;
        private System.Windows.Media.Effects.DropShadowEffect? _playGlow;
        private Rectangle? _bgColorRect;
        private Image? _bgAmbientGlow;
        private Canvas? _mainVisCanvas;
        private Canvas? _miniVisCanvas;

        public ObservableCollection<SongModel> Playlist { get; set; } = new ObservableCollection<SongModel>();

        public MainWindow()
        {
            InitializeComponent();
            RenderOptions.ProcessRenderMode = RenderMode.Default;

            LstSongs.ItemsSource = Playlist;

            this.Loaded += (s, e) =>
            {
                CacheUiRefs();
                _bgAmbientGlow = this.FindName("BgAmbientGlow") as Image;
                WindowsServices.EnableAcrylicBlur(this);
                ApplyTheme(0);
                SetupVisualizer();
                SetupShuffleRepeatButtons();
                SetupEqualizer();

                LoadSettings();
            };

            this.StateChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Minimized) StopRendering();
                else if (AudioEngine.Instance.IsPlaying) StartRendering();
            };

            this.Closing += (s, e) =>
            {
                SaveSettings();
                StopRendering();
                AudioEngine.Instance.Dispose();
            };

            AudioEngine.Instance.TrackEnded += (s, e) =>
                Dispatcher.Invoke(() => { if (PlaybackManager.Instance.GoToNext()) PlayCurrentSong(); });

            if (BtnTheme != null) BtnTheme.Click += BtnTheme_Click;
            BtnClose.Click += BtnClose_Click;
            BtnMinimize.Click += BtnMinimize_Click;
            BtnMainToMini.Click += ToggleMiniPlayer;
            BtnAddFolder.Click += BtnAddFolder_Click;

            // DİNAMİK BUTON BAĞLAMALARI (Olası x:Name kopmalarına karşı korumalı)
            if (this.FindName("BtnPlayPause") is Button btnPlay) btnPlay.Click += BtnPlayPause_Click;
            if (this.FindName("BtnPrev") is Button btnPrev) btnPrev.Click += BtnPrev_Click;
            if (this.FindName("BtnNext") is Button btnNext) btnNext.Click += BtnNext_Click;

            BtnMiniClose.Click += BtnClose_Click;
            BtnMiniToMain.Click += ToggleMiniPlayer;
            BtnMiniPlayPause.Click += BtnPlayPause_Click;
            BtnMiniPrev.Click += BtnPrev_Click;
            BtnMiniNext.Click += BtnNext_Click;

            LstSongs.SelectionChanged += LstSongs_SelectionChanged;
            SldVolume.ValueChanged += SldVolume_ValueChanged;

            BindSlider(SldTimeline, () => SldTimeline.Value);
            BindSlider(SldMiniTimeline, () => SldMiniTimeline.Value);

            if (CmbVisualizerMode != null)
                CmbVisualizerMode.SelectionChanged += (s, e) => SetupVisualizer();
            if (VisualizerCanvas != null)
                VisualizerCanvas.SizeChanged += (s, e) => SetupVisualizer();
            if (MiniVisualizerCanvas != null)
                MiniVisualizerCanvas.SizeChanged += (s, e) => SetupVisualizer();
            if (TxtSearch != null)
                TxtSearch.TextChanged += TxtSearch_TextChanged;

            var tabLibrary = this.FindName("TabLibrary") as RadioButton;
            var tabEq = this.FindName("TabEqualizer") as RadioButton;
            var playerPanel = this.FindName("PlayerViewPanel") as Grid;
            var eqPanel = this.FindName("EqViewPanel") as Grid;

            if (tabLibrary != null) tabLibrary.Checked += (s, e) => {
                if (playerPanel != null) playerPanel.Visibility = Visibility.Visible;
                if (eqPanel != null) eqPanel.Visibility = Visibility.Collapsed;
            };
            if (tabEq != null) tabEq.Checked += (s, e) => {
                if (playerPanel != null) playerPanel.Visibility = Visibility.Collapsed;
                if (eqPanel != null) eqPanel.Visibility = Visibility.Visible;
            };
        }

        // --- GÜNCELLENEN: VERİ KAYDETME VE KLASÖR HATIRLAMA ---
        private void SaveSettings()
        {
            try
            {
                var settings = new
                {
                    Volume = SldVolume.Value,
                    ThemeIndex = _currentTheme,
                    EqPresetIndex = FindVisualChildren<ComboBox>(this.FindName("EqViewPanel") as Grid).FirstOrDefault()?.SelectedIndex ?? 0,
                    LastFolderPath = _lastFolderPath // YENİ EKLENDİ
                };
                System.IO.File.WriteAllText("SoundVibe_Config.json", JsonSerializer.Serialize(settings));
            }
            catch { }
        }

        private async void LoadSettings()
        {
            try
            {
                if (System.IO.File.Exists("SoundVibe_Config.json"))
                {
                    var json = System.IO.File.ReadAllText("SoundVibe_Config.json");
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("Volume", out var v))
                    {
                        SldVolume.Value = v.GetDouble();
                        AudioEngine.Instance.SetVolume((float)(SldVolume.Value / 100.0));
                    }
                    if (doc.RootElement.TryGetProperty("ThemeIndex", out var t))
                    {
                        _currentTheme = t.GetInt32();
                        ApplyTheme(_currentTheme);
                    }
                    if (doc.RootElement.TryGetProperty("EqPresetIndex", out var eq))
                    {
                        var presetCombo = FindVisualChildren<ComboBox>(this.FindName("EqViewPanel") as Grid).FirstOrDefault();
                        if (presetCombo != null) presetCombo.SelectedIndex = eq.GetInt32();
                    }
                    if (doc.RootElement.TryGetProperty("LastFolderPath", out var folderPath))
                    {
                        _lastFolderPath = folderPath.GetString() ?? "";
                        if (!string.IsNullOrEmpty(_lastFolderPath) && System.IO.Directory.Exists(_lastFolderPath))
                        {
                            await LoadFolderAsync(_lastFolderPath);
                        }
                    }
                }
            }
            catch { }
        }

        // --- YENİ EKLENEN: OSD / TOAST BİLDİRİM METODU ---
        private void ShowToastNotification(SongModel song)
        {
            // Yeni bağımsız pencereyi oluşturur ve ekranda uçurur!
            new ToastWindow(song).Show();
        }
        // ----------------------------------------

        private void StartRendering()
        {
            if (!_isRendering)
            {
                CompositionTarget.Rendering += RenderLoop;
                _renderTimer.Start();
                _uiTimer.Start();
                _isRendering = true;
            }
        }

        private void StopRendering()
        {
            if (_isRendering)
            {
                CompositionTarget.Rendering -= RenderLoop;
                _renderTimer.Stop();
                _renderTimer.Reset();
                _uiTimer.Stop();
                _uiTimer.Reset();
                _isRendering = false;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T t) yield return t;
                    foreach (T childOfChild in FindVisualChildren<T>(child)) yield return childOfChild;
                }
            }
        }

        private void SetupShuffleRepeatButtons()
        {
            var shuffleBtn = this.FindName("BtnShuffle") as Button;
            if (shuffleBtn != null) shuffleBtn.Click += (s, e) => {
                PlaybackManager.Instance.IsShuffle = !PlaybackManager.Instance.IsShuffle;
                shuffleBtn.Foreground = PlaybackManager.Instance.IsShuffle ? (Brush)FindResource("ThemeAccentBrush") : Brushes.White;
            };

            var repeatBtn = this.FindName("BtnRepeat") as Button;
            if (repeatBtn != null) repeatBtn.Click += (s, e) => {
                PlaybackManager.Instance.IsRepeat = !PlaybackManager.Instance.IsRepeat;
                repeatBtn.Foreground = PlaybackManager.Instance.IsRepeat ? (Brush)FindResource("ThemeAccentBrush") : Brushes.White;
            };
        }

        // EQ SLIDER GERÇEK ZAMANLI BAĞLANTISI (Slider değeri anında NAudio'ya gidiyor)
        private void SetupEqualizer()
        {
            var eqPanel = this.FindName("EqViewPanel") as Grid;
            if (eqPanel == null) return;

            var sliders = FindVisualChildren<Slider>(eqPanel).Where(s => s.Orientation == Orientation.Vertical).ToList();
            for (int i = 0; i < sliders.Count && i < 10; i++)
            {
                int bandIndex = i;
                sliders[i].ValueChanged += (s, e) => {
                    float gain = (float)(sliders[bandIndex].Value - 50) * (15f / 50f);
                    AudioEngine.Instance.SetEqGain(bandIndex, gain);
                };
            }

            var presetCombo = FindVisualChildren<ComboBox>(eqPanel).FirstOrDefault();
            if (presetCombo != null)
            {
                presetCombo.SelectionChanged += (s, e) => ApplyEqPreset(presetCombo.SelectedIndex, sliders);
            }
        }

        private void ApplyEqPreset(int index, List<Slider> sliders)
        {
            string[] names = { "Flat (Normal)", "Bass Boost", "Vocal Enhancer", "Electronic" };
            float[][] presets = {
                new float[] { 50, 50, 50, 50, 50, 50, 50, 50, 50, 50 },
                new float[] { 70, 65, 60, 55, 50, 45, 50, 55, 60, 65 },
                new float[] { 45, 45, 50, 60, 65, 65, 60, 50, 45, 45 },
                new float[] { 65, 60, 50, 45, 45, 50, 55, 60, 65, 65 }
            };
            if (index >= 0 && index < presets.Length && sliders.Count >= 10)
            {
                for (int i = 0; i < 10; i++) sliders[i].Value = presets[index][i];
                if (_txtEqPresetName != null) _txtEqPresetName.Text = names[index];
            }
        }

        private static (
            Color c1, Color c2, Color c3, Color accent,
            Color glassColor, Color darkBgColor, Color windowBg1, Color windowBg2,
            string borderHex, double ambientOpacity, string name
        )[] _themeDefs = new[]
        {
            ( ParseColor("#FFFF006E"), ParseColor("#FF8A2BE2"), ParseColor("#FF00F0FF"), ParseColor("#FFFF006E"), Color.FromArgb(22, 255, 0, 110), Color.FromArgb(215, 8, 3, 20), Color.FromArgb(85, 15, 2, 32), Color.FromArgb(75, 6, 0, 18), "#30FF006E", 0.40, "🌃 Neon Dream" ),
            ( ParseColor("#FF00AAFF"), ParseColor("#FF80D8FF"), ParseColor("#FFE8F8FF"), ParseColor("#FF00AAFF"), Color.FromArgb(55, 135, 206, 250), Color.FromArgb(165, 5, 20, 65), Color.FromArgb(90, 3, 14, 45), Color.FromArgb(80, 2, 8, 30), "#3800AAFF", 0.55, "💎 Aero Glass" ),
            ( ParseColor("#FF1DB954"), ParseColor("#FF148040"), ParseColor("#FF3DDC84"), ParseColor("#FF1DB954"), Color.FromArgb(18, 255, 255, 255), Color.FromArgb(238, 18, 18, 18), Color.FromArgb(255, 18, 18, 18), Color.FromArgb(255, 12, 12, 12), "#201DB954", 0.12, "🎵 Midnight Green" ),
            ( ParseColor("#FF2ECC00"), ParseColor("#FFFF6600"), ParseColor("#FFFFD700"), ParseColor("#FF2ECC00"), Color.FromArgb(18, 47, 217, 0), Color.FromArgb(248, 4, 4, 4), Color.FromArgb(255, 5, 5, 5), Color.FromArgb(255, 2, 2, 2), "#282ECC00", 0.25, "📺 RetroClassic" ),
            ( Colors.Gray, Colors.DarkGray, Colors.White, Colors.Gray, Color.FromArgb(45, 128, 128, 128), Color.FromArgb(200, 10, 10, 10), Color.FromArgb(255, 15, 15, 15), Color.FromArgb(255, 5, 5, 5), "#30808080", 0.50, "🎨 Dinamik Adaptif" )
        };

        private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static Color AdjustColor(Color color, double factor)
        {
            byte r = (byte)Math.Max(0, Math.Min(255, color.R * factor));
            byte g = (byte)Math.Max(0, Math.Min(255, color.G * factor));
            byte b = (byte)Math.Max(0, Math.Min(255, color.B * factor));
            return Color.FromRgb(r, g, b);
        }

        private void GenerateDynamicTheme(Color dom)
        {
            Color c1 = AdjustColor(dom, 1.2);
            Color c2 = AdjustColor(dom, 0.8);
            Color c3 = AdjustColor(dom, 1.5);
            Color glass = Color.FromArgb(50, dom.R, dom.G, dom.B);
            Color darkBg = Color.FromArgb(230, (byte)(dom.R * 0.1), (byte)(dom.G * 0.1), (byte)(dom.B * 0.1));
            Color bg1 = Color.FromArgb(200, (byte)(dom.R * 0.08), (byte)(dom.G * 0.08), (byte)(dom.B * 0.08));
            Color bg2 = Color.FromArgb(220, (byte)(dom.R * 0.03), (byte)(dom.G * 0.03), (byte)(dom.B * 0.03));
            string hex = $"#50{dom.R:X2}{dom.G:X2}{dom.B:X2}";
            _themeDefs[4] = (c1, c2, c3, dom, glass, darkBg, bg1, bg2, hex, 0.60, "🎨 Dinamik Adaptif");
        }

        private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            _currentTheme = (_currentTheme + 1) % _themeDefs.Length;
            Color dom = Colors.Black;
            if (_bgColorRect?.Fill is RadialGradientBrush rgb && rgb.GradientStops.Count > 0)
                dom = rgb.GradientStops[0].Color;
            if (_currentTheme == 4) GenerateDynamicTheme(dom);
            ApplyTheme(_currentTheme);
            SetFakeAmbientGlow(dom);
        }

        private void ApplyTheme(int index)
        {
            var t = _themeDefs[index];
            var sg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            sg.GradientStops.Add(new GradientStop(t.c1, 0.0));
            sg.GradientStops.Add(new GradientStop(t.c2, 1.0));
            sg.Freeze();
            this.Resources["ThemeSliderGradient"] = sg;

            var vsg = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
            vsg.GradientStops.Add(new GradientStop(t.c1, 0.0));
            vsg.GradientStops.Add(new GradientStop(t.c2, 1.0));
            vsg.Freeze();
            this.Resources["ThemeVerticalSliderGradient"] = vsg;

            var mg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            mg.GradientStops.Add(new GradientStop(t.c1, 0.0));
            mg.GradientStops.Add(new GradientStop(t.c2, 1.0));
            mg.Freeze();
            this.Resources["ThemeMiniGradient"] = mg;

            var wbg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            wbg.GradientStops.Add(new GradientStop(t.windowBg1, 0.0));
            wbg.GradientStops.Add(new GradientStop(t.windowBg2, 0.5));
            wbg.GradientStops.Add(new GradientStop(t.windowBg1, 1.0));
            wbg.Freeze();
            this.Resources["ThemeWindowBg"] = wbg;

            var borderBrush = new SolidColorBrush(ParseColor(t.borderHex));
            borderBrush.Freeze();
            this.Resources["ThemeWindowBorderBrush"] = borderBrush;

            var accentBrush = new SolidColorBrush(t.accent); accentBrush.Freeze();
            var accentLightBrush = new SolidColorBrush(t.c1); accentLightBrush.Freeze();
            var glassBrush = new SolidColorBrush(t.glassColor);
            glassBrush.Freeze();
            var darkBgBrush = new SolidColorBrush(t.darkBgColor); darkBgBrush.Freeze();

            this.Resources["ThemeAccentColor"] = t.accent;
            this.Resources["ThemeAccentBrush"] = accentBrush;
            this.Resources["ThemeAccentLightBrush"] = accentLightBrush;
            this.Resources["ThemeGlassBrush"] = glassBrush;
            this.Resources["ThemeDarkBgBrush"] = darkBgBrush;

            var visBrush = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
            visBrush.GradientStops.Add(new GradientStop(t.c1, 0.0));
            visBrush.GradientStops.Add(new GradientStop(t.c2, 0.55));
            visBrush.GradientStops.Add(new GradientStop(t.c3, 1.0));
            visBrush.Freeze();
            _visBrush = visBrush;

            var miniBrush = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(1, 0) };
            miniBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, t.c1.R, t.c1.G, t.c1.B), 0.0));
            miniBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, t.c3.R, t.c3.G, t.c3.B), 1.0));
            miniBrush.Freeze();
            _miniVisBrush = miniBrush;

            _wavePen = new Pen(_visBrush, 2.5) { LineJoin = PenLineJoin.Round };
            _wavePen.Freeze();

            if (_playGlow != null) _playGlow.Color = t.accent;
            var bgAmbient = this.FindName("BgAmbientGlow") as System.Windows.Controls.Image;
            if (bgAmbient != null) bgAmbient.Opacity = t.ambientOpacity;

            if (BtnTheme != null) BtnTheme.Content = t.name;
            ApplyThemeBrushesToHosts();
        }

        private void ApplyThemeBrushesToHosts()
        {
            var t = _themeDefs[_currentTheme];
            Color crtColor = _currentTheme switch
            {
                1 => Color.FromArgb(60, 180, 230, 255),
                2 => Color.FromArgb(160, 0, 0, 0),
                3 => Color.FromArgb(120, 0, 0, 0),
                4 => Color.FromArgb(90, (byte)(t.accent.R * 0.5), (byte)(t.accent.G * 0.5), (byte)(t.accent.B * 0.5)),
                _ => Color.FromArgb(100, 0, 0, 0)
            };
            _mainVisHost?.UpdateThemeBrushes(t.accent, crtColor);
            _miniVisHost?.UpdateThemeBrushes(t.accent, crtColor);
        }

        private void CacheUiRefs()
        {
            _txtNowPlayingTitle = this.FindName("TxtNowPlayingTitle") as TextBlock;
            _txtNowPlayingArtist = this.FindName("TxtNowPlayingArtist") as TextBlock;
            _txtMiniTitle = this.FindName("TxtMiniTitle") as TextBlock;
            _txtMiniArtist = this.FindName("TxtMiniArtist") as TextBlock;
            _txtMetaData = this.FindName("TxtMetaData") as TextBlock;
            _txtCurrentTime = this.FindName("TxtCurrentTime") as TextBlock;
            _txtTotalTime = this.FindName("TxtTotalTime") as TextBlock;
            _txtWidgetLyrics = this.FindName("TxtWidgetLyrics") as TextBlock;
            _txtBigKaraokeLyrics = this.FindName("TxtBigKaraokeLyrics") as TextBlock;
            _txtQueueCount = this.FindName("TxtQueueCount") as TextBlock;
            _txtEqPresetName = this.FindName("TxtEqPresetName") as TextBlock;

            _txtSidebarNowPlaying = this.FindName("TxtSidebarNowPlaying") as TextBlock;
            _txtSidebarArtist = this.FindName("TxtSidebarArtist") as TextBlock;

            _imgAlbumCover = this.FindName("ImgAlbumCover") as Image;
            _imgMiniCover = this.FindName("ImgMiniCover") as Image;
            _imgMiniPlayerCover = this.FindName("ImgMiniPlayerCover") as Image;
            _vinylRotation = this.FindName("VinylRotation") as RotateTransform;
            _playGlow = this.FindName("PlayGlow") as System.Windows.Media.Effects.DropShadowEffect;
            _bgColorRect = this.FindName("BgColorRect") as Rectangle;
            _mainVisCanvas = this.FindName("VisualizerCanvas") as Canvas;
            _miniVisCanvas = this.FindName("MiniVisualizerCanvas") as Canvas;
        }

        private void BindSlider(Slider? sld, Func<double> getValue)
        {
            if (sld == null) return;
            sld.ApplyTemplate();

            if (sld.Template.FindName("PART_Track", sld)
                is System.Windows.Controls.Primitives.Track track)
            {
                track.Thumb?.ClearValue(StyleProperty);
            }

            sld.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
                new System.Windows.Controls.Primitives.DragStartedEventHandler((s, e) => isUpdatingSlider = true));

            sld.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
                new System.Windows.Controls.Primitives.DragCompletedEventHandler((s, e) => {
                    AudioEngine.Instance.Seek(TimeSpan.FromSeconds(getValue()));
                    isUpdatingSlider = false;
                }));

            sld.PreviewMouseLeftButtonDown += (s, e) => {
                isUpdatingSlider = true;
            };

            sld.PreviewMouseLeftButtonUp += (s, e) => {
                double mouseX = e.GetPosition(sld).X;
                double ratio = mouseX / sld.ActualWidth;
                ratio = Math.Max(0.0, Math.Min(1.0, ratio));

                double seekTo = sld.Minimum + ratio * (sld.Maximum - sld.Minimum);
                AudioEngine.Instance.Seek(TimeSpan.FromSeconds(seekTo));
                isUpdatingSlider = false;
            };
        }

        private void SetupVisualizer()
        {
            if (_mainVisCanvas == null || CmbVisualizerMode == null) return;
            _mainVisCanvas.Children.Clear();
            _miniVisCanvas?.Children.Clear();

            _mainVisHost = new VisualizerHost();
            double w = _mainVisCanvas.ActualWidth > 0 ? _mainVisCanvas.ActualWidth : 700;
            double h = _mainVisCanvas.ActualHeight > 0 ? _mainVisCanvas.ActualHeight : 150;
            _mainVisHost.Width = w; _mainVisHost.Height = h;
            _mainVisCanvas.Children.Add(_mainVisHost);

            _mainVisHost.ResetHistory();
            ApplyThemeBrushesToHosts();

            int mode = CmbVisualizerMode.SelectedIndex;
            if (_txtBigKaraokeLyrics != null) _txtBigKaraokeLyrics.Visibility = (mode == 12) ? Visibility.Visible : Visibility.Collapsed;

            bool needsOverflow = (mode == 4 || mode == 8 || mode == 9 || mode == 10);
            _mainVisCanvas.ClipToBounds = !needsOverflow;

            if (_mainVisCanvas.Parent is Grid parentGrid)
            {
                foreach (var child in parentGrid.Children)
                {
                    if (child is Rectangle rect && rect.Fill is VisualBrush vb && vb.Visual == _mainVisCanvas)
                    {
                        rect.Visibility = (mode == 4 || mode >= 8) ? Visibility.Collapsed : Visibility.Visible;
                        break;
                    }
                }
            }

            if (_miniVisCanvas != null)
            {
                _miniVisHost = new VisualizerHost();
                double mw = _miniVisCanvas.ActualWidth > 0 ? _miniVisCanvas.ActualWidth : 520;
                _miniVisHost.Width = mw;
                _miniVisHost.Height = _miniVisCanvas.ActualHeight > 0 ? _miniVisCanvas.ActualHeight : 80;
                _miniVisCanvas.Children.Add(_miniVisHost);
            }
        }

        private void RenderLoop(object? sender, EventArgs e)
        {
            if (_txtWidgetLyrics != null)
                _txtWidgetLyrics.Text = LyricEngine.Instance.GetCurrentLyric(AudioEngine.Instance.CurrentTime);

            if (_uiTimer.ElapsedMilliseconds > 250)
            {
                double total = AudioEngine.Instance.TotalTime.TotalSeconds;
                double current = AudioEngine.Instance.CurrentTime.TotalSeconds;

                if (!isUpdatingSlider)
                {
                    if (isMiniPlayer && SldMiniTimeline != null)
                    {
                        SldMiniTimeline.Maximum = total;
                        SldMiniTimeline.Value = current;
                    }
                    else if (SldTimeline != null)
                    {
                        SldTimeline.Maximum = total;
                        SldTimeline.Value = current;
                    }
                }

                if (_txtCurrentTime != null) _txtCurrentTime.Text = AudioEngine.Instance.CurrentTime.ToString(@"mm\:ss");
                if (_txtTotalTime != null) _txtTotalTime.Text = AudioEngine.Instance.TotalTime.ToString(@"mm\:ss");
                if (_txtQueueCount != null) _txtQueueCount.Text = PlaybackManager.Instance.QueueCount.ToString();

                _uiTimer.Restart();
            }

            if (!AudioEngine.Instance.IsPlaying) return;
            if (_renderTimer.ElapsedMilliseconds < 16) return;

            _lastFrameDelta = Math.Min(_renderTimer.ElapsedMilliseconds / 1000.0, 0.1);
            _renderTimer.Restart();

            if (!isMiniPlayer && _vinylRotation != null)
            {
                _vinylRotation.Angle += 0.4;
                if (_vinylRotation.Angle >= 360.0) _vinylRotation.Angle = 0.0;
            }

            float[] rawFft = AudioEngine.Instance.GetFftData();
            if (rawFft == null || rawFft.Length == 0) return;

            for (int i = 0; i < NUM_BARS && i < rawFft.Length; i++)
                smoothedFft[i] += (rawFft[i] - smoothedFft[i]) * 0.28f;

            double bass = smoothedFft[0] + smoothedFft[1] + smoothedFft[2];

            if (_bgAmbientGlow != null)
            {
                double baseOpacity = _themeDefs[_currentTheme].ambientOpacity;
                _bgAmbientGlow.Opacity = Math.Min(1.0, baseOpacity + (bass * 0.15));
            }

            if (!isMiniPlayer && _playGlow != null)
            {
                _playGlow.BlurRadius = Math.Min(50.0, 10.0 + Math.Sqrt(bass) * 38.0);
                double maxOpacity = (_currentTheme == 2 || _currentTheme == 3) ? 0.65 : 1.0;
                _playGlow.Opacity = Math.Min(maxOpacity, 0.3 + Math.Sqrt(bass) * 0.58);
            }

            _circularRotationAngle += 0.015;
            if (_circularRotationAngle > Math.PI * 2) _circularRotationAngle -= Math.PI * 2;

            int visMode = CmbVisualizerMode?.SelectedIndex ?? 0;
            if (visMode == 12 && !isMiniPlayer && _txtBigKaraokeLyrics != null)
                _txtBigKaraokeLyrics.Text = LyricEngine.Instance.GetCurrentLyric(AudioEngine.Instance.CurrentTime);

            if (!isMiniPlayer && _mainVisHost != null && _mainVisCanvas != null && visMode != 12)
            {
                double h = _mainVisCanvas.ActualHeight;
                double w = _mainVisCanvas.ActualWidth;
                if (w < 1 || h < 1) return;

                switch (visMode)
                {
                    case 0: _mainVisHost.DrawBars(smoothedFft, w, h, _visBrush!, mirror: false); break;
                    case 1: _mainVisHost.DrawCyberWave(smoothedFft, w, h, _visBrush!, _wavePen!); break;
                    case 2: _mainVisHost.DrawClassicWave(smoothedFft, w, h, _wavePen!); break;
                    case 3: _mainVisHost.DrawBars(smoothedFft, w, h, _visBrush!, mirror: true); break;
                    case 4: _mainVisHost.DrawClassicCircular(smoothedFft, w, h, _wavePen!, bass, _circularRotationAngle); break;
                    case 5: _mainVisHost.DrawSmoothedBlocks(smoothedFft, w, h, _visBrush!); break;
                    case 6: _mainVisHost.DrawFractureBars(smoothedFft, w, h, _visBrush!, bass); break;
                    case 7: _mainVisHost.DrawLiquidWave(smoothedFft, w, h, _wavePen!); break;
                    case 8: _mainVisHost.DrawOrbitalGalaxy(smoothedFft, w, h, _wavePen!, bass); break;
                    case 9: _mainVisHost.DrawAudioTunnel(smoothedFft, w, h, _wavePen!, bass); break;
                    case 10: _mainVisHost.DrawParticleReactor(smoothedFft, w, h, _visBrush!, bass, _lastFrameDelta); break;
                    case 11: _mainVisHost.DrawampCRT(smoothedFft, w, h, _visBrush!); break;
                }
            }
            else if (isMiniPlayer && _miniVisHost != null && _miniVisCanvas != null)
            {
                double mh = _miniVisCanvas.ActualHeight;
                double mw = _miniVisCanvas.ActualWidth;
                if (mw < 1 || mh < 1) return;

                var miniFft = new float[MINI_BARS];
                for (int i = 0; i < MINI_BARS; i++) miniFft[i] = smoothedFft[i * 2];
                _miniVisHost.DrawMiniBars(miniFft, mw, mh, _miniVisBrush!);
            }
        }

        private void SetFakeAmbientGlow(Color dominant)
        {
            if (_bgColorRect == null) return;
            RadialGradientBrush brush;
            switch (_currentTheme)
            {
                case 1:
                    brush = new RadialGradientBrush();
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(110, 0, 170, 255), 0.0)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(30, 0, 80, 160), 1.0)); break;
                case 2:
                    brush = new RadialGradientBrush();
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 18, 18, 18), 0.0)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 10, 10, 10), 1.0)); break;
                case 3:
                    brush = new RadialGradientBrush();
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 8, 8, 8), 0.0)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 2, 2, 2), 1.0)); break;
                case 4:
                    brush = new RadialGradientBrush();
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(180, dominant.R, dominant.G, dominant.B), 0.0)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, dominant.R, dominant.G, dominant.B), 1.0)); break;
                default:
                    brush = new RadialGradientBrush();
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(160, dominant.R, dominant.G, dominant.B), 0.0)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, dominant.R, dominant.G, dominant.B), 1.0)); break;
            }
            brush.Freeze();
            _bgColorRect.Fill = brush;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var src = e.OriginalSource as DependencyObject;
            while (src != null)
            {
                if (src is Button || src is ListBox || src is ListBoxItem ||
                    src is Slider || src is ScrollViewer || src is TextBox ||
                    src is System.Windows.Controls.Primitives.Thumb ||
                    src is System.Windows.Controls.Primitives.RepeatButton)
                    return;
                src = VisualTreeHelper.GetParent(src);
            }

            this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = TxtSearch.Text.ToLower();
            ICollectionView view = CollectionViewSource.GetDefaultView(Playlist);
            view.Filter = item => {
                if (string.IsNullOrWhiteSpace(q)) return true;
                if (item is SongModel s) return s.Title.ToLower().Contains(q) || s.Artist.ToLower().Contains(q);
                return false;
            };
        }

        private void MenuPlayNext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem m && m.DataContext is SongModel s) PlaybackManager.Instance.AddPlayNext(s);
        }
        private void MenuAddQueue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem m && m.DataContext is SongModel s) PlaybackManager.Instance.AddLastQueue(s);
        }

        private void ToggleMiniPlayer(object sender, RoutedEventArgs e)
        {
            isMiniPlayer = !isMiniPlayer;
            var mainUI = this.FindName("MainUI") as Grid;
            var miniUI = this.FindName("MiniUI") as Grid;

            if (isMiniPlayer)
            {
                if (mainUI != null) mainUI.Visibility = Visibility.Collapsed;
                if (miniUI != null) miniUI.Visibility = Visibility.Visible;
                this.Width = 450; this.Height = 80; this.Topmost = true;
                var wa = SystemParameters.WorkArea;
                this.Left = wa.Right - 465; this.Top = wa.Bottom - 95;
            }
            else
            {
                if (mainUI != null) mainUI.Visibility = Visibility.Visible;
                if (miniUI != null) miniUI.Visibility = Visibility.Collapsed;
                this.Width = 1100; this.Height = 750; this.Topmost = false;
                var wa = SystemParameters.WorkArea;
                this.Left = (wa.Width - 1100) / 2; this.Top = (wa.Height - 750) / 2;
            }
            SetupVisualizer();
        }

        private async void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "SoundVibe Müzik Klasörü Seçimi" };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            await LoadFolderAsync(dlg.SelectedPath);
        }

        // --- GÜNCELLENEN: YÜKLEME KODUNU DIŞARI ALDIK (OTOMATİK YÜKLEME İÇİN) ---
        private async Task LoadFolderAsync(string path)
        {
            if (BtnAddFolder != null)
            {
                BtnAddFolder.Content = "⏳ Yükleniyor...";
                BtnAddFolder.IsEnabled = false;
            }

            Playlist.Clear();
            string[] extensions = { "*.mp3", "*.flac", "*.wav", "*.ogg", "*.m4a", "*.aiff", "*.aac" };
            string[] files = extensions
                .SelectMany(ext => System.IO.Directory.GetFiles(path, ext, SearchOption.AllDirectories))
                .Distinct()
                .OrderBy(f => f)
                .ToArray();

            var loadedSongs = await Task.Run(() =>
            {
                var temp = new List<SongModel>();
                foreach (var file in files)
                {
                    var song = SongModel.CreateFromFile(file);
                    if (song != null) temp.Add(song);
                }
                return temp;
            });

            int chunkSize = 50;
            for (int i = 0; i < loadedSongs.Count; i += chunkSize)
            {
                var chunk = loadedSongs.Skip(i).Take(chunkSize);
                foreach (var song in chunk) Playlist.Add(song);
                await Task.Delay(1);
            }

            if (TxtSearch != null) TxtSearch.Text = "";
            PlaybackManager.Instance.UpdateQueue(Playlist.ToList());

            if (BtnAddFolder != null)
            {
                BtnAddFolder.Content = "📁 Add Folder";
                BtnAddFolder.IsEnabled = true;
            }
            _lastFolderPath = path;
        }

        private void LstSongs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is SongModel song)
            {
                if (PlaybackManager.Instance.CurrentSong == song) return;
                PlaybackManager.Instance.SetCurrentSong(song);
                PlayCurrentSong();
            }
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (AudioEngine.Instance.CurrentTime.TotalSeconds > 3.0)
            {
                AudioEngine.Instance.Seek(TimeSpan.Zero);
                return;
            }
            if (PlaybackManager.Instance.GoToPrevious()) PlayCurrentSong();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (PlaybackManager.Instance.GoToNext()) PlayCurrentSong();
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (AudioEngine.Instance.IsPlaying)
            {
                AudioEngine.Instance.Pause();
                UpdatePlayButtons(false);
                StopRendering();
            }
            else
            {
                AudioEngine.Instance.Resume();
                UpdatePlayButtons(true);
                StartRendering();
            }
        }

        private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => AudioEngine.Instance.SetVolume((float)(SldVolume.Value / 100.0));

        private async void PlayCurrentSong()
        {
            var song = PlaybackManager.Instance.CurrentSong;
            if (song == null) return;

            LstSongs.SelectedItem = song;
            LstSongs.ScrollIntoView(song);

            float vol = SldVolume != null ? (float)(SldVolume.Value / 100.0) : 0.5f;
            AudioEngine.Instance.Play(song.FilePath, vol);
            LyricEngine.Instance.LoadLyrics(song.FilePath);

            Array.Clear(smoothedFft, 0, smoothedFft.Length);
            SetupVisualizer();
            UpdatePlayButtons(true);
            StartRendering();

            Color domColor = Color.FromRgb(74, 144, 226);
            if (song.CoverImage != null)
            {
                int stride = 8 * 4;
                var pixels = new byte[8 * stride];
                try
                {
                    var scaled = new TransformedBitmap(song.CoverImage, new ScaleTransform(8.0 / song.CoverImage.PixelWidth, 8.0 / song.CoverImage.PixelHeight));
                    scaled.CopyPixels(pixels, stride, 0);

                    domColor = await Task.Run(() => {
                        long r = 0, g = 0, b = 0, cnt = 0;
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte pb = pixels[i], pg = pixels[i + 1], pr = pixels[i + 2];
                            if (pr + pg + pb > 80) { r += pr; g += pg; b += pb; cnt++; }
                        }
                        if (cnt == 0) return Color.FromRgb(74, 144, 226);
                        return Color.FromRgb((byte)Math.Min(255, r / cnt * 1.3), (byte)Math.Min(255, g / cnt * 1.1), (byte)Math.Min(255, b / cnt * 1.3));
                    });
                }
                catch { }
            }

            if (_currentTheme == 4)
            {
                GenerateDynamicTheme(domColor);
                ApplyTheme(4);
            }
            UpdateUI(song, domColor);
            ShowToastNotification(song); // OSD BİLDİRİMİNİ ÇAĞIRIYORUZ
        }

        private void UpdatePlayButtons(bool playing)
        {
            string icon = playing ? "⏸" : "▶";
            if (BtnPlayPause != null) BtnPlayPause.Content = icon;
            if (BtnMiniPlayPause != null) BtnMiniPlayPause.Content = icon;
        }

        private void UpdateUI(SongModel song, Color dominantColor)
        {
            if (_txtNowPlayingTitle != null) _txtNowPlayingTitle.Text = song.Title;
            if (_txtMiniTitle != null) _txtMiniTitle.Text = song.Title;
            if (_txtNowPlayingArtist != null) _txtNowPlayingArtist.Text = song.Artist;
            if (_txtMiniArtist != null) _txtMiniArtist.Text = song.Artist;

            if (_txtMetaData != null)
            {
                string fmt = string.IsNullOrWhiteSpace(song.FilePath) ? "MP3" : System.IO.Path.GetExtension(song.FilePath).TrimStart('.').ToUpperInvariant();
                if (string.IsNullOrEmpty(fmt)) fmt = "MP3";
                _txtMetaData.Text = $"{fmt} • {song.Bitrate} kbps • {(song.SampleRate / 1000.0):0.0} kHz";
            }

            if (_txtSidebarNowPlaying != null) _txtSidebarNowPlaying.Text = song.Title;
            if (_txtSidebarArtist != null) _txtSidebarArtist.Text = song.Artist;
            if (_imgAlbumCover != null)
            {
                RenderOptions.SetBitmapScalingMode(_imgAlbumCover, BitmapScalingMode.HighQuality);
                _imgAlbumCover.Source = song.CoverImage;
            }
            if (_imgMiniCover != null) _imgMiniCover.Source = song.CoverImage;
            if (_imgMiniPlayerCover != null) _imgMiniPlayerCover.Source = song.CoverImage;

            SetFakeAmbientGlow(dominantColor);
        }
    }

    #region [ 2. PLAYBACK & QUEUE MANAGER ]
    public class PlaybackManager
    {
        public static PlaybackManager Instance { get; } = new PlaybackManager();

        private List<SongModel> _originalQueue = new List<SongModel>();
        private List<SongModel> _shuffledQueue = new List<SongModel>();
        private List<SongModel> _userQueue = new List<SongModel>();
        private int _currentIndex = -1;
        private Random _rnd = new Random();

        private bool _isShuffle = false;

        public bool IsShuffle
        {
            get => _isShuffle;
            set
            {
                _isShuffle = value;
                if (_isShuffle) GenerateShuffleQueue();
            }
        }

        public bool IsRepeat { get; set; } = false;

        public SongModel? CurrentSong
        {
            get
            {
                var targetList = IsShuffle ? _shuffledQueue : _originalQueue;
                return (_currentIndex >= 0 && _currentIndex < targetList.Count) ? targetList[_currentIndex] : null;
            }
        }

        public int QueueCount => _userQueue.Count;

        private PlaybackManager() { }

        public void UpdateQueue(List<SongModel> songs)
        {
            _originalQueue = new List<SongModel>(songs);
            if (IsShuffle) GenerateShuffleQueue();
        }

        private void GenerateShuffleQueue()
        {
            _shuffledQueue = new List<SongModel>(_originalQueue);
            int n = _shuffledQueue.Count;
            while (n > 1)
            {
                n--;
                int k = _rnd.Next(n + 1);
                var value = _shuffledQueue[k];
                _shuffledQueue[k] = _shuffledQueue[n];
                _shuffledQueue[n] = value;
            }

            if (_currentIndex >= 0 && _currentIndex < _originalQueue.Count)
            {
                var current = _originalQueue[_currentIndex];
                _shuffledQueue.Remove(current);
                _shuffledQueue.Insert(0, current);
                _currentIndex = 0;
            }
        }

        public void AddPlayNext(SongModel song) => _userQueue.Insert(0, song);
        public void AddLastQueue(SongModel song) => _userQueue.Add(song);

        public void SetCurrentSong(SongModel song)
        {
            var targetList = IsShuffle ? _shuffledQueue : _originalQueue;
            _currentIndex = targetList.IndexOf(song);
        }

        public bool GoToNext()
        {
            var targetList = IsShuffle ? _shuffledQueue : _originalQueue;
            if (targetList.Count == 0) return false;
            if (IsRepeat) return true;

            if (_userQueue.Count > 0)
            {
                var next = _userQueue[0];
                _userQueue.RemoveAt(0);
                SetCurrentSong(next);
                return true;
            }

            _currentIndex++;
            if (_currentIndex >= targetList.Count)
            {
                _currentIndex = 0;
                if (IsShuffle) GenerateShuffleQueue();
            }
            return true;
        }

        public bool GoToPrevious()
        {
            var targetList = IsShuffle ? _shuffledQueue : _originalQueue;
            if (targetList.Count == 0) return false;
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = targetList.Count - 1;
            return true;
        }
    }
    #endregion

    #region [ 3. ASYNC DATA MODEL ]
    public class SongModel
    {
        public string Title { get; set; } = "Bilinmeyen Şarkı";
        public string Artist { get; set; } = "Bilinmeyen Sanatçı";
        public string FilePath { get; set; } = "";
        public BitmapImage? CoverImage { get; set; }
        public int Bitrate { get; set; }
        public int SampleRate { get; set; }

        public static SongModel? CreateFromFile(string path)
        {
            try
            {
                using var tagFile = TagLib.File.Create(path);
                var song = new SongModel
                {
                    FilePath = path,
                    Title = string.IsNullOrWhiteSpace(tagFile.Tag.Title) ? Path.GetFileNameWithoutExtension(path) : tagFile.Tag.Title,
                    Artist = string.IsNullOrWhiteSpace(string.Join(", ", tagFile.Tag.Performers)) ? "Bilinmeyen Sanatçı" : string.Join(", ", tagFile.Tag.Performers),
                    Bitrate = tagFile.Properties.AudioBitrate != 0 ? tagFile.Properties.AudioBitrate : ((tagFile.Properties.AudioSampleRate * 16 * 2) / 1000),
                    SampleRate = tagFile.Properties.AudioSampleRate
                };

                if (tagFile.Tag.Pictures.Length >= 1)
                {
                    var bin = (byte[])tagFile.Tag.Pictures[0].Data.Data;
                    using var ms = new MemoryStream(bin);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 350;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    song.CoverImage = bmp;
                }
                return song;
            }
            catch
            {
                return null;
            }
        }
    }
    #endregion

    #region [ 4. AUDIO ENGINE & EQUALIZER ]

    public class EqualizerProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BiQuadFilter[,] _filters;
        private readonly float[] _gains = new float[10];
        private readonly float[] _frequencies = { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        public WaveFormat WaveFormat => _source.WaveFormat;

        public EqualizerProvider(ISampleProvider source)
        {
            _source = source;
            int channels = source.WaveFormat.Channels;
            _filters = new BiQuadFilter[channels, _frequencies.Length];

            for (int c = 0; c < channels; c++)
            {
                for (int b = 0; b < _frequencies.Length; b++)
                {
                    _filters[c, b] = BiQuadFilter.PeakingEQ(source.WaveFormat.SampleRate, _frequencies[b], 1.0f, 0f);
                }
            }
        }

        public void UpdateGain(int bandIndex, float gainDb)
        {
            if (bandIndex < 0 || bandIndex >= _frequencies.Length) return;
            _gains[bandIndex] = gainDb;
            for (int c = 0; c < WaveFormat.Channels; c++)
            {
                _filters[c, bandIndex].SetPeakingEq(WaveFormat.SampleRate, _frequencies[bandIndex], 1.0f, gainDb);
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            int channels = WaveFormat.Channels;

            for (int i = 0; i < samplesRead; i++)
            {
                int ch = i % channels;
                float sample = buffer[offset + i];

                for (int b = 0; b < _frequencies.Length; b++)
                {
                    if (_gains[b] != 0)
                        sample = _filters[ch, b].Transform(sample);
                }
                buffer[offset + i] = sample;
            }
            return samplesRead;
        }
    }

    public class AudioEngine : IDisposable
    {
        public static AudioEngine Instance { get; } = new AudioEngine();

        private WaveOutEvent? outputDevice;
        private AudioFileReader? audioFile;
        private AudioAnalyzer? analyzer;
        private EqualizerProvider? equalizer;
        private float[] currentEqGains = new float[10];

        public bool IsPlaying => outputDevice?.PlaybackState == PlaybackState.Playing;
        public TimeSpan CurrentTime => audioFile?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan TotalTime => audioFile?.TotalTime ?? TimeSpan.Zero;
        public event EventHandler? TrackEnded;

        private AudioEngine() { }

        public void Play(string path, float volume)
        {
            try
            {
                Stop();
                Thread.Sleep(20);

                audioFile = new AudioFileReader(path);
                analyzer = new AudioAnalyzer(audioFile);
                equalizer = new EqualizerProvider(analyzer);
                for (int i = 0; i < 10; i++) equalizer.UpdateGain(i, currentEqGains[i]);

                outputDevice = new WaveOutEvent();
                outputDevice.PlaybackStopped += OnPlaybackStopped;
                outputDevice.Init(equalizer);
                outputDevice.Volume = volume;
                outputDevice.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ses cihazı başlatılamadı: \n{ex.Message}", "Donanım Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                Stop();
            }
        }

        public void SetEqGain(int bandIndex, float gainDb)
        {
            if (bandIndex >= 0 && bandIndex < 10)
            {
                currentEqGains[bandIndex] = gainDb;
                equalizer?.UpdateGain(bandIndex, gainDb);
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null) return;
            try
            {
                if (audioFile == null) return;
                if (audioFile.Length - audioFile.Position < audioFile.WaveFormat.AverageBytesPerSecond)
                    TrackEnded?.Invoke(this, EventArgs.Empty);
            }
            catch (ObjectDisposedException) { }
        }

        public void Pause() => outputDevice?.Pause();
        public void Resume() => outputDevice?.Play();
        public void Stop()
        {
            if (outputDevice != null)
            {
                outputDevice.PlaybackStopped -= OnPlaybackStopped;
                outputDevice.Stop();
                outputDevice.Dispose();
                outputDevice = null;
            }
            audioFile?.Dispose();
            audioFile = null;
            analyzer = null;
            equalizer = null;
        }

        public void SetVolume(float v)
        {
            if (outputDevice != null) outputDevice.Volume = v;
        }

        public void Seek(TimeSpan t)
        {
            if (audioFile != null) audioFile.CurrentTime = t;
        }

        public float[] GetFftData() => analyzer?.GetSafeFftResults() ?? Array.Empty<float>();

        public void Dispose() => Stop();
    }

    public class AudioAnalyzer : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _fftLength = 1024;
        private int _fftPos = 0;
        private Complex[] _fftBuffer;
        private float[] _fftResults;
        private readonly object _fftLock = new object();

        public WaveFormat WaveFormat => _source.WaveFormat;

        public AudioAnalyzer(ISampleProvider source)
        {
            _source = source;
            _fftBuffer = new Complex[_fftLength];
            _fftResults = new float[_fftLength / 2];
        }

        public float[] GetSafeFftResults()
        {
            lock (_fftLock)
            {
                return (float[])_fftResults.Clone();
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            int channels = _source.WaveFormat.Channels;

            for (int i = 0; i < samplesRead; i += channels)
            {
                float mono = 0f;
                for (int c = 0; c < channels; c++)
                    if (i + c < samplesRead) mono += buffer[offset + i + c];
                mono /= channels;

                double window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * _fftPos / (_fftLength - 1));
                _fftBuffer[_fftPos].X = (float)(mono * window);
                _fftBuffer[_fftPos].Y = 0f;
                _fftPos++;

                if (_fftPos >= _fftLength)
                {
                    FastFourierTransform.FFT(true, (int)Math.Log(_fftLength, 2.0), _fftBuffer);
                    lock (_fftLock)
                    {
                        for (int j = 0; j < _fftResults.Length; j++)
                            _fftResults[j] = (float)Math.Sqrt(_fftBuffer[j].X * _fftBuffer[j].X + _fftBuffer[j].Y * _fftBuffer[j].Y);
                    }
                    _fftPos = 0;
                }
            }
            return samplesRead;
        }
    }
    #endregion

    #region [ 5. LYRIC ENGINE & WINDOWS API ]
    public class LyricEngine
    {
        public static LyricEngine Instance { get; } = new LyricEngine();
        private List<KeyValuePair<TimeSpan, string>> _syncLyrics = new();
        private string _staticLyrics = "";

        private LyricEngine() { }

        public void LoadLyrics(string filePath)
        {
            _syncLyrics.Clear();
            _staticLyrics = "";
            try
            {
                string lrc = System.IO.Path.ChangeExtension(filePath, ".lrc");
                if (System.IO.File.Exists(lrc))
                {
                    string[] formats = { @"mm\:ss\.ff", @"m\:ss\.ff", @"mm\:ss\.fff", @"m\:ss\.fff", @"mm\:ss" };
                    foreach (var line in System.IO.File.ReadAllLines(lrc))
                    {
                        if (!line.StartsWith("[") || !line.Contains("]")) continue;
                        int end = line.IndexOf("]");
                        string ts = line.Substring(1, end - 1);
                        string txt = line.Substring(end + 1).Trim();
                        if (TimeSpan.TryParseExact(ts, formats, null, System.Globalization.TimeSpanStyles.None, out TimeSpan t))
                            _syncLyrics.Add(new KeyValuePair<TimeSpan, string>(t, txt));
                    }
                }
                else
                {
                    using var tf = TagLib.File.Create(filePath);
                    if (!string.IsNullOrWhiteSpace(tf.Tag.Lyrics)) _staticLyrics = tf.Tag.Lyrics;
                }
            }
            catch { }
        }

        public string GetCurrentLyric(TimeSpan t)
        {
            if (_syncLyrics.Count > 0)
            {
                string cur = "♪ ... ♪";
                // YENİ EKLENEN: LİRİK SENKRONİZASYONUNA 200ms AVANS (OFFSET)
                TimeSpan offsetTime = t.Add(TimeSpan.FromMilliseconds(200));
                foreach (var kv in _syncLyrics)
                {
                    if (offsetTime >= kv.Key) cur = string.IsNullOrWhiteSpace(kv.Value) ? "♪" : kv.Value;
                    else break;
                }
                return cur;
            }
            return string.IsNullOrWhiteSpace(_staticLyrics) ? "♪ Lirik Bulunamadı ♪" : _staticLyrics;
        }
    }

    public static class WindowsServices
    {
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        public static void EnableAcrylicBlur(Window window)
        {
            var wh = new WindowInteropHelper(window);
            var accent = new AccentPolicy { AccentState = 4, GradientColor = (0x01 << 24) | 0x40200D };
            int sz = Marshal.SizeOf(accent);
            var ptr = Marshal.AllocHGlobal(sz);
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = 19, SizeOfData = sz, Data = ptr };
            SetWindowCompositionAttribute(wh.Handle, ref data);
            Marshal.FreeHGlobal(ptr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data; public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags; public int GradientColor; public int AnimationId;
    }
    #endregion
}