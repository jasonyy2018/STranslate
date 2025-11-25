using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STranslate.Core;
using STranslate.Plugin;
using System.Collections.ObjectModel;

namespace STranslate.ViewModels.Pages;

public partial class HistoryViewModel : ObservableObject
{
    private const int PageSize = 20;
    private const int searchDelayMilliseconds = 500;

    private readonly SqlService _sqlService;
    private readonly ISnackbar _snackbar;
    private readonly Internationalization _i18n;
    private readonly Timer _searchTimer;

    private CancellationTokenSource? _searchCts;
    private DateTime _lastCursorTime = DateTime.Now;
    private bool _isLoading = false;

    private bool CanLoadMore =>
        !_isLoading &&
        string.IsNullOrEmpty(SearchText) &&
        (TotalCount == 0 || HistoryItems.Count != TotalCount);

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    // TODO: 后续考虑使用 https://github.com/Cysharp/ObservableCollections 优化性能
    [ObservableProperty] public partial ObservableCollection<HistoryModel> HistoryItems { get; set; } = [];

    [ObservableProperty] public partial HistoryModel? SelectedItem { get; set; }

    [ObservableProperty] public partial long TotalCount { get; set; }

    public HistoryViewModel(
        SqlService sqlService,
        ISnackbar snackbar,
        Internationalization i18n)
    {
        _sqlService = sqlService;
        _snackbar = snackbar;
        _i18n = i18n;
        _searchTimer = new Timer(async _ => await SearchAsync(), null, Timeout.Infinite, Timeout.Infinite);

        _ = RefreshAsync();
    }

    // 搜索文本变化时修改定时器
    partial void OnSearchTextChanged(string value) => _searchTimer.Change(searchDelayMilliseconds, Timeout.Infinite);

    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();

        if (string.IsNullOrEmpty(SearchText))
        {
            await RefreshAsync();
            return;
        }

        var historyItems = await _sqlService.GetDataAsync(SearchText, _searchCts.Token);
        App.Current.Dispatcher.Invoke(() => HistoryItems.Clear());
        if (historyItems == null) return;

        foreach (var item in historyItems)
            App.Current.Dispatcher.Invoke(() => HistoryItems.Add(item));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        TotalCount = await _sqlService.GetCountAsync();

        App.Current.Dispatcher.Invoke(() => HistoryItems.Clear());
        _lastCursorTime = DateTime.Now;

        await LoadMoreAsync();
    }

    [RelayCommand]
    private void Delete(HistoryModel? historyModel)
    {
        if (historyModel == null)
        {
            _snackbar.ShowWarning($"未选中任何历史记录项");
            return;
        }

        _sqlService.DeleteData(historyModel);

        App.Current.Dispatcher.Invoke(() => HistoryItems.Remove(historyModel));
    }

    [RelayCommand]
    private void Copy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Utilities.SetText(text);
        _snackbar.ShowSuccess(_i18n.GetTranslation("CopySuccess"));
    }

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync()
    {
        try
        {
            _isLoading = true;

            var historyData = await _sqlService.GetDataCursorPagedAsync(PageSize, _lastCursorTime);
            if (!historyData.Any()) return;

            App.Current.Dispatcher.Invoke(() =>
            {
                // 更新游标
                _lastCursorTime = historyData.Last().Time;
                var uniqueHistoryItems = historyData.Where(h => !HistoryItems.Any(existing => existing.Id == h.Id));
                foreach (var item in uniqueHistoryItems)
                    HistoryItems.Add(item);
            });
        }
        finally
        {
            _isLoading = false;
        }
    }
}