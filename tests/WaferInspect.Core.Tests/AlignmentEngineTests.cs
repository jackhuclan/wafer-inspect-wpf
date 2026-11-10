using WaferInspect.Core.Alignment;
using WaferInspect.Core.Recipes;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>H03 对位建模与模板匹配（B-08 唯一实现、BR-21~34、T-024、F-3.2.2-40~67）。</summary>
public sealed class AlignmentEngineTests
{
    private readonly TemplateMatchingEngine _engine = new();
    private readonly LightCalibrationAnalyzer _light = new();
    private readonly MarkAlignmentService _marks = new();
    private readonly ReferenceImagePlanner _planner = new();

    /* ---------------- B-08 / BR-23 / BR-24：模板匹配引擎 ---------------- */

    [Fact]
    public void GrayMatch_OnDieFeature_ExceedsThreshold()
    {
        var image = SyntheticFovImage.Generate();
        // 框选一个含特征块的 die 中心区域（die 间距 40，特征块起点 16）。
        var region = new TemplateRegion(56, 56, 24, 24);

        var outcome = _engine.Match(image, region, TemplateType.Gray, 70);

        Assert.True(outcome.Passed, $"score={outcome.Score}");
        Assert.True(outcome.Score > 70);
        Assert.Empty(outcome.Advisories);
        Assert.Contains("B-08", outcome.EngineName);   // 全系统唯一实现标注
    }

    [Fact]
    public void GrayMatch_OnUniformDieBase_FailsWithNoContrastAdvisory()
    {
        var image = SyntheticFovImage.Generate();
        var region = new TemplateRegion(30, 30, 8, 8);   // 纯 die 基底（无特征、无切割道）→ 仅噪声 → 无对比

        var outcome = _engine.Match(image, region, TemplateType.Gray, 70);

        Assert.False(outcome.Passed);
        Assert.Equal(0, outcome.Score);
        Assert.Contains(outcome.Advisories, a => a.Contains("无对比"));
    }

    [Fact]
    public void ThresholdBoundary_HigherThreshold_FailsLowScoreMatch()
    {
        var image = SyntheticFovImage.Generate();
        var region = new TemplateRegion(56, 56, 24, 24);
        var outcome = _engine.Match(image, region, TemplateType.Gray, 70);
        double score = outcome.Score;

        // BR-24：分数严格大于阈值才成功。
        Assert.False(_engine.Match(image, region, TemplateType.Gray, score).Passed);
        Assert.True(_engine.Match(image, region, TemplateType.Gray, score - 0.5).Passed);
    }

    [Fact]
    public void ShapeMatch_OversizedRegion_ProducesBr23Advisory()
    {
        var image = SyntheticFovImage.Generate(480, 360);
        var region = new TemplateRegion(0, 0, 260, 200);   // 52000 / 172800 ≈ 30% > 25%

        var outcome = _engine.Match(image, region, TemplateType.Shape, 70);

        Assert.Contains(outcome.Advisories, a => a.Contains("不宜过大") && a.Contains("BR-23"));
    }

    [Fact]
    public void ShapeMatch_OnFeature_Passes_WithoutSizeAdvisory()
    {
        var image = SyntheticFovImage.Generate();
        var region = new TemplateRegion(56, 56, 24, 24);

        var outcome = _engine.Match(image, region, TemplateType.Shape, 70);

        Assert.True(outcome.Passed, $"score={outcome.Score}");
        Assert.DoesNotContain(outcome.Advisories, a => a.Contains("不宜过大"));
    }

    [Fact]
    public void Match_OutOfBoundsRegion_FailsExplicitly()
    {
        var image = SyntheticFovImage.Generate(100, 100);
        var outcome = _engine.Match(image, new TemplateRegion(90, 90, 30, 30), TemplateType.Gray, 70);
        Assert.False(outcome.Passed);
        Assert.Contains(outcome.Advisories, a => a.Contains("超出视窗图像边界"));
    }

    [Fact]
    public void Match_ConsumesLevelingConfig_SameOutcomeAsExplicitCall()
    {
        // B-08：向导步骤 2 的 LevelingConfig 与显式参数走同一引擎路径（AC-P2-04 取值口径一致）。
        var image = SyntheticFovImage.Generate();
        var region = new TemplateRegion(56, 56, 24, 24);
        var config = new LevelingConfig { TemplateType = TemplateType.Gray, MatchScore = 70 };

        var viaConfig = _engine.Match(image, region, config);
        var viaExplicit = _engine.Match(image, region, TemplateType.Gray, 70);

        Assert.Equal(viaExplicit.Score, viaConfig.Score);
        Assert.Equal(viaExplicit.Passed, viaConfig.Passed);
    }

    [Fact]
    public void SyntheticImage_IsDeterministic_SameSeedSamePixels()
    {
        var a = SyntheticFovImage.Generate(120, 120, seed: 7);
        var b = SyntheticFovImage.Generate(120, 120, seed: 7);
        Assert.Equal(a.Pixels, b.Pixels);
    }

    /* ---------------- BR-21：光照校准 ---------------- */

    [Fact]
    public void LightCalibration_TooDark_ProducesAdvisory()
    {
        var image = FlatImage(20);
        var report = _light.Analyze(image);
        Assert.False(report.IsOk);
        Assert.Contains(report.Advisories, a => a.Contains("过暗"));
    }

    [Fact]
    public void LightCalibration_TooBright_ProducesAdvisory()
    {
        var image = FlatImage(240);
        var report = _light.Analyze(image);
        Assert.False(report.IsOk);
        Assert.Contains(report.Advisories, a => a.Contains("过亮"));
    }

    [Fact]
    public void LightCalibration_SaturatedFraction_ProducesAdvisory()
    {
        var pixels = new byte[10 * 10];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = i < 40 ? (byte)255 : (byte)120;   // 40% 饱和但均值未过亮
        var image = new GrayImage(10, 10, pixels);

        var report = _light.Analyze(image);

        Assert.Contains(report.Advisories, a => a.Contains("饱和"));
    }

    [Fact]
    public void LightCalibration_NormalImage_IsOk()
    {
        var report = _light.Analyze(SyntheticFovImage.Generate());
        Assert.True(report.IsOk, string.Join("；", report.Advisories));
    }

    private static GrayImage FlatImage(byte value)
    {
        var pixels = new byte[10 * 10];
        Array.Fill(pixels, value);
        return new GrayImage(10, 10, pixels);
    }

    /* ---------------- BR-26 / T-024：Mark 对位 ---------------- */

    [Fact]
    public void MarkDirection_Horizontal_RequiresTwoSameRow()
    {
        var marks = new List<MarkPoint>
        {
            new(0, 1, 1, 0, 0), new(1, 1, 5, 0, 0), new(2, 8, 9, 0, 0)
        };
        Assert.Empty(_marks.Validate(marks, MarkAlignmentMethod.Horizontal));

        var invalid = new List<MarkPoint>
        {
            new(0, 1, 1, 0, 0), new(1, 2, 5, 0, 0), new(2, 8, 9, 0, 0)
        };
        var issues = _marks.Validate(invalid, MarkAlignmentMethod.Horizontal);
        Assert.Contains(issues, i => i.Message.Contains("同一行") && i.Message.Contains("BR-26"));
    }

    [Fact]
    public void MarkDirection_Vertical_RequiresTwoSameCol()
    {
        var marks = new List<MarkPoint>
        {
            new(0, 1, 1, 0, 0), new(1, 5, 1, 0, 0), new(2, 8, 9, 0, 0)
        };
        Assert.Empty(_marks.Validate(marks, MarkAlignmentMethod.Vertical));

        var invalid = new List<MarkPoint>
        {
            new(0, 1, 1, 0, 0), new(1, 5, 2, 0, 0), new(2, 8, 9, 0, 0)
        };
        Assert.Contains(_marks.Validate(invalid, MarkAlignmentMethod.Vertical), i => i.Message.Contains("同一列"));
    }

    [Fact]
    public void MarkDirection_ThreePointAndTwoPlusOne_ArePreservedLevelC()
    {
        // 三点对位 / 2+1 对位为「推荐忽略」，级 C 保留：至少 2 点同行或同列即可。
        var marks = new List<MarkPoint>
        {
            new(0, 1, 1, 0, 0), new(1, 5, 1, 0, 0), new(2, 8, 9, 0, 0)
        };
        Assert.Empty(_marks.Validate(marks, MarkAlignmentMethod.ThreePoint));
        Assert.Empty(_marks.Validate(marks, MarkAlignmentMethod.TwoPlusOne));

        Assert.Equal("三点对位（推荐忽略，级 C 保留）", MarkAlignmentMethod.ThreePoint.Text());
        Assert.Equal("2+1 对位（推荐忽略，级 C 保留）", MarkAlignmentMethod.TwoPlusOne.Text());
    }

    [Fact]
    public void MarkReset_WithoutConfirmation_Throws_T024()
    {
        var marks = new List<MarkPoint> { new(0, 1, 1, 0, 0) };
        Assert.Throws<InvalidOperationException>(() => _marks.ResetMarks(marks, confirmed: false));
    }

    [Fact]
    public void MarkReset_WithConfirmation_ClearsAll_T024()
    {
        var marks = new List<MarkPoint> { new(0, 1, 1, 0, 0), new(1, 2, 2, 0, 0) };
        Assert.Empty(_marks.ResetMarks(marks, confirmed: true));
    }

    [Fact]
    public void DieToPlatform_MapsGridOffsetIntoMm()
    {
        var grid = new MapGridState();
        grid.Load(columns: 10, rows: 10);
        grid.Move(dx: 5, dy: -3);

        var point = _marks.DieToPlatform(row: 2, col: 3, dieXmm: 10, dieYmm: 8, grid);

        Assert.Equal(3 * 10 + 5 * 1.0, point.Xmm, 3);   // 偏移比例尺 = dieX / columns = 1mm/px
        Assert.Equal(2 * 8 + (-3) * 0.8, point.Ymm, 3);
    }

    [Fact]
    public void MapGrid_LockedAfterConfirm_CannotMove()
    {
        var grid = new MapGridState();
        Assert.Throws<InvalidOperationException>(() => grid.Move(1, 1));   // 未加载不可移动

        grid.Load(10, 10);
        grid.Move(2, 2);
        grid.ConfirmLocation();
        Assert.True(grid.IsLocked);

        Assert.Throws<InvalidOperationException>(() => grid.Move(1, 1));   // F-3.2.2-46：锁定后不可再移动

        grid.Reset();
        Assert.False(grid.IsLoaded);   // F-3.2.2-47：重置删除 Map 图
    }

    [Fact]
    public void ThumbnailRatio_MustBeOneOverPowerOfTwo_Br27()
    {
        Assert.True(ThumbnailRules.IsPowerOfTwoRatio(0.25));   // 1/4
        Assert.True(ThumbnailRules.IsPowerOfTwoRatio(0.5));    // 1/2
        Assert.True(ThumbnailRules.IsPowerOfTwoRatio(1));      // 1/1
        Assert.False(ThumbnailRules.IsPowerOfTwoRatio(0.3));
        Assert.False(ThumbnailRules.IsPowerOfTwoRatio(0));
        Assert.False(ThumbnailRules.IsPowerOfTwoRatio(1.5));
    }

    /* ---------------- F-3.2.2-60~67 / BR-29/30：参考图像 ---------------- */

    [Fact]
    public void ReferencePlanner_ClassifiesCells_F322_60()
    {
        var plan = _planner.Plan(cols: 10, rows: 10, referenceDieCount: 8, fovZoom: 1);

        Assert.Equal(8, plan.GreenCount);
        Assert.Equal(8, plan.BlueCount);
        Assert.Equal(0, plan.RedCount);            // 参考 Die 可能没有
        Assert.Equal(100 - 8, plan.YellowCount);
        Assert.Equal(8, plan.ReferenceDies.Count);
    }

    [Fact]
    public void ReferencePlanner_CostIndexIncreasesWithCount_Br29()
    {
        var small = _planner.Plan(10, 10, 3, 1);
        var large = _planner.Plan(10, 10, 20, 1);
        Assert.True(large.EstimatedCostIndex > small.EstimatedCostIndex);
    }

    [Fact]
    public void ReferencePlanner_FovZoomChange_NeedsRegeneration_Br30()
    {
        var plan = _planner.Plan(10, 10, 5, fovZoom: 1);
        Assert.False(plan.NeedsRegeneration);

        var changed = _planner.ChangeFovZoom(plan, newFovZoom: 2);
        Assert.True(changed.NeedsRegeneration);

        var same = _planner.ChangeFovZoom(plan, newFovZoom: 1);
        Assert.False(same.NeedsRegeneration);
    }

    [Fact]
    public void ReferencePlanner_CountExceedingDies_FailsExplicitly()
    {
        Assert.Throws<InvalidOperationException>(() => _planner.Plan(5, 5, referenceDieCount: 30, fovZoom: 1));
    }

    [Fact]
    public void ReferencePlanner_ManualAdjustKeepsPoints_F322_64()
    {
        var plan = _planner.Plan(10, 10, 4, 1);
        var adjusted = _planner.MarkManualAdjusted(plan);
        Assert.True(adjusted.ManualAdjusted);
        Assert.Equal(4, adjusted.ReferenceDies.Count);   // 手动调整不删除已规划点位
    }

    [Fact]
    public void StreetCenter_LocatesDieTopLeft_Br31()
    {
        var (x, y) = _marks.LocateDieStreetCenter(row: 3, col: 4, dieXmm: 9.8, dieYmm: 12.6);
        Assert.Equal(4 * 9.8, x, 3);
        Assert.Equal(3 * 12.6, y, 3);
    }
}
