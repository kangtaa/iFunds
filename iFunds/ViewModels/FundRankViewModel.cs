using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using iFunds.Models;
using iFunds.Services;
using Microsoft.UI.Dispatching;

namespace iFunds.ViewModels;

public partial class FundRankViewModel : ObservableObject
{
    private readonly AppState _state = AppState.Current;
    private List<Fund> _all = new();
    private CancellationTokenSource? _searchCts;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public ObservableCollection<Fund> Items { get; } = new();

    [ObservableProperty] private string _keyword = string.Empty;
    [ObservableProperty] private bool _isSearching;

    private string _category = "all";

    public async Task LoadAsync(string category = "all")
    {
        _category = category;
        IsSearching = true;
        _all = await _state.GetMarketAsync(category);
        foreach (var f in _all)
            f.Watched = _state.IsWatched(f.Code);
        ApplyFilter();
        IsSearching = false;
    }

    partial void OnKeywordChanged(string value)
    {
        // 本地先过滤一次（即时响应）
        ApplyFilter();
        // 关键词较完整时，再走真实搜索（支持榜单之外的任意基金）
        _ = DebouncedSearchAsync(value?.Trim() ?? "");
    }

    private async Task DebouncedSearchAsync(string kw)
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        if (kw.Length < 2) return; // 太短不触发网络搜索

        try
        {
            await Task.Delay(350, cts.Token); // 防抖
            if (cts.Token.IsCancellationRequested) return;

            IsSearching = true;
            var results = await _state.SearchMarketAsync(kw);
            if (cts.Token.IsCancellationRequested) return;

            _dispatcher.TryEnqueue(() =>
            {
                // 合并：本地命中 + 远程搜索（去重，远程在后）
                var localHits = _all.Where(f => f.Code.Contains(kw) || f.Name.Contains(kw)).ToList();
                var merged = new List<Fund>(localHits);
                foreach (var r in results)
                    if (!merged.Any(x => x.Code == r.Code))
                        merged.Add(r);

                Items.Clear();
                foreach (var f in merged) Items.Add(f);
                IsSearching = false;
            });
        }
        catch (TaskCanceledException) { }
        catch { IsSearching = false; }
    }

    public void ApplyFilter()
    {
        var kw = Keyword?.Trim() ?? "";
        var q = string.IsNullOrEmpty(kw)
            ? _all
            : _all.Where(f => f.Code.Contains(kw) || f.Name.Contains(kw)).ToList();

        Items.Clear();
        foreach (var f in q)
            Items.Add(f);
    }
}
