using System.Collections.Generic;
using System.Linq;
using iFunds.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace iFunds.Controls;

/// <summary>横向流式铺排的热力图：从左到右一格一格排，按可用宽度自动换行。</summary>
public sealed class HeatmapGrid : Canvas
{
    private const double Gap = 3;
    private const double Cell = 14;

    public HeatmapGrid()
    {
        SizeChanged += (_, _) => Rebuild();
    }

    public static readonly DependencyProperty CellsProperty =
        DependencyProperty.Register(nameof(Cells), typeof(IReadOnlyList<HeatCell>),
            typeof(HeatmapGrid), new PropertyMetadata(null, OnCellsChanged));

    public IReadOnlyList<HeatCell>? Cells
    {
        get => (IReadOnlyList<HeatCell>?)GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    private static void OnCellsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HeatmapGrid)d).Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        var cells = Cells?.Where(c => !c.IsEmpty).ToList();
        if (cells is null || cells.Count == 0)
        {
            Height = 0;
            return;
        }

        double avail = ActualWidth;
        if (avail <= 0) avail = 600; // 首次布局兜底

        int perRow = (int)((avail + Gap) / (Cell + Gap));
        if (perRow < 1) perRow = 1;

        for (int i = 0; i < cells.Count; i++)
        {
            int row = i / perRow;
            int col = i % perRow;
            var r = new Rectangle
            {
                Width = Cell,
                Height = Cell,
                RadiusX = 2,
                RadiusY = 2,
                Fill = cells[i].Fill,
            };
            ToolTipService.SetToolTip(r, cells[i].Tooltip);
            SetLeft(r, col * (Cell + Gap));
            SetTop(r, row * (Cell + Gap));
            Children.Add(r);
        }

        int rows = (cells.Count + perRow - 1) / perRow;
        Height = rows * (Cell + Gap);
    }
}
