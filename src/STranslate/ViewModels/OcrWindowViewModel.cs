using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using STranslate.Controls;
using STranslate.Core;
using STranslate.Instances;
using STranslate.Plugin;
using STranslate.Views;
using STranslate.Views.Pages;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace STranslate.ViewModels;

public partial class OcrWindowViewModel : ObservableObject, IDisposable
{
    #region Constructor & DI

    public OcrWindowViewModel(
        Settings settings,
        DataProvider dataProvider,
        MainWindowViewModel mainWindowViewModel,
        OcrInstance ocrInstance,
        TtsInstance ttsInstance,
        Internationalization i18n,
        ISnackbar snackbar)
    {
        Settings = settings;
        DataProvider = dataProvider;
        _mainWindowViewModel = mainWindowViewModel;
        _ocrInstance = ocrInstance;
        _ttsInstance = ttsInstance;
        _i18n = i18n;
        _snackbar = snackbar;

        OcrEngines = _ocrInstance.Services;
        SelectedOcrEngine = _ocrInstance.Services.FirstOrDefault(x => x.IsEnabled);

        // 订阅 OcrViewModel 中服务的 PropertyChanged 事件
        _ocrInstance.Services.CollectionChanged += OnServicesCollectionChanged;

        // 为现有服务订阅事件
        foreach (var service in _ocrInstance.Services)
        {
            service.PropertyChanged += OnOcrServicePropertyChanged;
        }

        Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    #endregion

    #region Properties

    public Settings Settings { get; }
    public DataProvider DataProvider { get; }

    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly OcrInstance _ocrInstance;
    private readonly TtsInstance _ttsInstance;
    private readonly Internationalization _i18n;
    private readonly ISnackbar _snackbar;

    private const double WidthMultiplier = 2;
    private const double WidthAdjustment = 12;

    [ObservableProperty]
    public partial bool IsExecuting { get; set; } = false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    public partial string Result { get; set; } = string.Empty;

    private OcrResult? _lastOcrResult;

    [ObservableProperty]
    public partial BitmapSource? DisplayImage { get; set; }

    private BitmapSource? _sourceImage;
    private BitmapSource? _annotatedImage;

    [ObservableProperty]
    public partial ObservableCollection<OcrWord> OcrWords { get; set; } = [];

    [ObservableProperty]
    public partial bool IsShowingFitToWindow { get; set; } = false;

    [ObservableProperty]
    public partial ObservableCollection<Service> OcrEngines { get; set; }

    [ObservableProperty]
    public partial Service? SelectedOcrEngine { get; set; } = null;

    #endregion

    #region Commands

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ExecuteAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        if (IsExecuting) return;

        IsExecuting = true;
        try
        {
            Clear();
            _sourceImage = Utilities.ToBitmapImage(bitmap);
            DisplayImage = _sourceImage;

            var ocrSvc = _ocrInstance.GetActiveSvc<IOcrPlugin>();
            if (ocrSvc == null)
                return;

            var data = Utilities.ToBytes(bitmap);
            _lastOcrResult = await ocrSvc.RecognizeAsync(new OcrRequest(data, Settings.OcrLanguage), cancellationToken);

            if (!_lastOcrResult.IsSuccess || string.IsNullOrEmpty(_lastOcrResult.Text))
                return;

            _annotatedImage = GenerateAnnotatedImage(_lastOcrResult, _sourceImage);
            PopulateOcrWords(_lastOcrResult);
            Result = _lastOcrResult.Text;

            DisplayImage = Settings.IsOcrShowingAnnotated ? _annotatedImage : _sourceImage;
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private async Task ReExecuteAsync()
    {
        if (_sourceImage == null || IsExecuting) return;
        using var bitmap = Utilities.ToBitmap(_sourceImage);
        await ExecuteCommand.ExecuteAsync(bitmap);
    }

    public bool CanTranslate() => !string.IsNullOrEmpty(Result);

    [RelayCommand(CanExecute = nameof(CanTranslate))]
    private void Translate()
    {
        _mainWindowViewModel.ExecuteTranslate(Result);
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task PlayAudioAsync(string text, CancellationToken cancellationToken)
    {
        var ttsSvc = _ttsInstance.GetActiveSvc<ITtsPlugin>();
        if (ttsSvc == null)
            return;

        await ttsSvc.PlayAudioAsync(text, cancellationToken);
    }

    [RelayCommand]
    private void Copy(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
            _snackbar.ShowSuccess(_i18n.GetTranslation("CopySuccess"));
        }
        else
        {
            _snackbar.ShowWarning(_i18n.GetTranslation("NoCopyContent"));
        }
    }

    [RelayCommand]
    private void RemoveLineBreaks(TextBox textBox) =>
        Utilities.TransformText(textBox, t => t.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " "));

    [RelayCommand]
    private void RemoveSpaces(TextBox textBox) =>
        Utilities.TransformText(textBox, t => t.Replace(" ", string.Empty));

    [RelayCommand]
    private void CopyImageOrText(ImageZoom? imageZoom)
    {
        var text = imageZoom?.SelectedText;
        try
        {
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
                _snackbar.ShowSuccess(_i18n.GetTranslation("CopySuccess"));
                return;
            }

            if (_sourceImage != null)
            {
                Clipboard.SetImage(_sourceImage);
                _snackbar.ShowSuccess(_i18n.GetTranslation("CopyImageSuccess"));
                return;
            }

            _snackbar.ShowWarning(_i18n.GetTranslation("NoCopyContent"));
        }
        catch (Exception ex)
        {
            // 剪贴板操作在某些情况下可能失败（非 STA、其他应用占用等）
            _snackbar.ShowError($"{_i18n.GetTranslation("CopyFailed")}: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SelectAllText(ImageZoom? imageZoom) => imageZoom?.SelectAllText();

    [RelayCommand]
    private void SaveImage()
    {
        if (_sourceImage is null)
        {
            _snackbar.ShowWarning(_i18n.GetTranslation("NoImageToSave"));
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Title = _i18n.GetTranslation("SaveAs"),
            Filter = "PNG Files (*.png)|*.png|JPEG Files (*.jpg;*.jpeg)|*.jpg;*.jpeg|All Files (*.*)|*.*",
            FileName = $"{DateTime.Now:yyyyMMddHHmmssfff}",
            DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            AddToRecent = true
        };

        if (saveFileDialog.ShowDialog() != true)
            return;

        try
        {
            var encoder = CreateBitmapEncoder(saveFileDialog.FileName);
            encoder.Frames.Add(BitmapFrame.Create(_sourceImage));

            using var fs = new FileStream(saveFileDialog.FileName, FileMode.Create);
            encoder.Save(fs);

            _snackbar.ShowSuccess(_i18n.GetTranslation("SaveSuccess"));
        }
        catch (Exception ex)
        {
            _snackbar.ShowError($"{_i18n.GetTranslation("SaveFailed")}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenClipboardImageAsync()
    {
        var bitmapSource = Clipboard.GetImage();
        if (bitmapSource == null)
        {
            _snackbar.ShowWarning(_i18n.GetTranslation("NoImageInClipboard"));
            return;
        }

        using var bitmap = Utilities.ToBitmap(bitmapSource);
        await ExecuteCommand.ExecuteAsync(bitmap);
    }

    [RelayCommand]
    private async Task OpenImageFileAsync()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = _i18n.GetTranslation("ImportFromFile"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
            RestoreDirectory = true
        };

        if (openFileDialog.ShowDialog() != true) return;

        await using var fs = new FileStream(openFileDialog.FileName, FileMode.Open, FileAccess.Read);
        var bytes = new byte[fs.Length];
        _ = await fs.ReadAsync(bytes);

        using var bitmap = Utilities.ToBitmap(bytes);
        await ExecuteCommand.ExecuteAsync(bitmap);
    }

    [RelayCommand]
    private void ZoomOut(ImageZoom? element) => element?.ZoomOut();

    [RelayCommand]
    private void ZoomIn(ImageZoom? element) => element?.ZoomIn();

    [RelayCommand]
    private void FitToWindowSize(ImageZoom? element)
    {
        IsShowingFitToWindow = false;
        element?.Reset();
    }

    [RelayCommand]
    private void FitToActualSize(ImageZoom? element)
    {
        IsShowingFitToWindow = true;
        element?.ResetActualSize();
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        await _mainWindowViewModel.OpenSettingsCommand.ExecuteAsync(null);

        if (Keyboard.Modifiers == ModifierKeys.Control)
            Application.Current.Windows
                .OfType<SettingsWindow>()
                .First()
                .Navigate(nameof(OcrPage));
        else
            Application.Current.Windows
                .OfType<SettingsWindow>()
                .First()
                .Navigate(nameof(StandalonePage));
    }

    [RelayCommand]
    private void ToggleTextControl() => Settings.IsOcrShowingTextControl = !Settings.IsOcrShowingTextControl;

    [RelayCommand]
    private void Cancel(Window window)
    {
        CancelOperations();
        window.Close();
    }

    #endregion

    #region Event Handlers

    private void OnServicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (Service service in e.NewItems)
            {
                service.PropertyChanged += OnOcrServicePropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (Service service in e.OldItems)
            {
                service.PropertyChanged -= OnOcrServicePropertyChanged;
            }
        }
    }

    /// <summary>
    /// 监听 OcrViewModel 中服务的 IsEnabled 变化
    /// </summary>
    private void OnOcrServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Service.IsEnabled) && sender is Service service)
        {
            // 如果某个服务被启用，同步更新 SelectedOcrEngine
            if (service.IsEnabled)
            {
                SelectedOcrEngine = service;
            }
            // 如果当前选中的服务被禁用，清空选择
            else if (SelectedOcrEngine == service)
            {
                SelectedOcrEngine = null;
            }
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Settings.IsOcrShowingTextControl):
                Settings.OcrWindowWidth = Settings.IsOcrShowingTextControl
                        ? Settings.OcrWindowWidth * WidthMultiplier - WidthAdjustment
                        : (Settings.OcrWindowWidth + WidthAdjustment) / WidthMultiplier;
                break;
            case nameof(Settings.IsOcrShowingAnnotated):
                DisplayImage = Settings.IsOcrShowingAnnotated ? _annotatedImage : _sourceImage;
                break;
        }
    }

    /// <summary>
    /// 当用户在 UI 中切换 OCR 引擎时，同步更新服务的 IsEnabled 状态
    /// </summary>
    partial void OnSelectedOcrEngineChanged(Service? oldValue, Service? newValue)
    {
        // 禁用旧的引擎
        if (oldValue != null && oldValue.IsEnabled)
        {
            oldValue.IsEnabled = false;
        }

        // 启用新的引擎
        if (newValue != null && !newValue.IsEnabled)
        {
            newValue.IsEnabled = true;
        }
    }

    #endregion

    #region Private Methods

    private void Clear()
    {
        Result = string.Empty;
        _sourceImage = null;
        _annotatedImage = null;
        DisplayImage = null;
        _lastOcrResult = null;
        IsShowingFitToWindow = false;
        OcrWords.Clear();
    }

    private static BitmapEncoder CreateBitmapEncoder(string fileName)
    {
        return fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder();
    }

    #endregion

    #region Image Processing

    private void PopulateOcrWords(OcrResult ocrResult)
    {
        if (_sourceImage == null || ocrResult?.OcrContents == null)
            return;

        var ocrWords = new List<OcrWord>();

        foreach (var content in ocrResult.OcrContents)
        {
            if (string.IsNullOrEmpty(content.Text) ||
                content.BoxPoints == null ||
                content.BoxPoints.Count == 0)
                continue;

            var boundingBox = CalculateBoundingBox(content.BoxPoints);
            var charCount = content.Text.Length;
            var avgCharWidth = boundingBox.Width / Math.Max(charCount, 1);

            // 按字符拆分
            for (int i = 0; i < charCount; i++)
            {
                var charLeft = boundingBox.Left + avgCharWidth * i;
                var charBox = new Rect(charLeft, boundingBox.Top, avgCharWidth, boundingBox.Height);

                ocrWords.Add(new OcrWord
                {
                    Text = content.Text[i].ToString(),
                    BoundingBox = charBox
                });
            }
        }

        // 排序并构建全文索引
        var sortedWords = ocrWords
            .OrderBy(w => w.BoundingBox.Top)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        OcrWords.Clear();
        int currentIndex = 0;
        foreach (var word in sortedWords)
        {
            word.StartIndexInFullText = currentIndex;
            OcrWords.Add(word);
            currentIndex += word.Text.Length;
        }
    }

    private static Rect CalculateBoundingBox(List<BoxPoint> boxPoints)
    {
        var minX = boxPoints.Min(p => p.X);
        var minY = boxPoints.Min(p => p.Y);
        var maxX = boxPoints.Max(p => p.X);
        var maxY = boxPoints.Max(p => p.Y);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static BitmapSource GenerateAnnotatedImage(OcrResult ocrResult, BitmapSource? image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // 没有位置信息的话返回原图
        if (ocrResult?.OcrContents == null ||
            ocrResult.OcrContents.All(x => x.BoxPoints?.Count == 0))
        {
            return image;
        }

        var drawingVisual = new DrawingVisual();

        using (var drawingContext = drawingVisual.RenderOpen())
        {
            // 绘制原始图像
            drawingContext.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));

            // 创建并冻结画笔以提高性能
            var pen = new Pen(Brushes.Red, 2);
            pen.Freeze();

            // 绘制所有多边形
            foreach (var item in ocrResult.OcrContents)
            {
                if (item.BoxPoints == null || item.BoxPoints.Count == 0)
                    continue;

                var geometry = CreatePolygonGeometry(item.BoxPoints);
                drawingContext.DrawGeometry(null, pen, geometry);
            }
        }

        // 使用标准 96 DPI，Viewbox 会自动处理高 DPI 屏幕的缩放
        var renderBitmap = new RenderTargetBitmap(
            image.PixelWidth,
            image.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32
        );

        renderBitmap.Render(drawingVisual);
        renderBitmap.Freeze();

        return renderBitmap;
    }

    private static StreamGeometry CreatePolygonGeometry(List<BoxPoint> points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, points[0].Y), false, true);

            for (int i = 1; i < points.Count; i++)
            {
                ctx.LineTo(new Point(points[i].X, points[i].Y), true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    #endregion

    #region Cancel & IDisposable

    public void CancelOperations()
    {
        ExecuteCancelCommand.Execute(null);
        PlayAudioCancelCommand.Execute(null);
        Clear();
    }

    public void Dispose()
    {
        // 取消订阅事件，防止内存泄漏
        _ocrInstance.Services.CollectionChanged -= OnServicesCollectionChanged;
        foreach (var service in _ocrInstance.Services)
        {
            service.PropertyChanged -= OnOcrServicePropertyChanged;
        }
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    #endregion
}