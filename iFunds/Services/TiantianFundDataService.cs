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
/// - 实时估值：fundgz.1234567.com.cn/js/{code}.js （JSONP）
/// - 历史净值：api.fund.eastmoney.com/f10/lsjz （需 Referer）
/// - 搜索：fundsuggest.eastmoney.com
/// 所有请求带超时与失败回退，单点失败不影响整体。
/// </summary>
public class TiantianFundDataService : IFundDataService
{
    private readonly HttpClient _http;

    public TiantianFundDataService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    /// <summary>取单只基金的实时估值。withTrend=false 时跳过历史走势请求（榜单批量用，更快）。</summary>
    public async Task<Fund?> FetchFundAsync(string code) => await FetchFundAsync(code, true);

    public async Task<Fund?> FetchFundAsync(string code, bool withTrend)
    {
        code = code.Trim();
        if (string.IsNullOrEmpty(code)) return null;

        try
        {
            // 实时估值 JSONP：jsonpgz({...});
            var url = $"https://fundgz.1234567.com.cn/js/{code}.js?rt={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var raw = await _http.GetStringAsync(url);
            var json = ExtractJsonp(raw);
            if (json is null) return BuildFallback(code);

            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            string name = GetStr(r, "name") ?? $"基金{code}";
            decimal dwjz = ParseDec(GetStr(r, "dwjz"));   // 上一交易日单位净值
            decimal gsz = ParseDec(GetStr(r, "gsz"));     // 估算净值
            decimal gszzl = ParseDec(GetStr(r, "gszzl")); // 估算涨跌幅 %
            string gztime = GetStr(r, "gztime") ?? "";    // 估值时间 yyyy-MM-dd HH:mm
            string jzrq = GetStr(r, "jzrq") ?? "";        // 净值日期

            var fund = new Fund
            {
                Code = code,
                Name = name,
                GrowthRate = gszzl,
                NetValue = dwjz > 0 ? dwjz : gsz,
                NetValueDate = FormatDate(jzrq),
                EstimateValue = gsz,
                EstimateTime = FormatTime(gztime),
                NetValueUpdated = false,
            };

            // 分时走势：仅在需要时拉历史（榜单批量跳过以提速）
            if (withTrend)
                fund.Trend = AppendIntradaySample(code, gszzl);
            return fund;
        }
        catch
        {
            return BuildFallback(code);
        }
    }

    public async Task<IReadOnlyList<Fund>> RefreshAsync(IEnumerable<string> codes)
    {
        var tasks = codes.Distinct().Select(FetchFundAsync);
        var results = await Task.WhenAll(tasks);
        return results.Where(f => f is not null).Select(f => f!).ToList();
    }

    // ── 历史净值 ──

    /// <summary>取最近 n 条历史净值（日期升序）。优先用 pingzhongdata（稳定、无防盗链），失败回退 lsjz。</summary>
    public async Task<List<(DateTime date, decimal nav)>> GetHistoryAsync(string code, int pageSize = 120)
    {
        // 方式一：pingzhongdata 静态文件，含 Data_netWorthTrend = [{x:ms,y:nav},...]
        var fromPzd = await GetHistoryFromPingzhongAsync(code, pageSize);
        if (fromPzd.Count >= 2) return fromPzd;

        // 方式二：lsjz 接口（带 Referer）
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

    /// <summary>从 pingzhongdata 静态文件解析历史净值（取末尾 take 条）。</summary>
    private async Task<List<(DateTime date, decimal nav)>> GetHistoryFromPingzhongAsync(string code, int take)
    {
        var list = new List<(DateTime, decimal)>();
        try
        {
            var url = $"https://fund.eastmoney.com/pingzhongdata/{code}.js?v={DateTime.Now:yyyyMMddHHmmss}";
            var body = await _http.GetStringAsync(url);

            // 提取 var Data_netWorthTrend = [ ... ];
            const string key = "Data_netWorthTrend";
            int ki = body.IndexOf(key);
            if (ki < 0) return list;
            int lb = body.IndexOf('[', ki);
            int rb = body.IndexOf(']', lb);
            if (lb < 0 || rb < 0) return list;
            var arrJson = body.Substring(lb, rb - lb + 1);

            using var doc = JsonDocument.Parse(arrJson);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                // {"x":1700000000000,"y":1.2345,"equityReturn":..,"unitMoney":""}
                if (item.TryGetProperty("x", out var xEl) && item.TryGetProperty("y", out var yEl))
                {
                    long ms = xEl.GetInt64();
                    var date = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.Date;
                    decimal nav = yEl.TryGetDecimal(out var dv) ? dv : 0m;
                    if (nav > 0) list.Add((date, nav));
                }
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            if (list.Count > take) list = list.Skip(list.Count - take).ToList();
        }
        catch { }
        return list;
    }

    // ── 当日分时采样 ──
    // 估值接口每次只返回"当前"一个点；把一天内多次刷新的估值涨跌幅按时间累积成曲线，
    // 表现为从当日首次采样画到当前，跨天自动重置。
    private readonly Dictionary<string, (DateTime day, List<decimal> points)> _intraday = new();

    private List<decimal> AppendIntradaySample(string code, decimal growth)
    {
        var today = DateTime.Today;
        lock (_intraday)
        {
            if (!_intraday.TryGetValue(code, out var entry) || entry.day != today)
            {
                entry = (today, new List<decimal>());
                _intraday[code] = entry;
            }
            if (entry.points.Count < 300)
                entry.points.Add(growth);
            if (entry.points.Count == 1)
                return new List<decimal> { 0m, growth };
            return new List<decimal>(entry.points);
        }
    }

    private async Task<List<decimal>> TryBuildTrendAsync(string code, decimal fallbackGrowth)
    {
        try
        {
            var hist = await GetHistoryAsync(code, 30);
            if (hist.Count >= 2)
            {
                // 用最近 30 日净值相对首日的累计涨跌%作为曲线
                var baseNav = hist[0].nav;
                if (baseNav > 0)
                    return hist.Select(h => Math.Round((h.nav - baseNav) / baseNav * 100m, 2)).ToList();
            }
        }
        catch { }
        // 回退：一条平滑到 fallbackGrowth 的占位线
        var line = new List<decimal>();
        for (int i = 0; i < 20; i++) line.Add(Math.Round(fallbackGrowth * i / 19m, 2));
        return line;
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
                    var code = item.GetProperty("CODE").GetString();
                    var name = item.GetProperty("NAME").GetString();
                    if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(name))
                        list.Add((code!, name!));
                    if (list.Count >= 30) break;
                }
            }
        }
        catch { }
        return list;
    }

    // ── 榜单/排行 ──

    /// <summary>取开放式基金日涨幅排行的前若干只（代码+名称）。ft：all/gp/hh/zq/zs/qdii/etf。失败返回空。</summary>
    public async Task<List<(string code, string name)>> GetRankingAsync(int top = 30, string ft = "all")
    {
        var list = new List<(string, string)>();
        try
        {
            // ft 类型；sc=rzdf 按日增长率；st=desc 降序
            var url = $"https://fund.eastmoney.com/data/rankhandler.aspx?op=ph&dt=kf&ft={ft}&rs=&gs=0&sc=rzdf&st=desc&pi=1&pn={top}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri("https://fund.eastmoney.com/data/fundranking.html");
            var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            // 返回形如 var rankData = {datas:["code,name,...","..."],...};
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

    // ── 辅助 ──

    private Fund BuildFallback(string code) => new()
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

    private static string? ExtractJsonp(string raw)
    {
        int s = raw.IndexOf('{');
        int e = raw.LastIndexOf('}');
        if (s < 0 || e < 0 || e <= s) return null;
        return raw.Substring(s, e - s + 1);
    }

    private static string? GetStr(JsonElement r, string name)
        => r.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static decimal ParseDec(string? s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static string FormatDate(string jzrq)
    {
        // jzrq 形如 2026-06-05 → 06-05
        if (DateTime.TryParse(jzrq, out var d)) return d.ToString("MM-dd");
        return jzrq;
    }

    private static string FormatTime(string gztime)
    {
        // gztime 形如 2026-06-05 15:00 → 06-05 15:00
        if (DateTime.TryParse(gztime, out var d)) return d.ToString("MM-dd HH:mm");
        return gztime;
    }
}
