using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using iFunds.Models;

namespace iFunds.Services;

public static class FundImportService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>生成带样例的 JSON 模板文本。</summary>
    public static string BuildTemplateJson()
    {
        var sample = new FundImportFile
        {
            Funds = new List<FundImportItem>
            {
                new()
                {
                    Code = "021277",
                    Name = "广发全球精选股票(QDII)",
                    Shares = 658.94m,
                    CostPrice = 3.9351m,
                    Alerts = new List<FundImportAlert>
                    {
                        new() { Direction = "rise", Threshold = 3m },
                        new() { Direction = "fall", Threshold = 3m },
                    }
                },
                new()
                {
                    Code = "008888",
                    Name = "华夏国证半导体芯片ETF联接C",
                    Shares = 0m,
                    CostPrice = 0m,
                    Alerts = new List<FundImportAlert>
                    {
                        new() { Direction = "fall", Threshold = 5m },
                    }
                },
            }
        };
        return JsonSerializer.Serialize(sample, Options);
    }

    /// <summary>把当前自选/持仓/提醒导出为与模板一致的 JSON（用于备份）。</summary>
    public static string BuildExportJson()
    {
        var state = AppState.Current;
        var file = new FundImportFile();

        foreach (var f in state.Funds)
        {
            var alerts = state.AlertRules
                .Where(r => r.FundCode == f.Code)
                .Select(r => new FundImportAlert
                {
                    Direction = r.Direction == AlertDirection.FallBelow ? "fall" : "rise",
                    Threshold = System.Math.Abs(r.Threshold),
                })
                .ToList();

            file.Funds.Add(new FundImportItem
            {
                Code = f.Code,
                Name = f.Name,
                Shares = f.Shares,
                CostPrice = f.CostPrice,
                Alerts = alerts.Count > 0 ? alerts : null,
            });
        }
        return JsonSerializer.Serialize(file, Options);
    }

    /// <summary>解析导入文本。</summary>
    public static FundImportFile? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<FundImportFile>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把导入项写入 AppState（自选 + 持仓 + 提醒）。返回成功导入数量。</summary>
    public static async Task<int> ApplyAsync(FundImportFile file)
    {
        var state = AppState.Current;
        int count = 0;

        foreach (var item in file.Funds)
        {
            var code = item.Code?.Trim();
            if (string.IsNullOrEmpty(code)) continue;

            Fund? fund;
            if (state.IsWatched(code))
            {
                fund = state.Funds.FirstOrDefault(f => f.Code == code);
            }
            else
            {
                await state.AddFundAsync(code);
                fund = state.Funds.FirstOrDefault(f => f.Code == code);
            }
            if (fund is null) continue;

            if (!string.IsNullOrWhiteSpace(item.Name))
                fund.Name = item.Name;
            if (item.Shares > 0) fund.Shares = item.Shares;
            if (item.CostPrice > 0) fund.CostPrice = item.CostPrice;
            fund.IsHolding = fund.Shares > 0;

            // 写入常驻持仓，刷新后不丢
            if (fund.Shares > 0)
                state.UpdateHolding(fund.Code, fund.Shares, fund.CostPrice, fund.Name);

            if (item.Alerts is not null)
            {
                foreach (var a in item.Alerts)
                {
                    var dir = a.Direction?.ToLower() == "fall"
                        ? AlertDirection.FallBelow
                        : AlertDirection.RiseAbove;
                    var threshold = dir == AlertDirection.FallBelow ? -System.Math.Abs(a.Threshold) : System.Math.Abs(a.Threshold);
                    state.AlertRules.Add(new AlertRule
                    {
                        FundCode = code,
                        Direction = dir,
                        Threshold = threshold,
                    });
                }
            }
            count++;
        }
        AppState.Current.SaveNow();
        return count;
    }
}
