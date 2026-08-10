using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using iFunds.Models;

namespace iFunds.Services;

/// <summary>
/// 天天基金/东方财富 真实数据。
/// - 基金详情 + 历史净值：fund.eastmoney.com/pingzhongdata/{code}.js
/// - 历史净值备选：api.fund.eastmoney.com/f10/lsjz（需 Referer）
/// - 排行榜：fund.eastmoney.com/data/rankhandler.aspx
/// - 搜索：fundsuggest.eastmoney.com
/// 所有请求带超时与失败回退，单点失败不影响整体。
/// </summary>
public class TiantianFundDataService : IFundDataService
{
    private readonly HttpClient _http;

    public TiantianFundDataService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    /// <summary>取单只基金信息。从 pingzhongdata 静态文件获取净值、名称、走势。</summary>
    public async Task<Fund?> FetchFundAsync(string code) => await FetchFundInternalAsync(code);

    public async Task<Fund?> FetchFundAsync(string code, bool _) => await FetchFundInternalAsync(code);

    private async Task<Fund?> FetchFundInternalAsync(string code)
    {
        code = code.Trim();
        if (string.IsNullOrEmpty(code)) return null;

        try
        {
            // 并行拉取双源：pingzhongdata（名称+走势）+ lsjz（最新净值，更新更快）
            var tPzd = FetchFromPingzhongAsync(code);
            var tLsjz = GetLsjzHistoryAsync(code, 3); // 只取最近 3 天，快

            await Task.WhenAll(tPzd, tLsjz);

            var fund = tPzd.Result;          // pingzhongdata：名称 + 全量走势
            var lsjzList = tLsjz.Result;     // lsjz：最新 3 天净值

            // 合并：lsjz 通常更新更快，取最新净值覆盖 pingzhongdata
            if (fund is not null && lsjzList.Count >= 2)
            {
                var lsjzLast = lsjzList[^1];
                // lsjz 日期比 pzd 更新 → 覆盖净值
                if (lsjzLast.Item1 > DateTime.Today.AddDays(-2))
                {
                    fund.NetValue = lsjzLast.Item2;
                    fund.NetValueDate = lsjzLast.Item1.ToString("MM-dd");
                    var prev = lsjzList[^2];
                    fund.PrevNetValue = prev.Item2;
                    if (prev.Item2 > 0)
                        fund.GrowthRate = Math.Round((lsjzLast.Item2 - prev.Item2) / prev.Item2 * 100m, 2);
                }
            }

            if (fund is not null) return fund;
            if (lsjzList.Count >= 2) return await FetchFromLsjzFallbackAsync(code);
        }
        catch { }

        return BuildFallback(code);
    }

    public async Task<IReadOnlyList<Fund>> RefreshAsync(IEnumerable<string> codes)
    {
        var tasks = codes.Distinct().Select(FetchFundInternalAsync);
        var results = await Task.WhenAll(tasks);
        return results.Where(f => f is not null).Select(f => f!).ToList();
    }

    // ── 主数据源：pingzhongdata ──

    /// <summary>从 pingzhongdata 解析基金信息。返回 null 表示失败需回退。</summary>
    private async Task<Fund?> FetchFromPingzhongAsync(string code)
    {
        var url = $"https://fund.eastmoney.com/pingzhongdata/{code}.js?v={DateTime.Now:yyyyMMddHHmmss}";
        var body = await _http.GetStringAsync(url);
        if (string.IsNullOrWhiteSpace(body)) return null;

        // 提取基金名称
        string name = ExtractJsVar(body, "fS_name") ?? $"基金{code}";

        // 提取净值走势 Data_netWorthTrend = [{x:ms,y:nav},...]
        var navList = ExtractNetWorthTrend(body);
        if (navList.Count == 0) return null;

        // 最近交易日净值
        var last = navList[^1];
        decimal netValue = last.Item2;
        string netValueDate = last.Item1.ToString("MM-dd");

        // 涨跌幅：最近两日净值变化
        decimal growthRate = 0m;
        decimal prevNetValue = netValue;
        if (navList.Count >= 2)
        {
            var prev = navList[^2];
            prevNetValue = prev.Item2;
            if (prev.Item2 > 0)
                growthRate = Math.Round((netValue - prev.Item2) / prev.Item2 * 100m, 2);
        }

        // 近30日走势序列（百分比）
        var trend = BuildTrendFromNav(navList);

        return new Fund
        {
            Code = code,
            Name = name,
            GrowthRate = growthRate,
            NetValue = netValue,
            PrevNetValue = prevNetValue,
            NetValueDate = netValueDate,
            EstimateValue = netValue,
            EstimateTime = netValueDate,
            Trend = trend,
            NetValueUpdated = false,
        };
    }

    /// <summary>回退：仅用 lsjz 获取净值（无名称）。</summary>
    private async Task<Fund?> FetchFromLsjzFallbackAsync(string code)
    {
        var list = await GetLsjzHistoryAsync(code, 30);
        if (list.Count == 0) return null;

        var last = list[^1];
        decimal growthRate = 0m;
        decimal prevNetValue = last.Item2;
        if (list.Count >= 2)
        {
            var prev = list[^2];
            prevNetValue = prev.Item2;
            if (prev.Item2 > 0) growthRate = Math.Round((last.Item2 - prev.Item2) / prev.Item2 * 100m, 2);
        }

        var trend = BuildTrendFromNav(list);

        return new Fund
        {
            Code = code,
            Name = $"基金{code}",
            GrowthRate = growthRate,
            NetValue = last.Item2,
            PrevNetValue = prevNetValue,
            NetValueDate = last.Item1.ToString("MM-dd"),
            EstimateValue = last.Item2,
            EstimateTime = last.Item1.ToString("MM-dd"),
            Trend = trend,
            NetValueUpdated = false,
        };
    }

    // ── 历史净值 ──

    /// <summary>取最近 n 条历史净值（日期升序）。优先 pingzhongdata，失败回退 lsjz。</summary>
    public async Task<List<(DateTime date, decimal nav)>> GetHistoryAsync(string code, int pageSize = 120)
    {
        // 方式一：pingzhongdata（更稳定，一次请求拿到全部历史）
        var list = await GetHistoryFromPingzhongAsync(code, pageSize);
        if (list.Count >= 2) return list;

        // 方式二：lsjz 接口
        return await GetLsjzHistoryAsync(code, pageSize);
    }

    private async Task<List<(DateTime date, decimal nav)>> GetHistoryFromPingzhongAsync(string code, int take)
    {
        var list = new List<(DateTime, decimal)>();
        try
        {
            var url = $"https://fund.eastmoney.com/pingzhongdata/{code}.js?v={DateTime.Now:yyyyMMddHHmmss}";
            var body = await _http.GetStringAsync(url);
            var raw = ExtractNetWorthTrend(body);
            list = raw;
            if (list.Count > take) list = list.Skip(list.Count - take).ToList();
        }
        catch { }
        return list;
    }

    private async Task<List<(DateTime date, decimal nav)>> GetLsjzHistoryAsync(string code, int pageSize = 120)
    {
        var list = new List<(DateTime, decimal)>();
        try
        {
            var url = $"https://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize={pageSize}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri($"https://fundf10.eastmoney.com/jjjz_{code}.html");
            var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var arr = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");
            foreach (var item in arr.EnumerateArray())
            {
                var ds = item.GetProperty("FSRQ").GetString();
                var ns = item.GetProperty("DWJZ").GetString();
                if (DateTime.TryParse(ds, out var d) && decimal.TryParse(ns, NumberStyles.Any, CultureInfo.InvariantCulture, out var nav))
                    list.Add((d, nav));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        }
        catch { }
        return list;
    }

    // ── 搜索 ──

    public async Task<List<(string code, string name)>> SearchAsync(string keyword)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(keyword)) return list;
        try
        {
            var url = $"https://fundsuggest.eastmoney.com/FundSearch/api/FundSearchAPI.ashx?m=1&key={Uri.EscapeDataString(keyword)}";
            var body = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("Datas", out var datas))
            {
                foreach (var item in datas.EnumerateArray())
                {
                    var c = item.GetProperty("CODE").GetString();
                    var n = item.GetProperty("NAME").GetString();
                    if (!string.IsNullOrEmpty(c) && !string.IsNullOrEmpty(n))
                        list.Add((c!, n!));
                    if (list.Count >= 30) break;
                }
            }
        }
        catch { }
        return list;
    }

    // ── 榜单/排行 ──

    public async Task<List<(string code, string name)>> GetRankingAsync(int top = 30, string ft = "all")
    {
        var list = new List<(string, string)>();
        try
        {
            var url = $"https://fund.eastmoney.com/data/rankhandler.aspx?op=ph&dt=kf&ft={ft}&rs=&gs=0&sc=rzdf&st=desc&pi=1&pn={top}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri("https://fund.eastmoney.com/data/fundranking.html");
            var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            int s = body.IndexOf('[');
            int e = body.IndexOf(']');
            if (s >= 0 && e > s)
            {
                var inner = body.Substring(s + 1, e - s - 1);
                foreach (var row in inner.Split("\",\""))
                {
                    var cells = row.Trim('"').Split(',');
                    if (cells.Length >= 2 && cells[0].Length == 6)
                        list.Add((cells[0], cells[1]));
                    if (list.Count >= top) break;
                }
            }
        }
        catch { }
        return list;
    }

    // ── 辅助：pingzhongdata 解析 ──

    /// <summary>从 JS 文本中提取 Data_netWorthTrend 数组。</summary>
    private static List<(DateTime date, decimal nav)> ExtractNetWorthTrend(string body)
    {
        var list = new List<(DateTime, decimal)>();
        const string key = "Data_netWorthTrend";
        int ki = body.IndexOf(key, StringComparison.Ordinal);
        if (ki < 0) return list;
        int lb = body.IndexOf('[', ki);
        int rb = body.IndexOf(']', lb);
        if (lb < 0 || rb < 0) return list;
        var arrJson = body.Substring(lb, rb - lb + 1);

        try
        {
            using var doc = JsonDocument.Parse(arrJson);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("x", out var xEl) && item.TryGetProperty("y", out var yEl))
                {
                    long ms = xEl.GetInt64();
                    var date = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.Date;
                    decimal nav = yEl.TryGetDecimal(out var dv) ? dv : 0m;
                    if (nav > 0) list.Add((date, nav));
                }
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        }
        catch { }
        return list;
    }

    /// <summary>从 JS 文本中提取 varName = "value" 这样的字符串变量。</summary>
    private static string? ExtractJsVar(string body, string varName)
    {
        var pat = $"var {varName}";
        int ki = body.IndexOf(pat, StringComparison.Ordinal);
        if (ki < 0) return null;
        int eq = body.IndexOf('=', ki + pat.Length);
        if (eq < 0) return null;
        int q1 = body.IndexOf('"', eq);
        if (q1 < 0) return null;
        int q2 = body.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return body.Substring(q1 + 1, q2 - q1 - 1);
    }

    /// <summary>从净值序列生成百分比走势（最近 30 个点）。</summary>
    private static List<decimal> BuildTrendFromNav(List<(DateTime date, decimal nav)> navList)
    {
        if (navList.Count < 2) return new List<decimal>();

        var recent = navList.Skip(navList.Count - 30).ToList();
        var baseNav = recent[0].Item2;
        if (baseNav <= 0) return new List<decimal>();

        return recent.Select(h => Math.Round((h.Item2 - baseNav) / baseNav * 100m, 2)).ToList();
    }

    // ── 兜底 ──

    private static Fund BuildFallback(string code) => new()
    {
        Code = code,
        Name = $"基金{code}",
        GrowthRate = 0,
        NetValue = 0,
        NetValueDate = "",
        EstimateValue = 0,
        EstimateTime = "--",
        Trend = new List<decimal>(),
    };
}
