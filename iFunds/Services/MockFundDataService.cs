using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using iFunds.Models;

namespace iFunds.Services;

/// <summary>假数据服务：模拟涨跌、分时曲线、持仓成本，用于无网络时验证界面。</summary>
public class MockFundDataService : IFundDataService
{
    private readonly Random _random = new();

    private static readonly Dictionary<string, string> Names = new()
    {
        ["021277"] = "广发全球精选股票(QDII)",
        ["025196"] = "广发创业板指数增强C",
        ["012734"] = "易方达中证人工智能主题ETF",
        ["022365"] = "永赢科技智选混合发起C",
        ["000307"] = "易方达黄金ETF联接A",
        ["018957"] = "中航机遇领航混合发起C",
        ["008888"] = "华夏国证半导体芯片ETF联接C",
    };

    public Task<Fund?> FetchFundAsync(string code)
    {
        var name = Names.TryGetValue(code, out var n) ? n : $"基金{code}";
        var growth = Math.Round((decimal)(_random.NextDouble() * 8 - 4), 2);
        var net = Math.Round((decimal)(_random.NextDouble() * 6 + 1), 4);
        bool holding = code is "021277" or "025196" or "018957";
        var shares = holding ? Math.Round((decimal)(_random.NextDouble() * 800 + 200), 2) : 0m;
        var cost = holding ? Math.Round(net * (decimal)(0.6 + _random.NextDouble() * 0.5), 4) : 0m;

        var fund = new Fund
        {
            Code = code,
            Name = name,
            GrowthRate = growth,
            NetValue = net,
            NetValueDate = "06-03",
            EstimateValue = Math.Round(net * (1 + growth / 100m), 4),
            EstimateTime = "06-05 15:00",
            IsHolding = holding,
            Shares = shares,
            CostPrice = cost,
            NetValueUpdated = _random.NextDouble() > 0.5,
            Trend = GenerateTrend(growth),
        };
        return Task.FromResult<Fund?>(fund);
    }

    private List<decimal> GenerateTrend(decimal final)
    {
        var list = new List<decimal>();
        double cur = 0;
        for (int i = 0; i < 30; i++)
        {
            double target = (double)final * (i / 29.0);
            cur += (target - cur) * 0.4 + (_random.NextDouble() - 0.5) * 0.6;
            list.Add(Math.Round((decimal)cur, 2));
        }
        list[^1] = final;
        return list;
    }

    public async Task<IReadOnlyList<Fund>> RefreshAsync(IEnumerable<string> codes)
    {
        var list = new List<Fund>();
        foreach (var code in codes)
        {
            var f = await FetchFundAsync(code);
            if (f is not null) list.Add(f);
        }
        return list;
    }
}
