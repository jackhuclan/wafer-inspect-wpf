#nullable enable
using System.Collections.Generic;
using System.Linq;
using WaferInspect.Core.Maps;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P3 增量 6（H08 复判全景 Map）：<see cref="MapGridModel.LoadWafer"/> / <see cref="MapGridModel.Reassign"/> /
/// <see cref="MapGridModel.RecolorWafer"/> 复用 H05 唯一绘制模型，颜色 / 代码 / 等级唯一来自
/// <see cref="IMapTypeCatalog"/>（B-11）；复判后颜色**不自动更新**（BR-53），必须显式刷新。
/// </summary>
public sealed class MapGridModelRecheckTests
{
    private static EditableMapTypeCatalog NewCatalog() => new();

    private static BinDefinition BinOf(EditableMapTypeCatalog catalog, BinKind kind)
        => catalog.Find("MAP-PL-300")!.Bins.First(b => b.Kind == kind);

    [Fact]
    public void LoadWafer_BuildsCellsFromMatrix_WithColorsFromCatalog()   // B-11 单一数据源
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        const int rows = 6, cols = 6;
        var matrix = Enumerable.Repeat(BinKind.GoodDie, rows * cols).ToList();
        matrix[5 * cols + 5] = BinKind.DefectDie;

        Assert.True(grid.LoadWafer("MAP-PL-300", catalog, matrix, rows, cols));
        Assert.Equal(rows * cols, grid.Cells.Count);

        var defect = BinOf(catalog, BinKind.DefectDie);
        var loaded = grid.Cells.First(c => c.Column == 5 && c.Row == 5);
        Assert.Equal(BinKind.DefectDie, loaded.Kind);
        Assert.Equal(defect.ColorHex, loaded.ColorHex);   // 颜色来自图谱（B-11），非本地写死
    }

    [Fact]
    public void LoadWafer_WrongMatrixLength_ReturnsFalse()
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        var matrix = new List<BinKind> { BinKind.GoodDie, BinKind.DefectDie };
        Assert.False(grid.LoadWafer("MAP-PL-300", catalog, matrix, 5, 5));
        Assert.Contains("不一致", grid.StatusText);
    }

    [Fact]
    [BusinessRule("BR-53")]
    public void Reassign_KeepsOldColor_UntilExplicitRecolor()   // BR-53：复判后不自动刷新颜色
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        const int rows = 6, cols = 6;
        var matrix = Enumerable.Repeat(BinKind.GoodDie, rows * cols).ToList();
        matrix[5 * cols + 5] = BinKind.DefectDie;
        Assert.True(grid.LoadWafer("MAP-PL-300", catalog, matrix, rows, cols));

        var cellBefore = grid.Cells.First(c => c.Column == 5 && c.Row == 5);
        string oldColor = cellBefore.ColorHex;                       // DefectDie 的颜色
        Assert.Equal(BinOf(catalog, BinKind.DefectDie).ColorHex, oldColor);

        var goodBin = BinOf(catalog, BinKind.GoodDie);
        Assert.True(grid.Reassign(5, 5, goodBin));

        var cellAfter = grid.Cells.First(c => c.Column == 5 && c.Row == 5);
        Assert.Equal(BinKind.GoodDie, cellAfter.Kind);              // 逻辑已更新
        Assert.Equal(oldColor, cellAfter.ColorHex);                 // 颜色未变（BR-53）
        Assert.True(grid.NeedsRefresh);

        // 显式刷新后才更新颜色
        int v = grid.RefreshVersion;
        Assert.True(grid.RecolorWafer("MAP-PL-300", catalog));
        var cellFinal = grid.Cells.First(c => c.Column == 5 && c.Row == 5);
        Assert.Equal(goodBin.ColorHex, cellFinal.ColorHex);         // 取色自图谱（B-11）
        Assert.Equal(v + 1, grid.RefreshVersion);
        Assert.False(grid.NeedsRefresh);
    }

    [Fact]
    [BusinessRule("BR-53")]
    public void RecolorWafer_IsOnlyRefreshEntry_AfterReassign()
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        const int rows = 4, cols = 4;
        var matrix = Enumerable.Repeat(BinKind.ReferenceDie, rows * cols).ToList();
        Assert.True(grid.LoadWafer("MAP-PL-300", catalog, matrix, rows, cols));
        int baseVersion = grid.RefreshVersion;

        var goodBin = BinOf(catalog, BinKind.GoodDie);
        Assert.True(grid.Reassign(2, 2, goodBin));
        // 未刷新时整张图仍为 ReferenceDie 颜色
        Assert.All(grid.Cells, c => Assert.Equal(BinOf(catalog, BinKind.ReferenceDie).ColorHex, c.ColorHex));

        Assert.True(grid.RecolorWafer("MAP-PL-300", catalog));
        Assert.Equal(baseVersion + 1, grid.RefreshVersion);
        var center = grid.Cells.First(c => c.Column == 2 && c.Row == 2);
        Assert.Equal(goodBin.ColorHex, center.ColorHex);
    }

    [Fact]
    public void RecolorWafer_WithoutLoadedMap_ReturnsFalse()
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        Assert.False(grid.RecolorWafer("MAP-PL-300", catalog));
        Assert.Contains("请先载入", grid.StatusText);
    }
}
