using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace iFunds.Controls;

/// <summary>用 Trend 百分比序列绘制的迷你分时曲线，颜色随涨跌。</summary>
public sealed partial class Sparkline : UserControl
{
    public Sparkline()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty PointsSourceProperty =
        DependencyProperty.Register(nameof(PointsSource), typeof(IReadOnlyList<decimal>),
            typeof(Sparkline), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty IsUpProperty =
        DependencyProperty.Register(nameof(IsUp), typeof(bool),
            typeof(Sparkline), new PropertyMetadata(true, OnChanged));

    public IReadOnlyList<decimal>? PointsSource
    {
        get => (IReadOnlyList<decimal>?)GetValue(PointsSourceProperty);
        set => SetValue(PointsSourceProperty, value);
    }

    public bool IsUp
    {
        get => (bool)GetValue(IsUpProperty);
        set => SetValue(IsUpProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((Sparkline)d).Redraw();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        var data = PointsSource;
        Line.Stroke = new SolidColorBrush(IsUp
            ? Color.FromArgb(255, 0xF1, 0x6A, 0x6D)
            : Color.FromArgb(255, 0x3F, 0xC3, 0x85));

        double w = Root.ActualWidth, h = Root.ActualHeight;
        if (data is null || data.Count < 2 || w <= 0 || h <= 0)
        {
            Line.Points = new PointCollection();
            return;
        }

        decimal min = data.Min(), max = data.Max();
        decimal range = max - min;
        if (range == 0) range = 1;

        double pad = 3;
        var pts = new PointCollection();
        for (int i = 0; i < data.Count; i++)
        {
            double x = pad + (w - 2 * pad) * i / (data.Count - 1);
            double norm = (double)((data[i] - min) / range);
            double y = (h - pad) - (h - 2 * pad) * norm;
            pts.Add(new Point(x, y));
        }
        Line.Points = pts;
    }
}
