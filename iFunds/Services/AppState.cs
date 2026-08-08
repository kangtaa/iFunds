using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using iFunds.Models;

namespace iFunds.Services;

/// <summary>全局共享状态：自选列表、提醒规则、设置、数据服务、全市场榜单。</summary>
public class AppState
{
    public static AppState Current { get; } = new();

    public IFundDataService DataService { get; } = new TiantianFundDataService();

    /// <summary>自选/持有基金（界面绑定的主集合，持有优先排序）</summary>
    public ObservableCollection<Fund> Funds { get; } = new();

    /// <summary>提醒规则</summary>
    public ObservableCollection<AlertRule> AlertRules { get; } = new();

    /// <summary>全局设置</summary>
    public AppSettings Settings { get; } = new();

    /// <summary>当前关注的基金代码（首次为空，由用户从基金页添加）</summary>
    public List<string> WatchCodes { get; } = new();

    private AppState()
    {
        // 设置变更 / 提醒增删 时自动持久化
        Settings.PropertyChanged += (_, _) => PersistenceService.Save();
        AlertRules.CollectionChanged += (_, _) => PersistenceService.Save();
    }

    /// <summary>供界面在修改持仓后调用，立即保存。</summary>
    public void SaveNow() => PersistenceService.Save();

    public bool IsWatched(string code) => WatchCodes.Contains(code);

    /// <summary>持仓信息（代码→份额/成本/名称），常驻，每次刷新都套用到新拉取的对象上。</summary>
    public Dictionary<string, PersistenceService.PersistFund> Holdings { get; } = new();

    /// <summary>兼容旧字段名：加载存档时把持仓灌进 Holdings。</summary>
    public Dictionary<string, PersistenceService.PersistFund>? PendingPersistedFunds
    {
        get => null;
        set
        {
            if (value is null) return;
            Holdings.Clear();
            foreach (var kv in value) Holdings[kv.Key] = kv.Value;
        }
    }

    /// <summary>把常驻持仓套用到一批基金对象（份额/成本/名称/持有标记）。</summary>
    private void ApplyHoldings(IEnumerable<Fund> funds)
    {
        foreach (var f in funds)
        {
            if (Holdings.TryGetValue(f.Code, out var p))
            {
                if (!string.IsNullOrWhiteSpace(p.Name) && (string.IsNullOrEmpty(f.Name) || f.Name.StartsWith("基金")))
                    f.Name = p.Name;
                f.Shares = p.Shares;
                f.CostPrice = p.CostPrice;
                f.IsHolding = p.Shares > 0;
            }
        }
    }

    /// <summary>界面编辑持仓后调用：更新常驻持仓并保存。</summary>
    public void UpdateHolding(string code, decimal shares, decimal cost, string name)
    {
        Holdings[code] = new PersistenceService.PersistFund
        {
            Code = code,
            Name = name,
            Shares = shares,
            CostPrice = cost,
            IsHolding = shares > 0,
        };
        SaveNow();
    }

    /// <summary>刷新所有关注基金，持有优先排序后写回 Funds。</summary>
    public async Task RefreshAsync()
    {
        var fresh = await DataService.RefreshAsync(WatchCodes);

        // 每次刷新都套用常驻持仓
        ApplyHoldings(fresh);

        var sorted = fresh
            .OrderByDescending(f => f.IsHolding)            // 持有优先
            .ThenByDescending(f => f.HoldingAmount)          // 持有内按市值
            .ToList();
        Funds.Clear();
        foreach (var f in sorted)
            Funds.Add(f);

        PersistenceService.Save();
    }

    /// <summary>添加一只基金到关注列表（持有的插到持有段尾，否则加到末尾）。</summary>
    public async Task<bool> AddFundAsync(string code)
    {
        code = code.Trim();
        if (string.IsNullOrEmpty(code) || WatchCodes.Contains(code))
            return false;

        var fund = await DataService.FetchFundAsync(code);
        if (fund is null) return false;

        WatchCodes.Add(code);
        Funds.Add(fund);
        Resort();
        PersistenceService.Save();
        return true;
    }

    public void RemoveFund(string code)
    {
        WatchCodes.Remove(code);
        Holdings.Remove(code);
        var f = Funds.FirstOrDefault(x => x.Code == code);
        if (f is not null) Funds.Remove(f);
        PersistenceService.Save();
    }

    private void Resort()
    {
        var sorted = Funds
            .OrderByDescending(f => f.IsHolding)
            .ThenByDescending(f => f.HoldingAmount)
            .ToList();
        Funds.Clear();
        foreach (var f in sorted) Funds.Add(f);
    }

    // ── 全市场榜单（真实排行，按分类缓存，失败回退内置常见基金） ──
    private readonly Dictionary<string, List<Fund>> _marketCache = new();

    public async Task<List<Fund>> GetMarketAsync(string category = "all")
    {
        if (_marketCache.TryGetValue(category, out var cached)) return cached;

        List<(string code, string name)> entries = new();
        if (DataService is TiantianFundDataService tt)
            entries = await tt.GetRankingAsync(30, category);

        if (entries.Count == 0 && category == "all")
        {
            var codes = new[]
            {
                "021277","025196","012734","022365","000307","018957","008888",
                "161725","005827","110011","260108","003096","001632","320007",
                "519674","011609","270042","002190","163406","000478"
            };
            entries = codes.Select(c => (c, "")).ToList();
        }

        // 并行拉取每只估值（原先串行 30 次，很慢）；榜单不需要走势，跳过历史请求
        var ttSvc = DataService as TiantianFundDataService;
        var tasks = entries.Select(async entry =>
        {
            var f = ttSvc is not null
                ? await ttSvc.FetchFundAsync(entry.code, false)
                : await DataService.FetchFundAsync(entry.code);
            if (f is null) return null;
            if (!string.IsNullOrEmpty(entry.name) && (string.IsNullOrEmpty(f.Name) || f.Name.StartsWith("基金")))
                f.Name = entry.name;
            return f;
        });
        var fetched = await Task.WhenAll(tasks);
        var market = fetched.Where(f => f is not null).Select(f => f!).ToList();
        market = market.OrderByDescending(f => f.GrowthRate).ToList();
        _marketCache[category] = market;
        return market;
    }

    /// <summary>按关键词搜索基金（真实接口）。</summary>
    public async Task<List<Fund>> SearchMarketAsync(string keyword)
    {
        if (DataService is not TiantianFundDataService tt) return new();
        var hits = await tt.SearchAsync(keyword);
        var list = new List<Fund>();
        foreach (var (code, name) in hits.Take(20))
        {
            var f = await DataService.FetchFundAsync(code);
            if (f is null) continue;
            if (string.IsNullOrEmpty(f.Name) || f.Name.StartsWith("基金")) f.Name = name;
            f.Watched = IsWatched(code);
            list.Add(f);
        }
        return list;
    }

    // ── 收益热力图（真实历史：持仓按市值加权的每日收益） ──
    private List<(DateTime date, decimal ret)> _dailyReturns = new();

    private List<(DateTime date, decimal ret)> DailyReturns() => _dailyReturns;

    /// <summary>
    /// 拉取真实历史净值，算出"持仓组合"每日加权涨跌幅，缓存供热力图使用。
    /// 无持仓时回退用第一只自选基金。失败保持上次数据。
    /// </summary>
    public async Task LoadDailyReturnsAsync()
    {
        if (DataService is not TiantianFundDataService tt) return;
        try
        {
            // 选取标的：优先持仓，否则全部自选
            var targets = Funds.Where(f => f.IsHolding).ToList();
            if (targets.Count == 0) targets = Funds.ToList();
            if (targets.Count == 0) return;

            // 拉每只的历史净值（今年以来），转成每日涨跌幅
            var perFund = new List<(decimal weight, Dictionary<DateTime, decimal> ret)>();
            decimal totalWeight = 0;
            foreach (var f in targets)
            {
                var hist = await tt.GetHistoryAsync(f.Code, 250);
                if (hist.Count < 2) continue;
                var map = new Dictionary<DateTime, decimal>();
                for (int i = 1; i < hist.Count; i++)
                {
                    var prev = hist[i - 1].nav;
                    if (prev > 0)
                        map[hist[i].date.Date] = (hist[i].nav - prev) / prev * 100m;
                }
                decimal w = f.IsHolding && f.HoldingAmount > 0 ? f.HoldingAmount : 1m;
                totalWeight += w;
                perFund.Add((w, map));
            }
            if (perFund.Count == 0 || totalWeight <= 0) return;

            // 汇总：每个交易日按权重平均
            var allDates = perFund.SelectMany(p => p.ret.Keys).Distinct()
                .Where(d => d.Year == DateTime.Today.Year)
                .OrderBy(d => d).ToList();
            var result = new List<(DateTime, decimal)>();
            foreach (var d in allDates)
            {
                decimal sum = 0; decimal wsum = 0;
                foreach (var (w, map) in perFund)
                    if (map.TryGetValue(d, out var r)) { sum += w * r; wsum += w; }
                if (wsum > 0) result.Add((d, Math.Round(sum / wsum, 2)));
            }
            if (result.Count > 0) _dailyReturns = result;
        }
        catch { }
    }

    public enum HeatPeriod { Day, Week, Month, Year }

    /// <summary>
    /// 日视图：GitHub 风格日历网格（列=周，行=周一..周日），含非交易日空格。
    /// 周/月/年：按聚合顺序铺排（行优先），方便用统一网格渲染。
    /// </summary>
    public List<HeatCell> BuildHeatCells(HeatPeriod period)
    {
        var daily = DailyReturns();
        var seq = new List<HeatCell>();

        if (period == HeatPeriod.Day)
        {
            int i = 0;
            foreach (var (d, r) in daily)
            {
                seq.Add(new HeatCell { Date = d, ReturnPercent = r, Label = d.ToString("MM-dd"), Column = i++, Row = 0 });
            }
            return seq;
        }

        IEnumerable<(string label, decimal ret)> agg = period switch
        {
            HeatPeriod.Week => daily
                .GroupBy(x => System.Globalization.ISOWeek.GetWeekOfYear(x.date))
                .Select(g => ("第" + System.Globalization.ISOWeek.GetWeekOfYear(g.First().date) + "周", Math.Round(g.Sum(v => v.ret), 2))),
            HeatPeriod.Month => daily
                .GroupBy(x => x.date.Month)
                .Select(g => (g.Key + "月", Math.Round(g.Sum(v => v.ret), 2))),
            HeatPeriod.Year => daily
                .GroupBy(x => x.date.Year)
                .Select(g => (g.Key + "年", Math.Round(g.Sum(v => v.ret), 2))),
            _ => Enumerable.Empty<(string, decimal)>()
        };

        int idx = 0;
        foreach (var (label, ret) in agg)
            seq.Add(new HeatCell { Label = label, ReturnPercent = ret, Column = idx++, Row = 0 });
        return seq;
    }
}