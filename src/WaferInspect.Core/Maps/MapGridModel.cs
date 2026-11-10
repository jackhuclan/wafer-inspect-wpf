namespace WaferInspect.Core.Maps;

/// <summary>
/// Map 网格单元格（B-06 唯一渲染规则的输出）：坐标、Bin 类型 / 代码 / 等级 / 颜色 / 类别名称。
/// 首页图谱、向导网格、图谱管理预览、H04 区域画布都消费本模型，不各自复制坐标与着色规则。
/// </summary>
public sealed record MapGridCell(int Row, int Column, BinKind Kind, string Code, int Level, string ColorHex, string Label)
{
    /// <summary>BR-51：缺陷所在 Die 格式 <c>[列,行]</c>，图谱左上角 <c>[0,0]</c>。</summary>
    public string Coordinate => MapGridModel.FormatCoordinate(Column, Row);

    public string Tip => $"{Coordinate} · {Label}（{Code}）· 等级 {Level}";
}

/// <summary>
/// H05 唯一 Map 网格模型（R-H05-03 / B-06）：Bin 着色、坐标换算、双击选中、X/Y 跳转、显式刷新。
/// 纯数据，不含 WPF 类型；App 的 <c>MapGridView</c> 只负责把 Cells 画出来。
/// </summary>
public sealed class MapGridModel
{
    public const int DefaultRows = 20;
    public const int DefaultColumns = 20;

    public int Rows { get; private set; } = DefaultRows;
    public int Columns { get; private set; } = DefaultColumns;

    public IReadOnlyList<MapGridCell> Cells { get; private set; } = [];

    public MapGridCell? Selected { get; private set; }

    /// <summary>显式刷新次数（BR-53：复判完成后不点「刷新图谱」，颜色不变）。</summary>
    public int RefreshVersion { get; private set; }

    public bool NeedsRefresh { get; private set; }

    public string StatusText { get; private set; } = "尚未生成 Map 图（BR-45：需先导入图谱文件）";

    /// <summary>状态变化通知；共享 <c>MapGridView</c> 据此重绘（控件不自建第二份绘制规则）。</summary>
    public event EventHandler? Changed;

    /// <summary>BR-51：Die 显示格式 [列,行]，左上角 [0,0]（H05 唯一换算口径）。</summary>
    public static string FormatCoordinate(int column, int row) => $"[{column},{row}]";

    /// <summary>
    /// H01/H04 的 Die 行列号是 1-based，H05 预览显示是 0-based；
    /// 所有需要把 1-based 行列号呈现为 [列,行] 的地方都必须调用本方法，禁止各界面自行 -1。
    /// </summary>
    public static string FormatDieCoordinate(int column1Based, int row1Based)
        => FormatCoordinate(column1Based - 1, row1Based - 1);

    /// <summary>解析 <c>[列,行]</c>；X 对应列、Y 对应行（BR-52）。</summary>
    public static bool TryParseCoordinate(string text, out int column, out int row)
    {
        column = row = -1;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string body = text.Trim().Trim('[', ']');
        string[] parts = body.Split(',');
        return parts.Length == 2
               && int.TryParse(parts[0].Trim(), out column)
               && int.TryParse(parts[1].Trim(), out row);
    }

    /// <summary>BR-52：X 对应列坐标、Y 对应行坐标；越界返回 false，不静默钳制。</summary>
    public bool JumpTo(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Columns || y >= Rows)
        {
            StatusText = $"X/Y 跳转越界：X={x}（列，0~{Columns - 1}）Y={y}（行，0~{Rows - 1}）";
            Raise();
            return false;
        }
        Select(x, y);
        StatusText = $"已跳转到 [列,行]={FormatCoordinate(x, y)}（BR-51/52，X=列 Y=行）";
        Raise();
        return true;
    }

    /// <summary>双击 Map 任意网格选中该 Die（F-3.7.3-19）。</summary>
    public bool Select(int column, int row)
    {
        var cell = Cells.FirstOrDefault(c => c.Column == column && c.Row == row);
        if (cell == null)
        {
            StatusText = $"网格 {FormatCoordinate(column, row)} 不存在";
            Raise();
            return false;
        }
        Selected = cell;
        StatusText = $"已选中 Die {cell.Coordinate} · {cell.Label}（{cell.Code}）· 等级 {cell.Level}";
        Raise();
        return true;
    }

    /// <summary>
    /// BR-45：生成 Map 图需先导入图谱文件；未导入时明确拒绝，不伪造 Map 数据。
    /// 生成成功后按选中 MapType 的 Bin 颜色着色（F-3.7.2-11/12）。
    /// </summary>
    public bool Generate(string mapTypeId, IMapTypeCatalog catalog, int rows = DefaultRows, int columns = DefaultColumns)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var type = catalog.Find(mapTypeId);
        if (type == null)
        {
            StatusText = $"图谱类型 {mapTypeId} 不存在（请先选择或新建）";
            Raise();
            return false;
        }
        if (string.IsNullOrWhiteSpace(type.MapFilePath))
        {
            StatusText = "生成 Map 图失败：需先导入图谱文件（BR-45 / F-3.7.2-11）";
            Raise();
            return false;
        }
        if (rows <= 0 || columns <= 0 || rows > 200 || columns > 200)
        {
            StatusText = "行列数必须在 1~200 之间";
            Raise();
            return false;
        }

        Rows = rows;
        Columns = columns;
        RebuildCells(catalog, mapTypeId);
        NeedsRefresh = false;
        RefreshVersion++;
        StatusText = $"已生成 Map 图：{rows}×{columns} Die，图谱「{type.Name}」，" +
                     $"文件 {System.IO.Path.GetFileName(type.MapFilePath)}（BR-45）";
        Raise();
        return true;
    }

    /// <summary>
    /// BR-53：复判完成后 Die 颜色**不自动更新**，须点击「刷新图谱」。
    /// 本方法只按当前 MapType 重算颜色，是唯一的显式刷新入口。
    /// </summary>
    public bool Refresh(string mapTypeId, IMapTypeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (Cells.Count == 0)
        {
            StatusText = "请先生成 Map 图（BR-45）后再刷新";
            Raise();
            return false;
        }
        RebuildCells(catalog, mapTypeId);
        NeedsRefresh = false;
        RefreshVersion++;
        StatusText = $"刷新图谱完成（v{RefreshVersion}）：颜色已按当前 MapType 更新（BR-53）";
        Raise();
        return true;
    }

    /// <summary>复判 / 外部数据变化后标记脏，但不立即刷新（BR-53）。</summary>
    public void MarkDirty()
    {
        NeedsRefresh = true;
        StatusText = "数据已变化：Map 颜色保持不变，需点击「刷新图谱」（BR-53）";
        Raise();
    }

    /// <summary>
    /// H08 复判全景 Map：用指定 Wafer 的逐 Die Bin 矩阵构建网格，
    /// 颜色 / 代码 / 等级取自 mapTypeId 对应图谱的 Bin 定义（B-11，唯一数据源），不各页面自建。
    /// </summary>
    public bool LoadWafer(string mapTypeId, IMapTypeCatalog catalog, IReadOnlyList<BinKind> matrix, int rows, int columns)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(matrix);
        var type = catalog.Find(mapTypeId);
        if (type == null)
        {
            StatusText = $"图谱类型 {mapTypeId} 不存在（请先在图谱管理创建或选择）";
            Raise();
            return false;
        }
        if (rows <= 0 || columns <= 0 || rows > 200 || columns > 200)
        {
            StatusText = "行列数必须在 1~200 之间";
            Raise();
            return false;
        }
        if (matrix.Count != rows * columns)
        {
            StatusText = $"Bin 矩阵长度 {matrix.Count} 与 {rows}×{columns} 不一致（数据损坏？）";
            Raise();
            return false;
        }

        Rows = rows;
        Columns = columns;
        var byKind = type.Bins.ToDictionary(b => b.Kind, b => b);
        var cells = new List<MapGridCell>(rows * columns);
        for (int i = 0; i < matrix.Count; i++)
        {
            int row = i / columns;
            int column = i % columns;
            var kind = matrix[i];
            var bin = byKind.TryGetValue(kind, out var b)
                ? b
                : kind.CreateDefault(0, MapLevelPalette.ColorFor("PL", kind.DefaultLevel()));
            cells.Add(new MapGridCell(row, column, bin.Kind, bin.Code, bin.Level, bin.ColorHex, bin.Name));
        }
        Cells = cells;
        NeedsRefresh = false;
        RefreshVersion++;
        StatusText = $"已载入 Wafer 全景 Map：{rows}×{columns} Die，图谱「{type.Name}」（H08 复判全景 Map，B-06）";
        Raise();
        return true;
    }

    /// <summary>
    /// H08 人工 / 批量 / 快捷键复判：把指定 Die 重新分配为某 Bin。
    /// BR-53：复判后 Die 颜色**不立即更新**——逻辑 Kind 已更新，但 ColorHex 保留旧值，
    /// 必须点击「刷新图谱」(<see cref="RecolorWafer"/>) 才按当前 Bin 重新取色。
    /// </summary>
    public bool Reassign(int column, int row, BinDefinition bin)
    {
        ArgumentNullException.ThrowIfNull(bin);
        var cell = Cells.FirstOrDefault(c => c.Column == column && c.Row == row);
        if (cell == null)
        {
            StatusText = $"网格 {FormatCoordinate(column, row)} 不存在";
            Raise();
            return false;
        }
        // ColorHex 故意保留旧值（BR-53）；其余逻辑字段更新为复判结果。
        var updated = cell with { Kind = bin.Kind, Code = bin.Code, Level = bin.Level, Label = bin.Name };
        var list = Cells.ToList();
        int at = list.FindIndex(c => c.Column == column && c.Row == row);
        list[at] = updated;
        Cells = list;
        NeedsRefresh = true;
        StatusText = $"复判已完成 Die {cell.Coordinate} → {bin.Code}（{bin.Name}）；颜色待「刷新图谱」更新（BR-53）";
        Raise();
        return true;
    }

    /// <summary>
    /// H08 显式刷新：按每颗 Die 当前 Kind 经 <see cref="IMapTypeCatalog"/> 重新取色（B-11），
    /// 是复判后颜色更新的唯一入口（BR-53）；Kind 不变，仅更新展示字段。
    /// </summary>
    public bool RecolorWafer(string mapTypeId, IMapTypeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (Cells.Count == 0)
        {
            StatusText = "请先载入 Wafer 全景 Map（B-06）后再刷新";
            Raise();
            return false;
        }
        var type = catalog.Find(mapTypeId);
        if (type == null)
        {
            StatusText = $"图谱类型 {mapTypeId} 不存在";
            Raise();
            return false;
        }
        var byKind = type.Bins.ToDictionary(b => b.Kind, b => b);
        var list = Cells.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var cell = list[i];
            if (byKind.TryGetValue(cell.Kind, out var bin))
                list[i] = cell with { Code = bin.Code, Level = bin.Level, ColorHex = bin.ColorHex, Label = bin.Name };
        }
        Cells = list;
        NeedsRefresh = false;
        RefreshVersion++;
        StatusText = $"刷新图谱完成（v{RefreshVersion}）：颜色已按当前 Bin 更新（BR-53）";
        Raise();
        return true;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    private void RebuildCells(IMapTypeCatalog catalog, string mapTypeId)
    {
        var bins = catalog.Find(mapTypeId)?.Bins ?? [];
        var cells = new List<MapGridCell>(Rows * Columns);
        for (int row = 0; row < Rows; row++)
        for (int column = 0; column < Columns; column++)
        {
            // 确定性分配：同一 MapType + 行列恒得同一 Bin，便于回归与刷新前后比对。
            var bin = bins.Count == 0
                ? BinKind.BlankDie.CreateDefault(0, "#E8EDF5")
                : bins[(row * 31 + column * 7) % bins.Count];
            cells.Add(new MapGridCell(row, column, bin.Kind, bin.Code, bin.Level, bin.ColorHex, bin.Name));
        }
        Cells = cells;
    }
}
