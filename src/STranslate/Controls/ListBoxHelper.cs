using STranslate.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace STranslate.Controls;

public static class ListBoxHelper
{
    public static readonly DependencyProperty ScrollAtBottomCommandProperty =
        DependencyProperty.RegisterAttached(
            "ScrollAtBottomCommand",
            typeof(ICommand),
            typeof(ListBoxHelper),
            new PropertyMetadata(null, OnScrollAtBottomCommandChanged));

    public static ICommand GetScrollAtBottomCommand(DependencyObject obj)
    {
        return (ICommand)obj.GetValue(ScrollAtBottomCommandProperty);
    }

    public static void SetScrollAtBottomCommand(DependencyObject obj, ICommand value)
    {
        obj.SetValue(ScrollAtBottomCommandProperty, value);
    }

    private static void OnScrollAtBottomCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox) return;

        // 当命令改变时，添加或移除 Loaded 事件处理器
        if (e.OldValue is not null)
        {
            listBox.Loaded -= ListBox_Loaded;
        }
        if (e.NewValue is not null)
        {
            listBox.Loaded += ListBox_Loaded;
        }
    }

    private static void ListBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        // 查找内部的 ScrollViewer
        var scrollViewer = Utilities.GetVisualChild<ScrollViewer>(listBox);
        if (scrollViewer != null)
        {
            // 订阅 ScrollChanged 事件
            scrollViewer.ScrollChanged += (s, args) =>
            {
                // 检查是否滚动到底部
                // 增加一个小的容差值 (1.0) 来处理可能的浮点数精度问题
                var isAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 1.0 &&
                                 scrollViewer.ScrollableHeight > 0;

                if (!isAtBottom) return;

                var command = GetScrollAtBottomCommand(listBox);
                if (command != null && command.CanExecute(null))
                {
                    command.Execute(null);
                }
            };
        }
    }
}