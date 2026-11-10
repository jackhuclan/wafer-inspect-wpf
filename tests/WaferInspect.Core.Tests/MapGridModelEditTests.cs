#nullable enable
using System.Collections.Generic;
using System.Linq;
using WaferInspect.Core.Maps;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P4-A 增量（1.5.0，H05 Wafer 扫描，PRD 4.5.2 / UI-3.7.3）：
/// <see cref="MapGridModel"/> 新增的本地编辑方法——Die 笔刷（F-3.7.3-19）、
/// 整行整列（F-3.7.3-20）、矩形批量（F-3.7.3-22）、外扩内缩（F-3.7.3-21/23，R-H05-05）。
/// 与 H08 复判 <see cref="MapGridModel.Reassign"/> 的关键区别：这些是直接编辑，颜色**即时**更新
/// （NeedsRefresh=false），不触发 BR-53 的「复判后待刷新」语义。颜色 / 代码 / 等级唯一来自
/// <see cref="IMapTypeCatalog"/>（B-11）。外扩内缩是纯本地形态学操作，不伪造 H03/H06 结果。
/// </summary>
public sealed class MapGridModelEditTests
{
    private static EditableMapTypeCatalog NewCatalog() => new();

    private static BinDefinition BinOf(EditableMapTypeCatalog catalog, BinKind kind)
        => catalog.Find("MAP-PL-300")!.Bins.First(b => b.Kind == kind);

    private static MapGridModel LoadedGrid(EditableMapTypeCatalog catalog, BinKind fill, int rows, int cols)
    {
        var grid = new MapGridModel();
        var matrix = Enumerable.Repeat(fill, rows * cols).ToList();
        Assert.True(grid.LoadWafer("MAP-PL-300", catalog, matrix, rows, cols));
        return grid;
    }

    /* ---------------- Die 笔刷（F-3.7.3-19） ---------------- */

    [Fact]
    public void EditCell_UpdatesSingleCell_KindAndColorFromBin()   // B-11 单一数据源
    {
        var catalog = NewCatalog();
        var grid = LoadedGrid(catalog, BinKind.BlankDie, 6, 6);
        var defect = BinOf(catalog, BinKind.DefectDie);

        Assert.True(grid.EditCell(4, 4, defect));
        var cell = grid.Cells.First(c => c.Column == 4 && c.Row == 4);
        Assert.Equal(BinKind.DefectDie, cell.Kind);
        Assert.Equal(defect.Code, cell.Code);
        Assert.Equal(defect.ColorHex, cell.ColorHex);          // 颜色来自图谱（B-11），非本地写死
        Assert.Equal(defect.Level, cell.Level);
    }

    [Fact]
    public void EditCell_WhenNoMap_ReturnsFalse()
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        Assert.False(grid.EditCell(0, 0, BinOf(catalog, BinKind.GoodDie)));
        Assert.Contains("请先生成", grid.StatusText);
    }

    [Fact]
    public void EditCell_OutOfRange_ReturnsFalse()
    {
        var catalog = NewCatalog();
        var grid = LoadedGrid(catalog, BinKind.BlankDie, 4, 4);
        Assert.False(grid.EditCell(9, 9, BinOf(catalog, BinKind.GoodDie)));
    }

    /* ---------------- 整行 / 整列（F-3.7.3-20） ---------------- */

    [Fact]
    public void EditRow_UpdatesEntireRow()
    {
        var catalog = NewCatalog();
        var grid = LoadedGrid(catalog, BinKind.BlankDie, 5, 5);
        var good = BinOf(catalog, BinKind.GoodDie);

        Assert.True(grid.EditRow(2, good));
        var rowCells = grid.Cells.Where(c => c.Row == 2).ToList();
        Assert.Equal(5, rowCells.Count);
        Assert.All(rowCells, c => Assert.Equal(BinKind.GoodDie, c.Kind));
        // 其余行不受影响
        Assert.All(grid.Cells.Where(c => c.Row != 2), c => Assert.Equal(BinKind.BlankDie, c.Kind));
    }

    [Fact]
    public void EditColumn_UpdatesEntireColumn()
    {
        var catalog = NewCatalog();
        var grid = LoadedGrid(catalog, BinKind.BlankDie, 5, 5);
        var refBin = BinOf(catalog, BinKind.ReferenceDie);

        Assert.True(grid.EditColumn(3, refBin));
        var colCells = grid.Cells.Where(c => c.Column == 3).ToList();
        Assert.Equal(5, colCells.Count);
        Assert.All(colCells, c => Assert.Equal(BinKind.ReferenceDie, c.Kind));
        Assert.All(grid.Cells.Where(c => c.Column != 3), c => Assert.Equal(BinKind.BlankDie, c.Kind));
    }

    [Fact]
    public void EditRow_WhenNoMap_ReturnsFalse()
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        Assert.False(grid.EditRow(0, BinOf(catalog, BinKind.GoodDie)));
        Assert.Contains("BR-45", grid.StatusText);
    }

    /* ---------------- 矩形批量（F-3.7.3-22） ---------------- */

    [Fact]
    public void EditRect_UpdatesNormalizedRectangularRegion()
    {
        var catalog = NewCatalog();
        var grid = LoadedGrid(catalog, BinKind.BlankDie, 6, 6);
        var defect = BinOf(catalog, BinKind.DefectDie);

        // 反向坐标也归一化：从 (4,4) 拖到 (1,1)
        Assert.True(grid.EditRect(4, 4, 1, 1, defect));
        var inRect = grid.Cells.Where(c => c.Column >= 1 && c.Column <= 4 && c.Row >= 1 && c.Row <= 4).ToList();
        Assert.Equal(16, inRect.Count);
        Assert.All(inRect, c => Assert.Equal(BinKind.DefectDie, c.Kind));
        Assert.All(grid.Cells.Where(c => c.Column < 1 || c.Column > 4 || c.Row < 1 || c.Row > 4),
            c => Assert.Equal(BinKind.BlankDie, c.Kind));
    }

    [Fact]
    public void EditRect_WhenNoMap_ReturnsFalse()
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        Assert.False(grid.EditRect(0, 0, 1, 1, BinOf(catalog, BinKind.GoodDie)));
        Assert.Contains("BR-45", grid.StatusText);
    }

    /* ---------------- 外扩 / 内缩（F-3.7.3-21/23，R-H05-05） ---------------- */

    [Fact]
    public void ExpandShrink_Expand_GrowsRegionByOneStep()   // 形态学膨胀，仅正交相邻
    {
        var catalog = NewCatalog();
        var grid = LoadedGrid(catalog, BinKind.BlankDie, 5, 5);
        var good = BinOf(catalog, BinKind.GoodDie);
        // 中心放一颗 GoodDie
        Assert.True(grid.EditCell(2, 2, good));

        Assert.True(grid.ExpandShrink("expand", good, BinOf(catalog, BinKind.BlankDie), 1));
        var goodCells = grid.Cells.Where(c => c.Kind == BinKind.GoodDie).ToList();
        // 中心 + 上下左右 4 个正交邻居 = 5（对角不计入）
        Assert.Equal(5, goodCells.Count);
        Assert.Contains(grid.Cells, c => c.Column == 2 && c.Row == 2 && c.Kind == BinKind.GoodDie);
        Assert.Contains(grid.Cells, c => c.Column == 1 && c.Row == 2 && c.Kind == BinKind.GoodDie);
        Assert.Contains(grid.Cells, c => c.Column == 3 && c.Row == 2 && c.Kind == BinKind.GoodDie);
        Assert.Contains(grid.Cells, c => c.Column == 2 && c.Row == 1 && c.Kind == BinKind.GoodDie);
        Assert.Contains(grid.Cells, c => c.Column == 2 && c.Row == 3 && c.Kind == BinKind.GoodDie);
    }

    [Fact]
    public void ExpandShrink_Expand_TwoSteps_FillsRadiusTwoDiamond()   // 4-连通膨胀 = 曼哈顿半径 2 的菱形（25 格网格中占 13 格，四角不可达）
    {
        var catalog = NewCatalog();
        var grid = LoadedGrid(catalog, BinKind.BlankDie, 5, 5);
        var good = BinOf(catalog, BinKind.GoodDie);
        Assert.True(grid.EditCell(2, 2, good));

        Assert.True(grid.ExpandShrink("expand", good, BinOf(catalog, BinKind.BlankDie), 2));
        // 单点 4-连通膨胀 2 步 = 半径 2 菱形：中心 1 + 半径1 的 4 + 半径2 的 8 = 13（四角曼哈顿距离 4，不可达）
        Assert.Equal(13, grid.Cells.Count(c => c.Kind == BinKind.GoodDie));
        // 四角确为不可达（仍空白）
        Assert.All(grid.Cells.Where(c => (c.Column == 0 || c.Column == 4) && (c.Row == 0 || c.Row == 4)),
            c => Assert.Equal(BinKind.BlankDie, c.Kind));
    }

    [Fact]
    public void ExpandShrink_Shrink_ErodesBorderLeavingCore()   // 形态学腐蚀：仅被 GoodDie 完全包围的中心存活
    {
        var catalog = NewCatalog();
        var grid = LoadedGrid(catalog, BinKind.BlankDie, 5, 5);
        var good = BinOf(catalog, BinKind.GoodDie);
        var blank = BinOf(catalog, BinKind.BlankDie);
        // 居中 3x3 实心 GoodDie，四周各留一圈 BlankDie（避免网格边界被当作「无邻居」而误留）
        Assert.True(grid.EditRect(1, 1, 3, 3, good));

        Assert.True(grid.ExpandShrink("shrink", good, blank, 1));
        // 3x3 外圈（有 BlankDie 邻居的）回退，仅中心 (2,2) 1x1 保留
        Assert.Equal(1, grid.Cells.Count(c => c.Kind == BinKind.GoodDie));
        Assert.Contains(grid.Cells, c => c.Column == 2 && c.Row == 2 && c.Kind == BinKind.GoodDie);
    }

    [Fact]
    public void ExpandShrink_WhenNoMap_ReturnsFalse()
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        var good = BinOf(catalog, BinKind.GoodDie);
        Assert.False(grid.ExpandShrink("expand", good, good, 1));
        Assert.Contains("BR-45", grid.StatusText);
    }

    /* ---------------- 直接编辑 vs H08 复判的语义边界 ---------------- */

    [Fact]
    [BusinessRule("BR-53")]
    public void DirectEdit_RefreshesImmediately_UnlikeReassign()   // BR-53 仅约束 H08 复判，不约束直接扫描编辑
    {
        var catalog = NewCatalog();
        var grid = LoadedGrid(catalog, BinKind.BlankDie, 6, 6);

        // 直接编辑（Wafer 扫描）：颜色即时更新，NeedsRefresh=false
        var good = BinOf(catalog, BinKind.GoodDie);
        Assert.True(grid.EditCell(0, 0, good));
        var edited = grid.Cells.First(c => c.Column == 0 && c.Row == 0);
        Assert.Equal(good.ColorHex, edited.ColorHex);
        Assert.False(grid.NeedsRefresh);

        // 复判（H08）：逻辑更新但颜色待刷新，NeedsRefresh=true（BR-53）
        var defect = BinOf(catalog, BinKind.DefectDie);
        Assert.True(grid.Reassign(1, 1, defect));
        var reassigned = grid.Cells.First(c => c.Column == 1 && c.Row == 1);
        Assert.Equal(BinKind.DefectDie, reassigned.Kind);
        Assert.NotEqual(defect.ColorHex, reassigned.ColorHex);   // 尚未刷新
        Assert.True(grid.NeedsRefresh);                          // BR-53 待显式刷新
    }
}
