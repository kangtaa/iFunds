using System;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace iFunds.Models;

/// <summary>收益热力图的一个格子。GitHub 风格下用 Column(第几列/周) + Row(周几) 定位。</summary>
public class HeatCell
{
    public DateTime Date { get; set; }
    public decimal ReturnPercent { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>列索引（GitHub 样式：第几周）</summary>
    public int Column { get; set; }
    /// <summary>行索引（GitHub 样式：0=周一 … 6=周日）</summary>
    public int Row { get; set; }
    /// <summary>是否为占位空格（非交易日/补齐用）</summary>
    public bool IsEmpty { get; set; }

    public string Tooltip => IsEmpty ? "" : $"{Label}  {(ReturnPercent >= 0 ? "+" : "")}{ReturnPercent:0.00}%";

    public Brush Fill
    {
        get
        {
            if (IsEmpty) return new SolidColorBrush(Colors.Transparent);
            double mag = Math.Min(1.0, (double)Math.Abs(ReturnPercent) / 3.0);
            byte Lerp(byte from, byte to) => (byte)(from + (to - from) * mag);
            if (Math.Abs(ReturnPercent) < 0.05m)
                return new SolidColorBrush(Color.FromArgb(255, 0x24, 0x26, 0x2E)); // 近零：暗灰
            if (ReturnPercent > 0)
                // 涨：从暗红到亮红
                return new SolidColorBrush(Color.FromArgb(255, Lerp(0x3A, 0xE5), Lerp(0x22, 0x48), Lerp(0x26, 0x4D)));
            // 跌：从暗绿到亮绿
            return new SolidColorBrush(Color.FromArgb(255, Lerp(0x1E, 0x2E), Lerp(0x30, 0xAD), Lerp(0x2A, 0x6F)));
        }
    }
}
