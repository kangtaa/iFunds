using System;

namespace iFunds.Models;

public enum AlertDirection
{
    RiseAbove,
    FallBelow,
    NetValueUpdated
}

public class AlertRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FundCode { get; set; } = string.Empty;
    public AlertDirection Direction { get; set; }
    public decimal Threshold { get; set; }
    public bool Enabled { get; set; } = true;
    public bool TriggeredToday { get; set; }

    public bool IsHit(decimal growthRate, bool netUpdated)
        => Direction switch
        {
            AlertDirection.RiseAbove => growthRate >= Threshold,
            AlertDirection.FallBelow => growthRate <= Threshold,
            AlertDirection.NetValueUpdated => netUpdated,
            _ => false
        };

    public string DescriptionText => Direction switch
    {
        AlertDirection.RiseAbove => $"涨 ≥ +{Threshold:0.##}%",
        AlertDirection.FallBelow => $"跌 ≤ {Threshold:0.##}%",
        AlertDirection.NetValueUpdated => "净值更新",
        _ => string.Empty
    };
}
