using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using STranslate.Core;
using STranslate.Helpers;
using STranslate.Services;
using STranslate.Plugin;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using Microsoft.Win32;

namespace STranslate.ViewModels.Pages;

public partial class PluginViewModel : ObservableObject
{
    private readonly PluginService _pluginService;
    private readonly Internationalization _i18n;

    public DataProvider DataProvider { get; }

    private readonly ISnackbar _snackbar;
    private readonly Settings _settings;
    private readonly CollectionViewSource _pluginCollectionView;
    public ICollectionView PluginCollectionView => _pluginCollectionView.View;

    [ObservableProperty] public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty] public partial ListSortDirection NameSortDirection { get; set; } = ListSortDirection.Ascending;
    [ObservableProperty] public partial ListSortDirection VersionSortDirection { get; set; } = ListSortDirection.Ascending;
    [ObservableProperty] public partial ListSortDirection AuthorSortDirection { get; set; } = ListSortDirection.Ascending;
    [ObservableProperty] public partial ListSortDirection TypeSortDirection { get; set; } = ListSortDirection.Ascending;

    /// <summary>
    /// 所有插件数量
    /// </summary>
    public int TotalPluginCount => _pluginService.PluginMetaDatas.Count;

    /// <summary>
    /// 翻译插件数量（包含翻译和词典插件）
    /// </summary>
    public int TranslatePluginCount => _pluginService.PluginMetaDatas
        .Where(x => typeof(ITranslatePlugin).IsAssignableFrom(x.PluginType) || typeof(IDictionaryPlugin).IsAssignableFrom(x.PluginType))
        .Count();

    /// <summary>
    /// OCR插件数量
    /// </summary>
    public int OcrPluginCount => _pluginService.PluginMetaDatas
        .Where(x => typeof(IOcrPlugin).IsAssignableFrom(x.PluginType))
        .Count();

    /// <summary>
    /// TTS插件数量
    /// </summary>
    public int TtsPluginCount => _pluginService.PluginMetaDatas
        .Where(x => typeof(ITtsPlugin).IsAssignableFrom(x.PluginType))
        .Count();

    /// <summary>
    /// 词汇表插件数量
    /// </summary>
    public int VocabularyPluginCount => _pluginService.PluginMetaDatas
        .Where(x => typeof(IVocabularyPlugin).IsAssignableFrom(x.PluginType))
        .Count();

    public PluginViewModel(
        PluginService pluginService,
        Internationalization i18n,
        DataProvider dataProvider,
        ISnackbar snackbar,
        Settings settings
        )
    {
        _pluginService = pluginService;
        _i18n = i18n;
        DataProvider = dataProvider;
        _snackbar = snackbar;
        _settings = settings;

        _pluginCollectionView = new()
        {
            Source = _pluginService.PluginMetaDatas
        };
        _pluginCollectionView.Filter += OnPluginFilter;

        // 监听插件集合变化，更新计数
        _pluginService.PluginMetaDatas.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(TotalPluginCount));
            OnPropertyChanged(nameof(TranslatePluginCount));
            OnPropertyChanged(nameof(OcrPluginCount));
            OnPropertyChanged(nameof(TtsPluginCount));
            OnPropertyChanged(nameof(VocabularyPluginCount));
        };
    }

    [ObservableProperty]
    public partial PluginType PluginType { get; set; } = PluginType.All;

    private void OnPluginFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is not PluginMetaData plugin)
        {
            e.Accepted = false;
            return;
        }

        // 类型筛选
        var typeMatch = PluginType switch
        {
            PluginType.Translate => typeof(ITranslatePlugin).IsAssignableFrom(plugin.PluginType) || typeof(IDictionaryPlugin).IsAssignableFrom(plugin.PluginType),
            PluginType.Ocr => typeof(IOcrPlugin).IsAssignableFrom(plugin.PluginType),
            PluginType.Tts => typeof(ITtsPlugin).IsAssignableFrom(plugin.PluginType),
            PluginType.Vocabulary => typeof(IVocabularyPlugin).IsAssignableFrom(plugin.PluginType),
            _ => true,
        };

        // 文本筛选
        var textMatch = string.IsNullOrEmpty(FilterText)
            || plugin.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || plugin.Author.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || plugin.Description.Contains(FilterText, StringComparison.OrdinalIgnoreCase);

        e.Accepted = typeMatch && textMatch;
    }

    private void ApplySort(string propertyName, ListSortDirection direction)
    {
        _pluginCollectionView.SortDescriptions.Clear();
        _pluginCollectionView.SortDescriptions.Add(new SortDescription(propertyName, direction));
    }

    private void ApplySortWithCustomComparer(ListSortDirection direction, IComparer comparer)
    {
        _pluginCollectionView.SortDescriptions.Clear();

        if (_pluginCollectionView.View is ListCollectionView listView)
        {
            if (direction == ListSortDirection.Descending)
            {
                listView.CustomSort = new ReverseComparer(comparer);
            }
            else
            {
                listView.CustomSort = comparer;
            }
        }
    }

    [RelayCommand]
    private void SortByName()
    {
        NameSortDirection = NameSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        ApplySort(nameof(PluginMetaData.Name), NameSortDirection);
    }

    [RelayCommand]
    private void SortByVersion()
    {
        VersionSortDirection = VersionSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        ApplySortWithCustomComparer(VersionSortDirection, new VersionComparer());
    }

    [RelayCommand]
    private void SortByAuthor()
    {
        AuthorSortDirection = AuthorSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        ApplySort(nameof(PluginMetaData.Author), AuthorSortDirection);
    }

    [RelayCommand]
    private void SortByType()
    {
        TypeSortDirection = TypeSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        ApplySortWithCustomComparer(TypeSortDirection, new PluginTypeComparer());
    }

    partial void OnPluginTypeChanged(PluginType value) => _pluginCollectionView.View?.Refresh();

    partial void OnFilterTextChanged(string value) => _pluginCollectionView.View?.Refresh();

    [RelayCommand]
    private async Task AddPluginAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = _i18n.GetTranslation("SelectPluginFile"),
            Filter = "Spkg File (*.spkg)|*.spkg",
            Multiselect = true,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var needRestart = false;
        foreach (var spkgPluginFilePath in dialog.FileNames)
        {
            var installResult = _pluginService.InstallPlugin(spkgPluginFilePath);

            if (installResult.RequiredUpgrade && installResult.ExistingPlugin != null)
            {
                // 插件已存在，询问是否升级
                var result = await new ContentDialog
                {
                    Title = _i18n.GetTranslation("PluginUpgrade"),
                    Content = string.Format(_i18n.GetTranslation("PluginUpgradeConfirm"), installResult.ExistingPlugin.Name, installResult.ExistingPlugin.Version, installResult.NewPlugin?.Version),
                    PrimaryButtonText = _i18n.GetTranslation("Confirm"),
                    CloseButtonText = _i18n.GetTranslation("Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                }.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // 执行升级
                    if (_pluginService.UpgradePlugin(installResult.ExistingPlugin, spkgPluginFilePath))
                    {
                        needRestart = true;
                        _snackbar.ShowSuccess(_i18n.GetTranslation("PluginInstallSuccess"));
                    }
                    else
                    {
                        _snackbar.ShowError(_i18n.GetTranslation("PluginUpgradeFailed"));
                    }
                }
            }
            else if (!installResult.Succeeded)
            {
                await new ContentDialog
                {
                    Title = _i18n.GetTranslation("PluginInstallFailed"),
                    CloseButtonText = _i18n.GetTranslation("Ok"),
                    DefaultButton = ContentDialogButton.Close,
                    Content = installResult.Message
                }.ShowAsync();
            }
            else
            {
                _snackbar.ShowSuccess(_i18n.GetTranslation("PluginInstallSuccess"));
            }
        }

        if (needRestart)
        {
            var restartResult = await new ContentDialog
            {
                Title = _i18n.GetTranslation("Prompt"),
                Content = _i18n.GetTranslation("PluginUpgradeSuccess"),
                PrimaryButtonText = _i18n.GetTranslation("Confirm"),
                CloseButtonText = _i18n.GetTranslation("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            }.ShowAsync();

            if (restartResult == ContentDialogResult.Primary)
            {
                UACHelper.Run(_settings.StartMode);
                App.Current.Shutdown();
            }
        }
    }

    [RelayCommand]
    private void OpenPluginDirectory(PluginMetaData plugin)
    {
        var directory = plugin.PluginDirectory;
        if (!string.IsNullOrEmpty(directory))
            Process.Start("explorer.exe", directory);
    }

    [RelayCommand]
    private async Task DeletePluginAsync(PluginMetaData plugin)
    {
        if (await new ContentDialog
        {
            Title = _i18n.GetTranslation("Prompt"),
            CloseButtonText = _i18n.GetTranslation("Cancel"),
            PrimaryButtonText = _i18n.GetTranslation("Confirm"),
            DefaultButton = ContentDialogButton.Primary,
            Content = string.Format(_i18n.GetTranslation("PluginDeleteConfirm"), plugin.Author, plugin.Version, plugin.Name),
        }.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!_pluginService.UninstallPlugin(plugin))
        {
            _ = new ContentDialog
            {
                Title = _i18n.GetTranslation("Prompt"),
                CloseButtonText = _i18n.GetTranslation("Ok"),
                DefaultButton = ContentDialogButton.Close,
                Content = _i18n.GetTranslation("PluginDeleteFailed")
            }.ShowAsync().ConfigureAwait(false);

            return;
        }

        if (await new ContentDialog
        {
            Title = _i18n.GetTranslation("Prompt"),
            CloseButtonText = _i18n.GetTranslation("Cancel"),
            PrimaryButtonText = _i18n.GetTranslation("Confirm"),
            DefaultButton = ContentDialogButton.Primary,
            Content = _i18n.GetTranslation("PluginDeleteForRestart"),
        }.ShowAsync() == ContentDialogResult.Primary)
        {
            UACHelper.Run(_settings.StartMode);
            App.Current.Shutdown();
        }
    }

    [RelayCommand]
    private void OpenOfficialLink(string url)
        => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}

/// <summary>
/// 插件类型比较器，用于按插件类型排序
/// 排序顺序：翻译类 -> OCR -> TTS -> 词汇表 -> 其他
/// </summary>
public class PluginTypeComparer : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (x is not PluginMetaData pluginX || y is not PluginMetaData pluginY)
            return 0;

        var typeX = GetPluginTypePriority(pluginX.PluginType);
        var typeY = GetPluginTypePriority(pluginY.PluginType);

        int result = typeX.CompareTo(typeY);

        // 如果类型相同，则按名称排序
        if (result == 0)
        {
            result = string.Compare(pluginX.Name, pluginY.Name, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// 获取插件类型的排序优先级
    /// </summary>
    /// <param name="pluginType">插件类型</param>
    /// <returns>优先级数字，越小优先级越高</returns>
    private static int GetPluginTypePriority(Type? pluginType)
    {
        // 检查是否为翻译类插件（翻译或词典）
        if (typeof(ITranslatePlugin).IsAssignableFrom(pluginType) ||
            typeof(IDictionaryPlugin).IsAssignableFrom(pluginType))
        {
            return 1;
        }

        // OCR 插件
        if (typeof(IOcrPlugin).IsAssignableFrom(pluginType))
        {
            return 2;
        }

        // TTS 插件
        if (typeof(ITtsPlugin).IsAssignableFrom(pluginType))
        {
            return 3;
        }

        // 词汇表插件
        if (typeof(IVocabularyPlugin).IsAssignableFrom(pluginType))
        {
            return 4;
        }

        // 其他类型插件
        return 5;
    }
}

/// <summary>
/// 版本号比较器，支持语义化版本排序
///     * 1.0.0
///     * 2.1.3.4
///     * 1.0.0-beta
///     * 1.2
///     * v1.0.0（会自动清理非数字字符）
/// </summary>
public class VersionComparer : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (x is not PluginMetaData pluginX || y is not PluginMetaData pluginY)
            return 0;

        return CompareVersions(pluginX.Version, pluginY.Version);
    }

    private static int CompareVersions(string version1, string version2)
    {
        if (string.IsNullOrEmpty(version1) && string.IsNullOrEmpty(version2)) return 0;
        if (string.IsNullOrEmpty(version1)) return -1;
        if (string.IsNullOrEmpty(version2)) return 1;

        // 尝试解析为 System.Version
        if (Version.TryParse(version1, out var v1) && Version.TryParse(version2, out var v2))
        {
            return v1.CompareTo(v2);
        }

        // 手动解析版本号（支持更灵活的格式）
        var parts1 = ParseVersionParts(version1);
        var parts2 = ParseVersionParts(version2);

        int maxLength = Math.Max(parts1.Length, parts2.Length);
        for (int i = 0; i < maxLength; i++)
        {
            int part1 = i < parts1.Length ? parts1[i] : 0;
            int part2 = i < parts2.Length ? parts2[i] : 0;

            int result = part1.CompareTo(part2);
            if (result != 0) return result;
        }

        return 0;
    }

    private static int[] ParseVersionParts(string version)
    {
        // 移除非数字字符，只保留数字和点
        var cleanVersion = new string(version.Where(c => char.IsDigit(c) || c == '.').ToArray());

        return cleanVersion.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var num) ? num : 0)
            .ToArray();
    }
}

/// <summary>
/// 反向比较器包装器
/// </summary>
public class ReverseComparer : IComparer
{
    private readonly IComparer _innerComparer;

    public ReverseComparer(IComparer innerComparer)
    {
        _innerComparer = innerComparer;
    }

    public int Compare(object? x, object? y) => _innerComparer.Compare(y, x);
}

public enum PluginType
{
    All,
    Translate,
    Ocr,
    Tts,
    Vocabulary,
}