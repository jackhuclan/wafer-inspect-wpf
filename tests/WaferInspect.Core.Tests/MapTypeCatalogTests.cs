#nullable enable
using WaferInspect.Core.Maps;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P2 增量 5（H05）：BR-43~45 / BR-51~53、AC-P2-06、AC-H05-01/02/04/05、
/// B-11 单一数据源与 T-043 删除保护 / 引用检查。
/// Wafer 扫描 12 步（P4）与 Sinf 生成（P3）不在本测试范围，界面已显式登记。
/// </summary>
public sealed class MapTypeCatalogTests
{
    private static EditableMapTypeCatalog NewCatalog(Func<IReadOnlyList<string>>? references = null)
        => new(references);

    private static BinDefinition BinOf(EditableMapTypeCatalog catalog, string typeId, BinKind kind)
        => catalog.Find(typeId)!.Bins.First(b => b.Kind == kind);

    /* ---------------- BR-43：8 类固定枚举 ---------------- */

    [Fact]
    [BusinessRule("BR-43")]
    public void FixedKinds_AreExactlyTheEightHistoricalTypesIncludingForBiddenDie()
    {
        Assert.Equal(
            new[] { "GoodDie", "DefectDie", "ReferenceDie", "OriginalDie", "BlankDie", "ForBiddenDie", "MatchFailDie", "ExceptionDie" },
            BinKindCatalog.FixedKinds.Select(k => k.ToString()));

        var catalog = NewCatalog();
        Assert.Equal(8, catalog.Find("MAP-PL-300")!.Bins.Count);
        Assert.Empty(catalog.AvailableKinds("MAP-PL-300"));
    }

    [Fact]
    [BusinessRule("BR-43")]
    public void AddBin_OnlyEnablesFixedKinds_AndRejectsDuplicatesOrNinthType()
    {
        var catalog = NewCatalog();
        catalog.RemoveBin("MAP-PL-300", BinOf(catalog, "MAP-PL-300", BinKind.ExceptionDie).Index);

        Assert.Equal(new[] { BinKind.ExceptionDie }, catalog.AvailableKinds("MAP-PL-300"));
        var added = catalog.AddBin("MAP-PL-300", BinKind.ExceptionDie);
        Assert.Equal("ED", added.Code);
        Assert.Equal(8, catalog.Find("MAP-PL-300")!.Bins.Count);

        Assert.Throws<InvalidOperationException>(() => catalog.AddBin("MAP-PL-300", BinKind.ExceptionDie));
        Assert.Throws<InvalidOperationException>(() => catalog.AddBin("MAP-PL-300", (BinKind)99));
    }

    /* ---------------- BR-44 / F-3.7.2-05~10：可编辑字段与校验 ---------------- */

    [Fact]
    [BusinessRule("BR-44")]
    public void AllEightBins_AreEditable_CodeLevelColorShortcutNoteQuantity()
    {
        var catalog = NewCatalog();
        foreach (var kind in BinKindCatalog.FixedKinds)
        {
            var bin = BinOf(catalog, "MAP-PL-300", kind);
            var edited = bin with
            {
                Code = $"X-{kind}", Name = $"{kind.Text()}-改", Level = 2,
                ColorHex = "#123456", Shortcut = $"K-{kind}", Note = "备注", Quantity = 7
            };
            catalog.UpdateBin("MAP-PL-300", bin.Index, edited);

            var saved = BinOf(catalog, "MAP-PL-300", kind);
            Assert.Equal(edited.Code, saved.Code);
            Assert.Equal(edited.Name, saved.Name);
            Assert.Equal(2, saved.Level);
            Assert.Equal("#123456", saved.ColorHex);
            Assert.Equal(edited.Shortcut, saved.Shortcut);
            Assert.Equal("备注", saved.Note);
            Assert.Equal(7, saved.Quantity);
        }
    }

    [Fact]
    [BusinessRule("BR-44")]
    public void DuplicateCodeOrShortcut_IsRejected()
    {
        var catalog = NewCatalog();
        var good = BinOf(catalog, "MAP-PL-300", BinKind.GoodDie);
        var defect = BinOf(catalog, "MAP-PL-300", BinKind.DefectDie);

        Assert.Throws<InvalidOperationException>(() =>
            catalog.UpdateBin("MAP-PL-300", defect.Index, defect with { Code = good.Code }));
        Assert.Throws<InvalidOperationException>(() =>
            catalog.UpdateBin("MAP-PL-300", defect.Index, defect with { Shortcut = good.Shortcut }));
    }

    [Fact]
    public void InvalidLevelOrColor_IsRejected()
    {
        var catalog = NewCatalog();
        var bin = BinOf(catalog, "MAP-PL-300", BinKind.GoodDie);
        Assert.Throws<InvalidOperationException>(() =>
            catalog.UpdateBin("MAP-PL-300", bin.Index, bin with { Level = 9 }));
        Assert.Throws<InvalidOperationException>(() =>
            catalog.UpdateBin("MAP-PL-300", bin.Index, bin with { ColorHex = "blue" }));
    }

    /* ---------------- AC-P2-06 / AC-H05-02：快捷键 ---------------- */

    [Fact]
    public void Shortcut_EditTakesEffectImmediately_InReviewLookup()
    {
        var catalog = NewCatalog();
        var bin = BinOf(catalog, "MAP-PL-300", BinKind.GoodDie);
        Assert.Equal(bin.Code, catalog.ResolveShortcut("MAP-PL-300", "Space")?.Code);

        catalog.UpdateBin("MAP-PL-300", bin.Index, bin with { Shortcut = "G" });
        Assert.Null(catalog.ResolveShortcut("MAP-PL-300", "Space"));
        Assert.Equal("GD", catalog.ResolveShortcut("MAP-PL-300", "G")?.Code);
        Assert.Equal("GD", catalog.ResolveShortcut("MAP-PL-300", "g")?.Code);   // 大小写不敏感
    }

    /* ---------------- AC-H05-01 / B-11：消费方同步 ---------------- */

    [Fact]
    public void Changed_IsRaisedForEveryEdit_SoAllConsumersCanRefresh()
    {
        var catalog = NewCatalog();
        int count = 0;
        catalog.Changed += (_, _) => count++;

        var created = catalog.Create("新图谱");
        var exception = BinOf(catalog, created.Id, BinKind.ExceptionDie);
        catalog.RemoveBin(created.Id, exception.Index);     // 新建图谱默认含 8 类，先移除才有空位
        catalog.AddBin(created.Id, BinKind.ExceptionDie);
        count = 0;
        catalog.Rename(created.Id, "新图谱-改");
        catalog.Refresh();
        Assert.Equal(2, count);
    }

    [Fact]
    public void BinColorEdit_SyncsH01LevelColor_AndMapPreview()
    {
        var catalog = NewCatalog();
        string before = catalog.ColorForLevel("PL", 0);
        var bin = BinOf(catalog, "MAP-PL-300", BinKind.GoodDie);
        catalog.UpdateBin("MAP-PL-300", bin.Index, bin with { ColorHex = "#FF00AA" });

        Assert.NotEqual(before, catalog.ColorForLevel("PL", 0));
        Assert.Equal("#FF00AA", catalog.ColorForLevel("PL", 0));
        Assert.Equal("#FF00AA", catalog.ColorForLevel("EL", 0));
        Assert.Equal("#FF00AA", catalog.ColorForLevel("AOI", 0));

        var grid = new MapGridModel();
        Assert.True(grid.Generate("MAP-PL-300", catalog));
        Assert.Contains(grid.Cells, c => c.ColorHex == "#FF00AA");
    }

    [Fact]
    public void MapTypeRename_IsVisibleToH02DropdownWithoutDuplicatingDefinition()
    {
        var catalog = NewCatalog();
        catalog.Rename("MAP-PL-300", "PL 标准图谱 · 已改名");
        Assert.Equal("PL 标准图谱 · 已改名", catalog.Types.First(t => t.Id == "MAP-PL-300").Name);
        Assert.Equal(2, catalog.Types.Count);
    }

    /* ---------------- T-043：多套并存 / 删除保护 / 引用检查 ---------------- */

    [Fact]
    public void MultipleTypes_Coexist_AndDeleteIsProtectedByReferences()
    {
        var catalog = NewCatalog(() => ["MAP-PL-300"]);
        var created = catalog.Create("第三套图谱");
        Assert.Equal(3, catalog.Types.Count);

        Assert.Throws<InvalidOperationException>(() => catalog.Delete("MAP-PL-300"));
        catalog.Delete(created.Id);          // 未被引用，可删除
        Assert.Equal(2, catalog.Types.Count);

        var copy = catalog.Duplicate("MAP-EL-200");
        Assert.Contains(catalog.Types, t => t.Id == "MAP-PL-300");   // 被引用，删除被拒绝后仍存在
        Assert.Contains(catalog.Types, t => t.Id == copy.Id);
    }

    [Fact]
    public void Delete_LastType_IsRejected()
    {
        var catalog = NewCatalog();
        catalog.Delete("MAP-EL-200");
        Assert.Single(catalog.Types);
        Assert.Throws<InvalidOperationException>(() => catalog.Delete("MAP-PL-300"));
    }

    /* ---------------- BR-45 / BR-51~53：Map 网格唯一模型 ---------------- */

    [Fact]
    [BusinessRule("BR-45")]
    public void GenerateMap_RequiresImportedMapFile()
    {
        var catalog = NewCatalog();
        var created = catalog.Create("无图谱文件");     // 新建类型 MapFilePath 为空
        var grid = new MapGridModel();
        Assert.False(grid.Generate(created.Id, catalog));
        Assert.Contains("BR-45", grid.StatusText);
        Assert.Empty(grid.Cells);

        catalog.Update(created with { MapFilePath = "assets/map-samples/aoi-finescan-200mm.csv" });
        Assert.True(grid.Generate(created.Id, catalog));
        Assert.Equal(400, grid.Cells.Count);
    }

    [Fact]
    [BusinessRule("BR-51")]
    public void CoordinateFormatting_IsTheSingleRule_ForDisplayAndDieNumbers()
    {
        Assert.Equal("[3,7]", MapGridModel.FormatCoordinate(3, 7));
        Assert.Equal("[3,7]", MapGridModel.FormatDieCoordinate(4, 8));   // 1-based → 0-based
        Assert.True(MapGridModel.TryParseCoordinate("[3,7]", out int column, out int row));
        Assert.Equal(3, column);
        Assert.Equal(7, row);
        Assert.False(MapGridModel.TryParseCoordinate("3-7", out _, out _));
    }

    [Fact]
    [BusinessRule("BR-52")]
    public void JumpTo_UsesXAsColumnAndYAsRow_AndRejectsOutOfRange()
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        Assert.True(grid.Generate("MAP-PL-300", catalog));

        Assert.True(grid.JumpTo(3, 7));
        Assert.Equal("[3,7]", grid.Selected?.Coordinate);
        Assert.False(grid.JumpTo(999, 0));
    }

    [Fact]
    [BusinessRule("BR-53")]
    public void AfterReviewMarkDirty_ColorDoesNotChangeUntilExplicitRefresh()
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        Assert.True(grid.Generate("MAP-PL-300", catalog, 4, 4));
        string before = string.Join(",", grid.Cells.Select(c => c.ColorHex));

        // 复判完成 → 数据变化，但不自动刷新（颜色不变）
        grid.MarkDirty();
        Assert.True(grid.NeedsRefresh);
        Assert.Equal(before, string.Join(",", grid.Cells.Select(c => c.ColorHex)));

        // 修改 Bin 颜色后，未点刷新仍不变
        var bin = BinOf(catalog, "MAP-PL-300", BinKind.GoodDie);
        catalog.UpdateBin("MAP-PL-300", bin.Index, bin with { ColorHex = "#ABCDEF" });
        Assert.Equal(before, string.Join(",", grid.Cells.Select(c => c.ColorHex)));

        Assert.True(grid.Refresh("MAP-PL-300", catalog));
        Assert.False(grid.NeedsRefresh);
        Assert.Contains(grid.Cells, c => c.ColorHex == "#ABCDEF");
    }

    [Fact]
    public void MapGridModel_Changed_NotifiesSharedCanvas()
    {
        var catalog = NewCatalog();
        var grid = new MapGridModel();
        int raised = 0;
        grid.Changed += (_, _) => raised++;

        grid.Generate("MAP-PL-300", catalog);
        grid.Select(1, 1);
        grid.Refresh("MAP-PL-300", catalog);
        Assert.Equal(3, raised);
    }

    [Fact]
    public void DefaultPalette_KeepsHistoricalHomepageColors()
    {
        var catalog = NewCatalog();
        Assert.Equal("#E9F1FD", MapLevelPalette.ColorFor("PL", 0));
        Assert.Equal("#E7F4F1", MapLevelPalette.ColorFor("EL", 0));
        Assert.Equal("#ECEFFB", MapLevelPalette.ColorFor("AOI", 0));
        Assert.Equal(MapLevelPalette.ColorFor("PL", 3), catalog.ColorForLevel("PL", 3));
    }
}
