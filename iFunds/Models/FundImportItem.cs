using System.Collections.Generic;

namespace iFunds.Models;

/// <summary>导入文件中单只基金的结构。</summary>
public class FundImportItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Shares { get; set; }
    public decimal CostPrice { get; set; }

    /// <summary>涨跌幅提醒列表（可空）。每条：{ "direction": "rise"|"fall", "threshold": 3 }</summary>
    public List<FundImportAlert>? Alerts { get; set; }
}

public class FundImportAlert
{
    /// <summary>"rise"=涨幅达到，"fall"=跌幅达到</summary>
    public string Direction { get; set; } = "rise";

    /// <summary>阈值（百分比，正数）</summary>
    public decimal Threshold { get; set; }
}

/// <summary>导入文件根结构。</summary>
public class FundImportFile
{
    public List<FundImportItem> Funds { get; set; } = new();
}
