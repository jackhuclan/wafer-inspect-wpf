using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WaferInspectHMI.Models;
using WaferInspectHMI.ViewModels;

namespace WaferInspectHMI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _navCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _vm.WindowActionRequested += OnWindowAction;
        StateChanged += (_, _) => UpdateMaximizeIcon();
        UpdateMaximizeIcon();
    }

    /* ---------------- 标题栏：拖拽 / 双击 / 窗口三态命令 ---------------- */
    private void TitleBar_Down(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) != null) return;
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (WindowState == WindowState.Maximized) return;
        DragMove();
    }

    private void OnWindowAction(object sender, string action)
    {
        switch (action)
        {
            case "min": WindowState = WindowState.Minimized; break;
            case "max": ToggleMaximize(); break;
            case "close": Close(); break;
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeIcon();
    }

    private void UpdateMaximizeIcon()
    {
        bool max = WindowState == WindowState.Maximized;
        MaxIcon.Data = Geometry.Parse(max ? "M8 4h12v12 M16 14v4H6V8h4" : "M5 5h14v14H5z");
        MaxButton.ToolTip = max ? "向下还原" : "最大化";
        Root.Margin = max ? new Thickness(0) : new Thickness(0);
        BorderThickness = max ? new Thickness(0) : new Thickness(1);
    }

    /* ---------------- 用户菜单与通知 ---------------- */
    private void CloseUserMenu_Click(object sender, RoutedEventArgs e) => UserToggle.IsChecked = false;

    private void Notify_Click(object sender, RoutedEventArgs e)
        => _vm.ShowNotification("2 条未读通知：VAC-02 真空告警 · WFR-045 良率预警");

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        UserToggle.IsChecked = false;
        _vm.ShowNotification("已退出登录，窗口切换为只读监控模式");
    }

    /* ---------------- 侧栏折叠 ---------------- */
    private void CollapseNav_Click(object sender, RoutedEventArgs e)
    {
        _navCollapsed = !_navCollapsed;
        SidebarColumn.Width = new GridLength(_navCollapsed ? 64 : 236);
        SidebarFooter.Visibility = _navCollapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var tb in FindVisualChildren<TextBlock>(Sidebar))
        {
            if (tb.DataContext is NavItem || tb.DataContext is NavGroup)
                tb.Visibility = _navCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        // 图标条模式下分组标题压缩为居中的箭头按钮，仍可独立展开 / 收起。
        foreach (var btn in FindVisualChildren<Button>(Sidebar))
        {
            if (btn.Tag is not string tag || tag != "NavGroupToggle") continue;
            btn.Width = _navCollapsed ? 40 : double.NaN;
            btn.HorizontalAlignment = _navCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            btn.Padding = _navCollapsed ? new Thickness(0) : new Thickness(10, 0, 10, 0);
        }
        _vm.ShowNotification(_navCollapsed ? "导航栏已收起为图标模式" : "导航栏已展开");
    }

    /* ---------------- 任务详情 ---------------- */
    private void TaskDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var task = _vm.Tasks.FirstOrDefault(t => t.Id == id);
        if (task == null) return;
        MessageBox.Show(
            $"任务编号：{task.Id}\n批次 / 晶圆：{task.Lot} / {task.Wafer}\n检测配方：{task.Recipe}\n" +
            $"腔体：{task.Chamber}\n状态：{task.StatusText}\n进度：{task.ProgressText}\n开始时间：{task.Start}",
            "检测任务详情", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /* ---------------- 可视化树工具 ---------------- */
    private static T FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
