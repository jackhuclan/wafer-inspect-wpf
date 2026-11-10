using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaferInspect.Core.Inspection;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Views;

/// <summary>
/// H04 共享 Map 圈选画布交互（F-3.2.2-70/77、F-3.8.1-03、BR-38）：
/// 拖拽 = 添加扫描区域 / ROI 区域，双击 Die = 抽检；具体写入由承载 ViewModel 转发到唯一数据源。
/// 网格以单张 <see cref="WriteableBitmap"/> 呈现，避免大量子元素进入 WPF 布局与 UIA 树。
/// </summary>
public partial class MapRegionCanvas : UserControl
{
    private const int Size = 400;
    private const double Cell = MapRegionCanvasState.CellSize;
    private const int Limit = MapRegionCanvasState.Columns;

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(MapRegionCanvasState), typeof(MapRegionCanvas),
        new PropertyMetadata(null, OnStateChanged));

    private MapRegionCanvasState _subscribed;

    public MapRegionCanvas() => InitializeComponent();

    /// <summary>唯一数据源快照状态（各界面均绑定各自 ViewModel 的 <c>Map</c> 属性）。</summary>
    public MapRegionCanvasState State
    {
        get => (MapRegionCanvasState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (MapRegionCanvas)d;
        if (canvas._subscribed != null) canvas._subscribed.Refreshed -= canvas.OnStateRefreshed;
        canvas._subscribed = e.NewValue as MapRegionCanvasState;
        if (canvas._subscribed != null) canvas._subscribed.Refreshed += canvas.OnStateRefreshed;
        canvas.Redraw();
    }

    private void OnStateRefreshed(object sender, EventArgs e) => Redraw();

    private void Redraw()
    {
        var pixels = new byte[Size * Size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xF5; pixels[i + 1] = 0xF2; pixels[i + 2] = 0xED; pixels[i + 3] = 0xFF;
        }

        if (State != null)
        {
            foreach (var cell in State.Cells) FillCell(pixels, cell);
            foreach (var region in State.Regions) OverlayRegion(pixels, region);
        }

        var bitmap = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, Size, Size), pixels, Size * 4, 0);
        bitmap.Freeze();
        MapImage.Source = bitmap;
    }

    private static void FillCell(byte[] pixels, MapCellRow cell)
    {
        var color = (Color)ColorConverter.ConvertFromString(cell.Fill);
        int x0 = (int)cell.X, y0 = (int)cell.Y;
        for (int y = y0; y < y0 + (int)Cell; y++)
        for (int x = x0; x < x0 + (int)Cell; x++)
        {
            bool gridLine = x == x0 || y == y0;
            Set(pixels, x, y, gridLine ? Colors.White : color);
        }
    }

    private static void OverlayRegion(byte[] pixels, ScanRegionRow region)
    {
        int x0 = (int)region.X, y0 = (int)region.Y;
        int x1 = x0 + (int)region.Width, y1 = y0 + (int)region.Height;
        var accent = Color.FromRgb(0x25, 0x63, 0xEB);
        for (int y = Math.Max(0, y0); y < Math.Min(Size, y1); y++)
        for (int x = Math.Max(0, x0); x < Math.Min(Size, x1); x++)
        {
            bool edge = x - x0 < 2 || y - y0 < 2 || x1 - x <= 2 || y1 - y <= 2;
            if (edge) { Set(pixels, x, y, accent); continue; }

            int at = (y * Size + x) * 4;
            pixels[at] = (byte)((pixels[at] * 7 + accent.B * 3) / 10);
            pixels[at + 1] = (byte)((pixels[at + 1] * 7 + accent.G * 3) / 10);
            pixels[at + 2] = (byte)((pixels[at + 2] * 7 + accent.R * 3) / 10);
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

    private Point? _dragStart;

    private void CanvasRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IMapRegionCanvasHost host) return;
        var origin = e.GetPosition(CanvasRoot);

        // BR-38：双击选中 Map 图单个 Die，可设单个或多个。
        if (e.ClickCount == 2)
        {
            var cell = CellAt(origin);
            if (cell != null) host.ToggleDie(cell.Row, cell.Column);
            return;
        }

        _dragStart = origin;
        SelRect.Visibility = Visibility.Collapsed;
        CanvasRoot.CaptureMouse();
    }

    private void CanvasRoot_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        DrawSelection(ToDieRect(start, e.GetPosition(CanvasRoot)));
    }

    private void CanvasRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start) return;
        _dragStart = null;
        CanvasRoot.ReleaseMouseCapture();

        var rect = ToDieRect(start, e.GetPosition(CanvasRoot));
        DrawSelection(rect);
        if (DataContext is IMapRegionCanvasHost host) host.AddRegion(rect);
    }

    private static MapCellRow CellAt(Point position)
    {
        int column = (int)Math.Floor(position.X / Cell) + 1;
        int row = (int)Math.Floor(position.Y / Cell) + 1;
        return column is >= 1 and <= Limit && row is >= 1 and <= Limit
            ? new MapCellRow(row, column)
            : null;
    }

    private static MapRect ToDieRect(Point start, Point end)
    {
        int x1 = Clamp((int)Math.Floor(Math.Min(start.X, end.X) / Cell) + 1);
        int y1 = Clamp((int)Math.Floor(Math.Min(start.Y, end.Y) / Cell) + 1);
        int x2 = Clamp((int)Math.Floor(Math.Max(start.X, end.X) / Cell) + 1);
        int y2 = Clamp((int)Math.Floor(Math.Max(start.Y, end.Y) / Cell) + 1);
        return new MapRect(x1, y1, x2 - x1 + 1, y2 - y1 + 1);
    }

    private static int Clamp(int value) => Math.Max(1, Math.Min(Limit, value));

    private void DrawSelection(MapRect rect)
    {
        // SelRect 位于 Grid（非 Canvas）内，用 Margin 定位，不能依赖 Canvas.Left/Top 附加属性。
        SelRect.Margin = new Thickness((rect.X - 1) * Cell, (rect.Y - 1) * Cell, 0, 0);
        SelRect.Width = rect.Width * Cell;
        SelRect.Height = rect.Height * Cell;
        SelRect.Visibility = Visibility.Visible;
    }
}
