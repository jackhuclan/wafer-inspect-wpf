using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaferInspect.Core.Maps;

namespace WaferInspectHMI.Views;

/// <summary>
/// H05 唯一 Map 图谱控件（B-06）：按 <see cref="MapGridModel"/> 的 Bin 颜色绘制，
/// 双击选中 Die（F-3.7.3-19），坐标换算统一走 BR-51/52；X/Y 跳转由模型提供。
/// 单张位图绘制，避免 400 个子元素进入可视化 / UIA 树。
/// </summary>
public partial class MapGridView : UserControl
{
    private const int Size = 400;

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(MapGridModel), typeof(MapGridView),
        new PropertyMetadata(null, OnModelChanged));

    private MapGridModel _subscribed;
    private bool _dragging;
    private int _dragC0;
    private int _dragR0;

    /// <summary>Wafer 扫描笔刷：非 null 时双击网格把该 Die 改为本 Bin（F-3.7.3-19）。
    /// 为 null 时保持原「双击选中」行为（maps 预览 / H08 复判兼容，不破坏既有页面）。</summary>
    public static readonly DependencyProperty BrushBinProperty = DependencyProperty.Register(
        nameof(BrushBin), typeof(BinDefinition), typeof(MapGridView), new PropertyMetadata(null));

    public BinDefinition BrushBin
    {
        get => (BinDefinition)GetValue(BrushBinProperty);
        set => SetValue(BrushBinProperty, value);
    }

    /// <summary>Wafer 扫描矩形批量模式（F-3.7.3-22）：开启后按住左键拖拽框选区域并触发 <see cref="RectSelected"/>。</summary>
    public static readonly DependencyProperty BatchModeProperty = DependencyProperty.Register(
        nameof(BatchMode), typeof(bool), typeof(MapGridView), new PropertyMetadata(false));

    public bool BatchMode
    {
        get => (bool)GetValue(BatchModeProperty);
        set => SetValue(BatchModeProperty, value);
    }

    /// <summary>Wafer 扫描 Ink 墨点笔刷（BR-49）：开启后单击网格即切换该 Die 的 Ink 墨点标记，
    /// 与 <see cref="BrushBin"/> / <see cref="BatchMode"/> 正交；为 null/false 时保持既有行为（向后兼容）。</summary>
    public static readonly DependencyProperty InkModeProperty = DependencyProperty.Register(
        nameof(InkMode), typeof(bool), typeof(MapGridView), new PropertyMetadata(false));

    public bool InkMode
    {
        get => (bool)GetValue(InkModeProperty);
        set => SetValue(InkModeProperty, value);
    }

    /// <summary>矩形框选完成事件（批量修改触发），参数为归一化后的四角坐标。</summary>
    public event EventHandler<RectSelectedEventArgs> RectSelected;

    public MapGridView() => InitializeComponent();

    public MapGridModel Model
    {
        get => (MapGridModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MapGridView)d;
        if (view._subscribed != null) view._subscribed.Changed -= view.OnModelUpdated;
        view._subscribed = e.NewValue as MapGridModel;
        if (view._subscribed != null) view._subscribed.Changed += view.OnModelUpdated;
        view.Redraw();
    }

    private void OnModelUpdated(object sender, EventArgs e) => Redraw();

    private void Redraw()
    {
        var pixels = new byte[Size * Size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xF5; pixels[i + 1] = 0xF2; pixels[i + 2] = 0xED; pixels[i + 3] = 0xFF;
        }

        var model = Model;
        if (model != null && model.Cells.Count > 0)
        {
            double cellWidth = Size / (double)Math.Max(1, model.Columns);
            double cellHeight = Size / (double)Math.Max(1, model.Rows);
            foreach (var cell in model.Cells)
            {
                PaintCell(pixels, cell, cellWidth, cellHeight);
                if (cell.Inked) PaintInkDot(pixels, cell, cellWidth, cellHeight);
            }
            if (model.Selected != null) PaintSelection(pixels, model.Selected.Column, model.Selected.Row, cellWidth, cellHeight);
        }

        var bitmap = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, Size, Size), pixels, Size * 4, 0);
        bitmap.Freeze();
        MapImage.Source = bitmap;
    }

    private static void PaintCell(byte[] pixels, MapGridCell cell, double cellWidth, double cellHeight)
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(cell.ColorHex); }
        catch (FormatException) { color = Colors.LightGray; }

        int x0 = (int)Math.Round(cell.Column * cellWidth);
        int y0 = (int)Math.Round(cell.Row * cellHeight);
        int x1 = (int)Math.Round((cell.Column + 1) * cellWidth);
        int y1 = (int)Math.Round((cell.Row + 1) * cellHeight);
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            bool gridLine = x == x0 || y == y0;
            Set(pixels, x, y, gridLine ? Colors.White : color);
        }
    }

    private static void PaintSelection(byte[] pixels, int column, int row, double cellWidth, double cellHeight)
    {
        int x0 = (int)Math.Round(column * cellWidth);
        int y0 = (int)Math.Round(row * cellHeight);
        int x1 = (int)Math.Round((column + 1) * cellWidth);
        int y1 = (int)Math.Round((row + 1) * cellHeight);
        var accent = Color.FromRgb(0x1D, 0x4F, 0xD1);
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
            if (x - x0 < 2 || y - y0 < 2 || x1 - x <= 2 || y1 - y <= 2) Set(pixels, x, y, accent);
    }

    /// <summary>BR-49：在单元格中心绘制墨点（物理墨点属性可视化）。</summary>
    private static void PaintInkDot(byte[] pixels, MapGridCell cell, double cellWidth, double cellHeight)
    {
        double cx = (cell.Column + 0.5) * cellWidth;
        double cy = (cell.Row + 0.5) * cellHeight;
        double r = Math.Max(1, Math.Min(cellWidth, cellHeight) * 0.22);
        var ink = Color.FromRgb(0x22, 0x22, 0x22);
        int x0 = (int)Math.Floor(cx - r), x1 = (int)Math.Ceiling(cx + r);
        int y0 = (int)Math.Floor(cy - r), y1 = (int)Math.Ceiling(cy + r);
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
            if (dx * dx + dy * dy <= r * r) Set(pixels, x, y, ink);
        }
    }

    private static void Set(byte[] pixels, int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size) return;
        int at = (y * Size + x) * 4;
        pixels[at] = color.B;
        pixels[at + 1] = color.G;
        pixels[at + 2] = color.R;
        pixels[at + 3] = 0xFF;
    }

    private void CanvasRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Model == null) return;
        int column = CellAt(e.GetPosition(CanvasRoot), out int row);
        if (InkMode)   // BR-49：墨点笔刷，单击即切换该 Die 的 Ink 标记（正交于笔刷/批量）
        {
            var existing = Model.Cells.FirstOrDefault(c => c.Column == column && c.Row == row);
            if (existing == null) return;
            Model.EditInk(column, row, !existing.Inked);
            Model.Select(column, row);
            return;
        }
        if (BatchMode)
        {
            _dragging = true;
            _dragC0 = column;
            _dragR0 = row;
            ShowOverlay(column, row, column, row);
            return;
        }
        if (e.ClickCount != 2) return;
        if (BrushBin != null)
        {
            // F-3.7.3-19：Die 笔刷直接改图（颜色即时刷新），并同步更新选中格供整行/整列操作。
            Model.EditCell(column, row, BrushBin);
            Model.Select(column, row);
        }
        else
        {
            Model.Select(column, row);   // BR-51：左上角 [0,0]，X=列 Y=行
        }
    }

    private void CanvasRoot_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || Model == null) return;
        int column = CellAt(e.GetPosition(CanvasRoot), out int row);
        ShowOverlay(_dragC0, _dragR0, column, row);
    }

    private void CanvasRoot_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || Model == null) return;
        _dragging = false;
        int column = CellAt(e.GetPosition(CanvasRoot), out int row);
        if (RectOverlay != null) RectOverlay.Visibility = Visibility.Collapsed;
        RectSelected?.Invoke(this, new RectSelectedEventArgs(
            Math.Min(_dragC0, column), Math.Min(_dragR0, row),
            Math.Max(_dragC0, column), Math.Max(_dragR0, row)));
    }

    private int CellAt(Point point, out int row)
    {
        double cellWidth = Size / (double)Math.Max(1, Model.Columns);
        double cellHeight = Size / (double)Math.Max(1, Model.Rows);
        int column = Clamp((int)Math.Floor(point.X / cellWidth), 0, Model.Columns - 1);
        row = Clamp((int)Math.Floor(point.Y / cellHeight), 0, Model.Rows - 1);
        return column;
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;

    private void ShowOverlay(int c0, int r0, int c1, int r1)
    {
        if (Model == null || RectOverlay == null) return;
        double cellWidth = Size / (double)Math.Max(1, Model.Columns);
        double cellHeight = Size / (double)Math.Max(1, Model.Rows);
        double x0 = Math.Min(c0, c1) * cellWidth;
        double y0 = Math.Min(r0, r1) * cellHeight;
        double x1 = (Math.Max(c0, c1) + 1) * cellWidth;
        double y1 = (Math.Max(r0, r1) + 1) * cellHeight;
        RectOverlay.Margin = new Thickness(x0, y0, 0, 0);
        RectOverlay.Width = x1 - x0;
        RectOverlay.Height = y1 - y0;
        RectOverlay.Visibility = Visibility.Visible;
    }
}

/// <summary>矩形框选完成参数（已归一化）。</summary>
public sealed class RectSelectedEventArgs : EventArgs
{
    public RectSelectedEventArgs(int c0, int r0, int c1, int r1)
    {
        C0 = c0; R0 = r0; C1 = c1; R1 = r1;
    }

    public int C0 { get; }
    public int R0 { get; }
    public int C1 { get; }
    public int R1 { get; }
}
