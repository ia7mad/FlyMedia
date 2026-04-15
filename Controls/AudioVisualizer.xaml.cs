using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MediaOverlay.Services;
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Rectangle = System.Windows.Shapes.Rectangle;
using Color = System.Windows.Media.Color;

namespace MediaOverlay.Controls;

public partial class AudioVisualizer : UserControl
{
    private Rectangle[] _bars;
    private const int BandCount = 32;

    public static readonly DependencyProperty BarColorProperty =
        DependencyProperty.Register("BarColor", typeof(Brush), typeof(AudioVisualizer), new PropertyMetadata(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255))));

    public Brush BarColor
    {
        get { return (Brush)GetValue(BarColorProperty); }
        set { SetValue(BarColorProperty, value); }
    }

    public AudioVisualizer()
    {
        InitializeComponent();
        _bars = new Rectangle[BandCount];
        
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildBars();
        AudioVisualizerService.Instance.Start();
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        // Don't stop the global instance necessarily, another view might be using it
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        BuildBars();
    }

    private void BuildBars()
    {
        MainCanvas.Children.Clear();
        
        double width = ActualWidth;
        double height = ActualHeight;
        if (width == 0 || height == 0) return;

        double gap = Math.Max(1, width / BandCount * 0.2);
        double barWidth = (width - gap * (BandCount - 1)) / BandCount;
        if (barWidth < 1) barWidth = 1;

        for (int i = 0; i < BandCount; i++)
        {
            var rect = new Rectangle
            {
                Width = barWidth,
                Height = 0,
                Fill = BarColor,
                RadiusX = barWidth / 2,
                RadiusY = barWidth / 2
            };
            Canvas.SetLeft(rect, i * (barWidth + gap));
            Canvas.SetTop(rect, height);
            MainCanvas.Children.Add(rect);
            _bars[i] = rect;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (ActualHeight == 0) return;

        var data = AudioVisualizerService.Instance.SpectrumData;
        double maxHeight = ActualHeight;

        for (int i = 0; i < BandCount; i++)
        {
            if (i >= data.Length) break;
            
            double value = data[i];
            // Add a minimum height so they don't completely disappear
            double targetHeight = Math.Max(2, value * maxHeight);
            
            if (_bars[i] != null)
            {
                _bars[i].Height = targetHeight;
                Canvas.SetTop(_bars[i], maxHeight - targetHeight);
            }
        }
    }
}
