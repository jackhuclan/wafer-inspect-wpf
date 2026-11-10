#nullable enable
using WaferInspect.Core.Maps;
using WaferInspect.Core.Recipes;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P2（H02）子配方七步向导：T-042 步骤顺序、B-25 失败门禁、BR-19 步骤可重入、
/// T-005 基础信息必填口径与仓库 CRUD/继承。
/// </summary>
public sealed class SubRecipeWizardTests
{
    private static InMemoryJobRepository NewJobRepo()
        => new(new SimulatedMapTypeCatalog());

    private static (InMemoryJobRepository Jobs, InMemorySubRecipeRepository Subs) NewRepos()
    {
        var jobs = NewJobRepo();
        return (jobs, new InMemorySubRecipeRepository(jobs));
    }

    private static SubRecipe NewSubRecipe(InMemoryJobRepository jobs, InMemorySubRecipeRepository subs, string name = "SR-STD")
        => subs.Create(jobs.Jobs[0].JobId, name);

    /* ---------------- T-042：步骤顺序 ---------------- */

    [Fact]
    [BusinessRule("B-25")]
    public void OrderedSteps_AreExactlyTheSevenConfirmedSteps()
    {
        Assert.Equal(
            new[]
            {
                WizardStepKey.BasicInfo, WizardStepKey.Leveling, WizardStepKey.Mark,
                WizardStepKey.ReferenceImage, WizardStepKey.Roi, WizardStepKey.ScanArea,
                WizardStepKey.Recheck
            },
            SubRecipeWizard.OrderedSteps);

        Assert.Equal(7, SubRecipeWizard.OrderedSteps.Count);
    }

    [Fact]
    public void Wizard_StartsAtBasicInfo_WhenNothingCompleted()
    {
        var (jobs, subs) = NewRepos();
        var wizard = new SubRecipeWizard(NewSubRecipe(jobs, subs));

        Assert.Equal(WizardStepKey.BasicInfo, wizard.Current.Key);
        Assert.Equal(WizardStepStatus.NotStarted, wizard.Current.Status);
    }

    /* ---------------- B-25：成功后才允许下一步 ---------------- */

    [Fact]
    [BusinessRule("B-25")]
    public void ForwardEntry_BlockedUntilPreviousStepCompleted()
    {
        var (jobs, subs) = NewRepos();
        var wizard = new SubRecipeWizard(NewSubRecipe(jobs, subs));

        // 未完成基础信息：禁止直接进入拉平及之后任何步骤。
        Assert.False(wizard.CanEnter(WizardStepKey.Leveling));
        Assert.False(wizard.CanEnter(WizardStepKey.Recheck));
        Assert.Throws<InvalidOperationException>(() => wizard.Enter(WizardStepKey.Recheck));

        wizard.CompleteCurrent();
        Assert.True(wizard.TryEnter(WizardStepKey.Leveling));
        Assert.Equal(WizardStepStatus.InProgress, wizard.Current.Status);
    }

    [Fact]
    [BusinessRule("B-25")]
    public void SkippedMiddleStep_IsRejected()
    {
        var (jobs, subs) = NewRepos();
        var wizard = new SubRecipeWizard(NewSubRecipe(jobs, subs));

        wizard.CompleteCurrent();            // 基础信息完成 → 允许拉平
        Assert.True(wizard.TryEnter(WizardStepKey.Leveling));
        wizard.CompleteCurrent();            // 拉平完成 → 允许 Mark

        // 但不允许跳过 Mark 直接进入 ROI。
        Assert.False(wizard.CanEnter(WizardStepKey.Roi));
        Assert.False(wizard.TryEnter(WizardStepKey.Roi));
    }

    [Fact]
    [BusinessRule("B-25")]
    public void FailedStep_BlocksAllLaterSteps_UntilReExecuted()
    {
        var (jobs, subs) = NewRepos();
        var wizard = new SubRecipeWizard(NewSubRecipe(jobs, subs));

        wizard.CompleteCurrent();
        Assert.True(wizard.TryEnter(WizardStepKey.Leveling));
        wizard.FailCurrent("框选区域后未找到特征（BR-24 匹配分数低于阈值）");

        // B-25：失败后，其后任何步骤禁止进入。
        Assert.False(wizard.CanEnter(WizardStepKey.Mark));
        Assert.False(wizard.CanEnter(WizardStepKey.Recheck));
        var ex = Assert.Throws<InvalidOperationException>(() => wizard.Enter(WizardStepKey.Mark));
        Assert.Contains("B-25", ex.Message);
        Assert.Contains("拉平", ex.Message);

        // 重新执行并成功后解锁。
        Assert.True(wizard.CanEnter(WizardStepKey.Leveling));   // 失败步骤本身可重入（BR-19）
        wizard.Enter(WizardStepKey.Leveling);
        Assert.Equal(WizardStepStatus.InProgress, wizard.Current.Status);
        wizard.CompleteCurrent();
        Assert.True(wizard.TryEnter(WizardStepKey.Mark));
    }

    /* ---------------- BR-19：每个步骤都可进入且重新执行 ---------------- */

    [Fact]
    [BusinessRule("BR-19")]
    public void CompletedStep_CanBeReEntered_AndReFailureReBlocksForward()
    {
        var (jobs, subs) = NewRepos();
        var wizard = new SubRecipeWizard(NewSubRecipe(jobs, subs));

        wizard.CompleteCurrent();
        wizard.TryEnter(WizardStepKey.Leveling);
        wizard.CompleteCurrent();

        // 回退到基础信息重新执行（BR-19：不是一次性向导）。
        Assert.True(wizard.TryEnterPrevious());
        Assert.Equal(WizardStepKey.BasicInfo, wizard.Current.Key);

        // 再次失败会重新锁住后续步骤（可重入意味着失败也会重新生效）。
        wizard.FailCurrent("名称被清空");
        Assert.False(wizard.CanEnter(WizardStepKey.Leveling));
        wizard.CompleteCurrent();
        Assert.True(wizard.CanEnter(WizardStepKey.Leveling));
    }

    [Fact]
    public void IsFinished_OnlyWhenAllSevenStepsCompleted()
    {
        var (jobs, subs) = NewRepos();
        var recipe = NewSubRecipe(jobs, subs);
        var wizard = new SubRecipeWizard(recipe);

        Assert.False(wizard.IsFinished);
        Assert.Equal(WizardStepKey.BasicInfo, wizard.FirstIncomplete);

        foreach (WizardStepKey key in SubRecipeWizard.OrderedSteps)
        {
            Assert.Equal(key, wizard.FirstIncomplete);
            wizard.Enter(key);
            wizard.CompleteCurrent();
        }

        Assert.True(wizard.IsFinished);
        Assert.Null(wizard.FirstIncomplete);
        Assert.All(recipe.Steps, s => Assert.Equal(WizardStepStatus.Completed, s.Status));
    }

    [Fact]
    public void StepStates_PersistWithSubRecipe_RoundTripKeepsProgress()
    {
        var (jobs, subs) = NewRepos();
        var recipe = NewSubRecipe(jobs, subs);

        var wizard = new SubRecipeWizard(recipe);
        wizard.CompleteCurrent();
        wizard.TryEnter(WizardStepKey.Leveling);
        wizard.FailCurrent("设备未连接（T-029）");

        // 通过仓库往返（编辑保存）后状态不丢。
        var updated = subs.Update(recipe);
        var reloaded = subs.Find(updated.Id);

        var leveling = reloaded!.Steps.First(s => s.Key == WizardStepKey.Leveling);
        Assert.Equal(WizardStepStatus.Failed, leveling.Status);
        Assert.Contains("T-029", leveling.FailureReason);
    }

    /* ---------------- T-005：基础信息字段与校验 ---------------- */

    [Fact]
    public void SubRecipeValidator_RequiredFields_MatchT005Screenshot()
    {
        Assert.Equal(
            new[] { "Name", "DieYmm", "MapTypeId", "MapFilePath", "MapNotchAngle", "BarcodeCode" },
            SubRecipeValidator.RequiredFields);
    }

    [Fact]
    public void SubRecipeValidator_MissingMapNotchAngle_IsRejected()
    {
        var info = ValidBasicInfo();
        info.MapNotchAngle = -1;

        var issues = SubRecipeValidator.Validate(info);

        Assert.Contains(issues, i => i.Field == nameof(SubRecipeBasicInfo.MapNotchAngle));
        Assert.False(SubRecipeValidator.IsValid(info));
    }

    [Fact]
    public void SubRecipeValidator_Br78_TypeMustMatchFileExtension()
    {
        var info = ValidBasicInfo();
        info.MapFileType = MapFileType.Txt;   // 文件实际是 .csv

        var issues = SubRecipeValidator.Validate(info);

        Assert.Contains(issues, i => i.Field == nameof(SubRecipeBasicInfo.MapFileType) && i.Message.Contains("BR-78"));
    }

    [Fact]
    public void LevelingConfig_MatchScore_MustBeWithin0To100()
    {
        Assert.Empty(SubRecipeValidator.ValidateLeveling(new LevelingConfig { MatchScore = 70 }));
        Assert.NotEmpty(SubRecipeValidator.ValidateLeveling(new LevelingConfig { MatchScore = 101 }));
        Assert.NotEmpty(SubRecipeValidator.ValidateLeveling(new LevelingConfig { MatchScore = -1 }));
    }

    [Fact]
    public void RecheckConfig_Defaults_FollowBr20()
    {
        var config = new RecheckConfig();

        Assert.Equal(RecheckMode.OfflineReview, config.Mode);   // BR-20：默认离线复判
        Assert.True(config.UseReviewCamera);                     // BR-20：默认复查相机
    }

    /* ---------------- 仓库：继承 / 唯一名称 / CRUD ---------------- */

    [Fact]
    public void Create_InheritsBasicInfoFromParentJob()
    {
        var (jobs, subs) = NewRepos();
        var parent = jobs.Jobs[0];

        var recipe = subs.Create(parent.JobId, "SR-INHERIT");

        Assert.Equal(parent.PatternType, recipe.BasicInfo.PatternType);
        Assert.Equal(parent.Size, recipe.BasicInfo.Size);
        Assert.Equal(parent.DieYmm, recipe.BasicInfo.DieYmm);
        Assert.Equal(parent.MapTypeId, recipe.BasicInfo.MapTypeId);
        Assert.Equal(parent.MapFilePath, recipe.BasicInfo.MapFilePath);
        Assert.Equal(parent.MapFileType, recipe.BasicInfo.MapFileType);
        Assert.Equal(parent.BarcodeCode, recipe.BasicInfo.BarcodeCode);
        Assert.Equal(parent.TraceImage, recipe.BasicInfo.TraceImage);
        // 图谱 Notch 方向初值沿用 Notch 方向（T-005 截图字段，向导步骤 1 可改）。
        Assert.Equal(parent.NotchAngle, recipe.BasicInfo.MapNotchAngle);
    }

    [Fact]
    public void Create_SameNameInSameJob_IsRejected_CrossJobAllowed()
    {
        var (jobs, subs) = NewRepos();
        subs.Create(jobs.Jobs[0].JobId, "SR-A");

        Assert.Throws<InvalidOperationException>(() => subs.Create(jobs.Jobs[0].JobId, "sr-a"));

        // 不同 Job 下允许同名。
        var other = subs.Create(jobs.Jobs[1].JobId, "SR-A");
        Assert.Equal("SR-A", other.Name);
    }

    [Fact]
    public void Duplicate_CopiesAllSevenStepStates()
    {
        var (jobs, subs) = NewRepos();
        var source = NewSubRecipe(jobs, subs);

        var wizard = new SubRecipeWizard(source);
        wizard.CompleteCurrent();
        wizard.TryEnter(WizardStepKey.Leveling);
        wizard.CompleteCurrent();
        subs.Update(source);

        var copy = subs.Duplicate(source.Id);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.StartsWith("SR-STD-副本", copy.Name);
        Assert.Equal(WizardStepStatus.Completed, copy.Steps.First(s => s.Key == WizardStepKey.BasicInfo).Status);
        Assert.Equal(WizardStepStatus.Completed, copy.Steps.First(s => s.Key == WizardStepKey.Leveling).Status);
        Assert.Equal(WizardStepStatus.NotStarted, copy.Steps.First(s => s.Key == WizardStepKey.Mark).Status);
    }

    private static SubRecipeBasicInfo ValidBasicInfo()
    {
        string mapPath = Path.GetTempFileName();
        string csvPath = Path.ChangeExtension(mapPath, ".csv");
        File.Move(mapPath, csvPath, true);
        File.WriteAllText(csvPath, "DieX,DieY,Cols,Rows\n24,25,10,10\n0,0,1\n");

        return new SubRecipeBasicInfo
        {
            Name = "SR-VALID",
            DieXmm = 24,
            DieYmm = 25,
            MapTypeId = "MAP-PL-300",
            MapFileType = MapFileType.Csv,
            MapFilePath = csvPath,
            MapNotchAngle = 270,
            BarcodeCode = "1"
        };
    }
}
