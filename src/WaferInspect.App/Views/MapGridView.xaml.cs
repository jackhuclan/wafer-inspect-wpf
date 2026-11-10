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
            foreach (var cell in model.Cells) PaintCell(pixels, cell, cellWidth, cellHeight);
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
        if (e.ClickCount != 2 || Model == null) return;
        var point = e.GetPosition(CanvasRoot);
        double cellWidth = Size / (double)Math.Max(1, Model.Columns);
        double cellHeight = Size / (double)Math.Max(1, Model.Rows);
        int column = (int)Math.Floor(point.X / cellWidth);
        int row = (int)Math.Floor(point.Y / cellHeight);
        Model.Select(column, row);   // BR-51：左上角 [0,0]，X=列 Y=行
    }
}
