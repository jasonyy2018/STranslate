using CommunityToolkit.Mvvm.Input;
using STranslate.Core;
using STranslate.Helpers;
using STranslate.Plugin;

namespace STranslate.ViewModels.Pages;

public partial class GeneralViewModel : SearchViewModelBase
{
    public GeneralViewModel(
        Settings settings,
        DataProvider dataProvider,
        Internationalization i18n) : base(i18n, "General_")
    {
        Settings = settings;
        DataProvider = dataProvider;
        Languages = i18n.LoadAvailableLanguages();
    }

    [RelayCommand]
    private void ResetFontFamily() => Settings.AppFont = Win32Helper.GetSystemDefaultFont();

    [RelayCommand]
    private void ResetFontSize() => Settings.TextFontSize = 14;

    public List<int> ScreenNumbers
    {
        get
        {
            var screens = MonitorInfo.GetDisplayMonitors();
            var screenNumbers = new List<int>();
            for (int i = 1; i <= screens.Count; i++)
            {
                screenNumbers.Add(i);
            }

            return screenNumbers;
        }
    }
    public Settings Settings { get; }
    public DataProvider DataProvider { get; }

    public List<I18nPair> Languages { get; }
}