#nullable enable
using WaferInspect.Core.Inspection;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Recipes;
using WaferInspect.Core.Run;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P2 增量 4（H04）：ROI / 扫描区域唯一数据源（B-01/B-02、AC-P2-03、AC-H04-01~04）、
/// BR-35~42、BR-40 关闭自动保存（AC-P2-05）、T-014 可扩展参数集合与 B-23 统一缺陷模型。
/// 设备采集与 AI 推理未接入（T-029 / T-035），断言其结果**显式标注搁置**，不把离线演算冒充设备返回。
/// </summary>
public sealed class RoiScanAreaTests
{
    private static (IRoiScanAreaStore Store, InMemorySubRecipeRepository Subs, string RecipeId) NewStore()
    {
        var jobs = new InMemoryJobRepository(new SimulatedMapTypeCatalog());
        var subs = new InMemorySubRecipeRepository(jobs);
        string id = subs.Create(jobs.Jobs[0].JobId, "SR-H04").Id;
        return (new RoiScanAreaStore(subs), subs, id);
    }

    private static DetectionItem SavedItem(string name = "Zone-A") => new()
    {
        Name = name,
        Kind = RoiTargetKind.Zone,
        AlgorithmType = InspectionText.DefaultParameterType,
        GoldenTemplateRegion = new MapRect(1, 1, 2, 2),
        ParameterType = InspectionText.DefaultParameterType,
        Extraction = RegionExtraction.TemplateMatching,
        Saved = true
    };

    /* ---------------- BR-37 / BR-39：默认全片与区域增删 ---------------- */

    [Fact]
    [BusinessRule("BR-37")]
    public void Snapshot_FirstLoad_IsFullWaferByDefault()
    {
        var (store, _, id) = NewStore();
        var snapshot = store.Snapshot(id);

        Assert.True(snapshot.FullWafer);
        Assert.Empty(snapshot.ScanRegions);
        Assert.Empty(snapshot.Zones);
        Assert.Empty(snapshot.DetectionItems);
        Assert.Equal(20 * 20, store.DiesInScanArea(id, 20, 20).Count);
    }

    [Fact]
    [BusinessRule("BR-39")]
    public void AddScanRegion_SupportsMultipleAndSwitchesOffFullWafer()
    {
        var (store, _, id) = NewStore();
        store.AddScanRegion(id, new MapRect(1, 1, 3, 2));
        store.AddScanRegion(id, new MapRect(8, 6, 2, 2));

        var snapshot = store.Snapshot(id);
        Assert.Equal(2, snapshot.ScanRegions.Count);
        Assert.False(snapshot.FullWafer);
    }

    [Fact]
    [BusinessRule("BR-39")]
    public void ClearScanRegions_ClearsAllAndRestoresBr37Default()
    {
        var (store, _, id) = NewStore();
        store.AddScanRegion(id, new MapRect(1, 1, 3, 2));
        store.AddScanRegion(id, new MapRect(8, 6, 2, 2));
        store.ClearScanRegions(id);

        Assert.Empty(store.Snapshot(id).ScanRegions);
        Assert.True(store.Snapshot(id).FullWafer);
        Assert.Equal(400, store.DiesInScanArea(id, 20, 20).Count);
    }

    [Fact]
    public void AddScanRegion_RejectsZeroBasedOrNonPositiveRect()
    {
        var (store, _, id) = NewStore();
        Assert.Throws<ArgumentOutOfRangeException>(() => store.AddScanRegion(id, new MapRect(0, 1, 2, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.AddScanRegion(id, new MapRect(1, 1, 0, 2)));
        Assert.Empty(store.Snapshot(id).ScanRegions);
    }

    /* ---------------- BR-41 / AC-H04-04：预扫描仅覆盖框选区域 ---------------- */

    [Fact]
    [BusinessRule("BR-41")]
    public void DiesInScanArea_LocalRegion_CoversOnlyBoxedDies()
    {
        var (store, _, id) = NewStore();
        store.AddScanRegion(id, new MapRect(3, 4, 2, 3));   // 列 3~4，行 4~6

        var dies = store.DiesInScanArea(id, 20, 20);
        Assert.Equal(6, dies.Count);
        Assert.Contains(new DieCoordinate(4, 3), dies);
        Assert.Contains(new DieCoordinate(6, 4), dies);
        Assert.DoesNotContain(new DieCoordinate(3, 3), dies);
        Assert.DoesNotContain(new DieCoordinate(7, 3), dies);
    }

    [Fact]
    [BusinessRule("BR-41")]
    public void Prescan_OnlyEvaluatesBoxedRegion()
    {
        var (store, _, id) = NewStore();
        store.AddScanRegion(id, new MapRect(2, 2, 3, 3));
        var outcome = store.Prescan(id, 20, 20);

        Assert.Equal(VerificationScope.Prescan, outcome.Scope);
        Assert.Equal(9, outcome.CoveredDies.Count);
        Assert.All(outcome.Defects, d => Assert.InRange(d.Row, 2, 4));
        Assert.All(outcome.Defects, d => Assert.InRange(d.Column, 2, 4));
    }

    /* ---------------- BR-38：抽检可设多个 Die ---------------- */

    [Fact]
    [BusinessRule("BR-38")]
    public void SamplingDies_SupportMultipleAndToggleOff()
    {
        var (store, _, id) = NewStore();
        store.ToggleSamplingDie(id, 2, 3);
        store.ToggleSamplingDie(id, 7, 9);
        Assert.Equal(2, store.Snapshot(id).SamplingDies.Count);
        Assert.True(store.Snapshot(id).SamplingEnabled);
        Assert.False(store.Snapshot(id).FullWafer);

        store.ToggleSamplingDie(id, 2, 3);   // 重复双击取消
        Assert.Single(store.Snapshot(id).SamplingDies);
        Assert.Equal(new DieCoordinate(7, 9), store.DiesInScanArea(id, 20, 20)[0]);
    }

    [Fact]
    [BusinessRule("BR-38")]
    public void ClearSamplingDies_LeavesNoCoveredDieInSamplingMode()
    {
        var (store, _, id) = NewStore();
        store.ToggleSamplingDie(id, 2, 3);
        store.ClearSamplingDies(id);
        Assert.Empty(store.Snapshot(id).SamplingDies);
        Assert.Empty(store.DiesInScanArea(id, 20, 20));
    }

    /* ---------------- AC-P2-03 / AC-H04-01：唯一数据源 ---------------- */

    [Fact]
    public void Changed_BroadcastsRecipeId_ForEveryEdit()
    {
        var (store, _, id) = NewStore();
        var seen = new List<string>();
        store.Changed += (_, changedId) => seen.Add(changedId);

        store.AddRoiZone(id, new MapRect(1, 1, 2, 2));
        store.AddScanRegion(id, new MapRect(4, 4, 2, 2));
        store.SetParameter(id, "Detection.Threshold", "150");

        Assert.Equal(new[] { id, id, id }, seen);
    }

    [Fact]
    public void SingleDataSource_AllConsumersSeeTheSameRegion()
    {
        // 模拟「配方向导步骤 6」与「参数验证页」两个消费者：两者读同一接口实例，不各自持有副本。
        var (store, _, id) = NewStore();
        IRoiScanAreaStore wizardConsumer = store;
        IRoiScanAreaStore validationConsumer = store;

        wizardConsumer.AddScanRegion(id, new MapRect(5, 5, 4, 3), "向导步骤 6 · Map 圈选");
        var fromValidation = validationConsumer.Snapshot(id);

        Assert.Single(fromValidation.ScanRegions);
        Assert.Equal("向导步骤 6 · Map 圈选", fromValidation.ScanRegions[0].Source);
        Assert.Equal(new MapRect(5, 5, 4, 3), fromValidation.ScanRegions[0].Rect);
        Assert.False(fromValidation.FullWafer);
        Assert.Equal(12, validationConsumer.DiesInScanArea(id, 20, 20).Count);
    }

    [Fact]
    public void ApplyTo_WritesOneRecipeProjectionForAllConsumers()
    {
        var (store, _, id) = NewStore();
        store.AddRoiZone(id, new MapRect(2, 3, 3, 2));
        store.AddScanRegion(id, new MapRect(2, 3, 3, 2));
        store.SaveDetectionItem(id, SavedItem());
        var recipe = new SubRecipe { Id = id };

        store.ApplyTo(id, recipe);

        Assert.Single(recipe.RoiZones);
        Assert.False(recipe.ScanArea.FullWafer);
        Assert.Single(recipe.Inspection.ScanRegions);
        Assert.Single(recipe.Inspection.DetectionItems);
        Assert.Equal(recipe.RoiZones.Select(z => z.Index), recipe.ScanArea.RoiIndexes);
    }

    /* ---------------- BR-40 / AC-P2-05 / AC-H04-03：关闭自动保存 ---------------- */

    [Fact]
    [BusinessRule("BR-40")]
    public void Save_PersistsParametersAndIncrementsVersion()
    {
        var (store, subs, id) = NewStore();
        store.SetParameter(id, "Detection.Threshold", "199");
        store.AddParameter(id, new AlgorithmParameter("Custom.Gain", "检测算法", "3.5", "dB"));
        store.AddScanRegion(id, new MapRect(1, 1, 2, 2));

        Assert.True(store.Save(id));
        var saved = subs.Find(id);
        Assert.NotNull(saved);
        Assert.Equal(1, saved!.Inspection.SavedVersion);
        Assert.NotNull(saved.Inspection.LastSavedAt);
        Assert.Equal("199", saved.Inspection.Parameters.First(p => p.Key == "Detection.Threshold").Value);
        Assert.Contains(saved.Inspection.Parameters, p => p.Key == "Custom.Gain");
        Assert.Single(saved.Inspection.ScanRegions);
    }

    [Fact]
    [BusinessRule("BR-40")]
    public void ReloadFromRecipe_KeepsParametersAfterClosingPage()
    {
        // AC-H04-03：关闭后重进参数保持（新 store 实例模拟重新进入参数页）。
        var (store, subs, id) = NewStore();
        store.SetParameter(id, "Global.ParameterType", "Edge");
        store.AddScanRegion(id, new MapRect(3, 3, 2, 2));
        Assert.True(store.Save(id));

        var reopened = new RoiScanAreaStore(subs);
        var snapshot = reopened.Snapshot(id);

        Assert.Equal("Edge", AlgorithmParameterCatalog.ParameterTypeOf(snapshot.Parameters));
        Assert.Single(snapshot.ScanRegions);
        Assert.Equal(1, snapshot.SavedVersion);
        Assert.Equal(4, reopened.DiesInScanArea(id, 20, 20).Count);
    }

    [Fact]
    public void Save_ReturnsFalseWhenRecipeIsMissing_NoFakeSuccess()
    {
        var (store, _, _) = NewStore();
        Assert.False(store.Save("SUB-DOES-NOT-EXIST"));
    }

    /* ---------------- BR-35 / BR-36 / T-014：参数集合与检测项 ---------------- */

    [Fact]
    [BusinessRule("BR-35")]
    public void ParameterType_DefaultsToSurfaceAndIsEditable()
    {
        var (store, _, id) = NewStore();
        Assert.Equal("Surface", AlgorithmParameterCatalog.ParameterTypeOf(store.Snapshot(id).Parameters));
        store.SetParameter(id, "Global.ParameterType", "Other");
        Assert.Equal("Other", AlgorithmParameterCatalog.ParameterTypeOf(store.Snapshot(id).Parameters));
    }

    [Fact]
    public void DefaultParameterSet_IsExtensibleAndGrouped()
    {
        var defaults = AlgorithmParameterCatalog.Defaults;
        Assert.True(defaults.Count >= 9);
        Assert.Contains(defaults, p => p.Group == AlgorithmParameterCatalog.DetectionGroup);
        Assert.Contains(defaults, p => p.Group == AlgorithmParameterCatalog.MetrologyGroup);
        Assert.Contains(defaults, p => p.Group == AlgorithmParameterCatalog.GlobalGroup);
        Assert.All(defaults, p => Assert.Contains("T-014", p.Description));
    }

    [Fact]
    public void AddAndRemoveParameter_ChangesTheCollection()
    {
        var (store, _, id) = NewStore();
        store.AddParameter(id, new AlgorithmParameter("Custom.Focus", "量测算法", "7"));
        Assert.Contains(store.Snapshot(id).Parameters, p => p.Key == "Custom.Focus");
        Assert.Throws<InvalidOperationException>(() =>
            store.AddParameter(id, new AlgorithmParameter("Custom.Focus", "量测算法", "8")));
        store.RemoveParameter(id, "Custom.Focus");
        Assert.DoesNotContain(store.Snapshot(id).Parameters, p => p.Key == "Custom.Focus");
    }

    [Fact]
    [BusinessRule("BR-36")]
    public void EffectiveAndMaskedRegions_DoNotChangeDetectionCoverage()
    {
        var (store, _, id) = NewStore();
        store.AddScanRegion(id, new MapRect(2, 2, 3, 3));
        int before = store.DiesInScanArea(id, 20, 20).Count;

        store.SaveDetectionItem(id, new DetectionItem
        {
            Name = "Zone-A",
            EffectiveRegion = new MapRect(2, 2, 1, 1),
            MaskedRegion = new MapRect(3, 3, 1, 1),
            Saved = true
        });

        Assert.Equal(before, store.DiesInScanArea(id, 20, 20).Count);
        Assert.Equal(9, store.Prescan(id, 20, 20).CoveredDies.Count);
    }

    [Fact]
    public void DetectionItems_RequireSaveBeforeStepCompletion()
    {
        var (store, _, id) = NewStore();
        store.AddRoiZone(id, new MapRect(1, 1, 2, 2));
        var unsaved = new DetectionItem { Name = "Zone-A", Saved = false };
        store.RemoveDetectionItem(id, "none");

        // 直接以未保存项构造校验输入，确认门禁口径（F-3.2.2-75 第 6 步）。
        var snapshot = store.Snapshot(id) with { DetectionItems = [unsaved] };
        Assert.Contains(InspectionValidator.ValidateRoi(snapshot), i => i.Field == "DetectionItems" && i.Message.Contains("尚未保存"));

        var saved = store.SaveDetectionItem(id, SavedItem());
        Assert.True(saved.Saved);
        Assert.Empty(InspectionValidator.ValidateRoi(store.Snapshot(id)));
    }

    [Fact]
    public void ValidateRoi_RequiresAtLeastOneZoneAndOneItem()
    {
        var (store, _, id) = NewStore();
        var issues = InspectionValidator.ValidateRoi(store.Snapshot(id));
        Assert.Contains(issues, i => i.Field == "RoiZones");
        Assert.Contains(issues, i => i.Field == "DetectionItems");
    }

    [Fact]
    public void ValidateScanArea_RejectsLocalScanWithoutRegion()
    {
        var (store, _, id) = NewStore();
        store.SetFullWafer(id, false);
        Assert.Contains(InspectionValidator.ValidateScanArea(store.Snapshot(id)),
            i => i.Field == "ScanArea");

        store.AddScanRegion(id, new MapRect(1, 1, 2, 2));
        Assert.Empty(InspectionValidator.ValidateScanArea(store.Snapshot(id)));
    }

    [Fact]
    public void RemoveRoiZone_ReindexesZonesAndRepairsDetectionItemReferences()
    {
        var (store, _, id) = NewStore();
        int z0 = store.AddRoiZone(id, new MapRect(1, 1, 2, 2)).Index;
        int z1 = store.AddRoiZone(id, new MapRect(4, 4, 2, 2)).Index;
        store.SaveDetectionItem(id, new DetectionItem { Name = "Ref-Z1", RoiZoneIndex = z1, Saved = true });

        store.RemoveRoiZone(id, z0);
        var snapshot = store.Snapshot(id);

        Assert.Single(snapshot.Zones);
        Assert.Equal(0, snapshot.Zones[0].Index);
        Assert.Equal(0, snapshot.DetectionItems[0].RoiZoneIndex);
        Assert.DoesNotContain(snapshot.DetectionItems, i => i.RoiZoneIndex == z1);
    }

    [Fact]
    public void DetectionItem_KeepsZonePadKindAndExtractionAlgorithm()
    {
        var (store, _, id) = NewStore();
        var item = new DetectionItem
        {
            Name = "Pad-1",
            Kind = RoiTargetKind.Pad,
            Extraction = RegionExtraction.Binarization,
            GoldenTone = GoldenImageTone.Dark,
            ParameterType = "Other",
            AlgorithmType = "Pad-Algo",
            Saved = true
        };
        var saved = store.SaveDetectionItem(id, item);

        Assert.Equal(RoiTargetKind.Pad, saved.Kind);
        Assert.Equal(RegionExtraction.Binarization, saved.Extraction);
        Assert.Equal(GoldenImageTone.Dark, saved.GoldenTone);
        Assert.Equal("Pad", saved.Kind.Text());
        Assert.Equal("二值化", saved.Extraction.Text());
    }

    /* ---------------- AC-H04-02 / B-23：单幅 vs 全部、统一缺陷模型 ---------------- */

    [Fact]
    public void TestSingle_UsesOnlyTheSelectedImage()
    {
        var (store, _, id) = NewStore();
        store.SelectImage(id, "D:/images/a.png");
        var outcome = store.TestSingle(id);

        Assert.Equal(VerificationScope.SingleImage, outcome.Scope);
        Assert.Equal(new[] { "D:/images/a.png" }, outcome.ImagePaths);
        Assert.All(outcome.Defects, d => Assert.Equal("D:/images/a.png", d.SourceImagePath));
    }

    [Fact]
    public void TestAll_UsesAllLoadedImages()
    {
        var (store, _, id) = NewStore();
        string[] images = ["a.png", "b.png", "c.png"];
        var outcome = store.TestAll(id, images);

        Assert.Equal(VerificationScope.AllImages, outcome.Scope);
        Assert.Equal(3, outcome.ImagePaths.Count);
        Assert.All(outcome.Defects, d => Assert.Contains(d.SourceImagePath, images));
    }

    [Fact]
    public void Prescan_IsExplicitlyOfflineAndDeviceDeferred()
    {
        var (store, _, id) = NewStore();
        var outcome = store.Prescan(id, 20, 20);
        Assert.True(outcome.IsOfflineComputation);
        Assert.Equal(InspectionText.DeferredDeviceAction, outcome.DeviceActionNote);
        Assert.Contains("[搁置] 未下发（T-029）", outcome.DeviceActionNote);
    }

    [Fact]
    public void DefectRecord_IsTheSingleSharedModelWithExtendedColumns()
    {
        var (store, _, id) = NewStore();
        store.SelectImage(id, "img.png");
        var record = store.TestSingle(id).Defects.FirstOrDefault();

        // 7 列历史字段 + H08 复判扩展列同时存在于同一记录（B-23 / R-H04-05）。
        Assert.NotNull(record);
        Assert.IsType<DefectRecord>(record);
        Assert.True(record!.Area > 0);
        Assert.Equal("未复判", record.ReviewState);
        Assert.Equal("img.png", record.SourceImagePath);
        Assert.Contains("AI 模型未部署", record.AiClassification);
    }
}
