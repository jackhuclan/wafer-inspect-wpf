using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Views;

/// <summary>子配方七步向导窗口（T-042 顺序 / BR-19 可重入 / B-25 门禁）；编排逻辑全在 ViewModel。</summary>
public partial class SubRecipeWizardDialog : Window
{
    private readonly SubRecipeWizardViewModel _vm;

    /// <summary>由 DI 工厂构造（依赖 ISubRecipeRepository / IJobRepository / IMapTypeCatalog / IH02DialogService 与 H03 对位能力族）。</summary>
    public SubRecipeWizardDialog(SubRecipeWizardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /* ---------------- 步骤 2：视窗图像拖拽框选（F-3.2.2-38：框选后自动执行拉平） ---------------- */

    private Point? _fovDragStart;

    private void FovCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _fovDragStart = e.GetPosition(FovCanvas);
        FovSelRect.Visibility = Visibility.Collapsed;
        FovCanvas.CaptureMouse();
    }

    private void FovCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_fovDragStart is not { } start) return;
        var current = e.GetPosition(FovCanvas);
        DrawFovSelection(ClampToImage(start, current));
    }

    private void FovCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_fovDragStart is not { } start) return;
        _fovDragStart = null;
        FovCanvas.ReleaseMouseCapture();

        var rect = ClampToImage(start, e.GetPosition(FovCanvas));
        if (rect.Width < 8 || rect.Height < 8)
        {
            FovSelRect.Visibility = Visibility.Collapsed;
            _vm.StatusText = "框选区域过小（至少 8×8 像素）：请重新拖拽框选含特征的 Die 区域（F-3.2.2-38）";
            return;
        }

        DrawFovSelection(rect);
        // F-3.2.2-38：框选结束后自动执行拉平。
        _vm.ExecuteLeveling((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
    }

    private Rect ClampToImage(Point start, Point end)
    {
        double x = Math.Max(0, Math.Min(start.X, end.X));
        double y = Math.Max(0, Math.Min(start.Y, end.Y));
        double w = Math.Min(SubRecipeWizardViewModel.FovWidth, Math.Abs(end.X - start.X));
        double h = Math.Min(SubRecipeWizardViewModel.FovHeight, Math.Abs(end.Y - start.Y));
        if (start.X > end.X) x = Math.Max(0, start.X - w);
        if (start.Y > end.Y) y = Math.Max(0, start.Y - h);
        return new Rect(x, y, w, h);
    }

    private void DrawFovSelection(Rect rect)
    {
        Canvas.SetLeft(FovSelRect, rect.X);
        Canvas.SetTop(FovSelRect, rect.Y);
        FovSelRect.Width = rect.Width;
        FovSelRect.Height = rect.Height;
        FovSelRect.Visibility = Visibility.Visible;
    }

    /* ---------------- 步骤 3：Map 网格拖动 / 双击 Die 填行列（F-3.2.2-45/46/59） ---------------- */

    private Point? _gridDragStart;
    private bool _gridDragging;

    private void MarkGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // F-3.2.2-59：双击 Die 网格 → 自动填入当前 Mark 的行列。
        if (e.ClickCount == 2)
        {
            if (e.OriginalSource is FrameworkElement { DataContext: MarkCellRow cell })
                _vm.PickMarkDie(cell);
            return;
        }

        // F-3.2.2-45：单击拖动移动网格（确定定位锁定前）。
        _gridDragStart = e.GetPosition(MarkGridCanvas);
        _gridDragging = false;
        MarkGridCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void MarkGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_gridDragStart is not { } start) return;
        var current = e.GetPosition(MarkGridCanvas);
        double dx = current.X - start.X;
        double dy = current.Y - start.Y;

        if (!_gridDragging && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;   // 移动阈值，避免与双击冲突
        _gridDragging = true;
        _gridDragStart = current;
        _vm.MoveGrid(dx, dy);
    }

    private void MarkGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_gridDragStart == null) return;
        _gridDragStart = null;
        _gridDragging = false;
        MarkGridCanvas.ReleaseMouseCapture();
    }
}
