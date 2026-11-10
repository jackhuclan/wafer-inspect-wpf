namespace WaferInspect.Core.Recipes;

/// <summary>
/// 子配方分步向导编排（B-25：流程 Owner 是 H02，步骤能力来自 H03/H04/H06）。
/// 门禁规则（`R-H02-06`）：
/// · 顺序固定为 T-042 确认的 7 步，不允许插入、跳过或重排步骤；
/// · **BR-19**：已完成的步骤随时可重新进入并再次执行（可回退，非一次性向导）；
/// · **B-25**：某步骤「失败」后，禁止进入其后任何步骤，直至重新执行并成功；
/// · 未开始的步骤中，只允许进入「第一个未完成步骤」（不得跳过中间步骤）。
/// </summary>
public sealed class SubRecipeWizard
{
    /// <summary>T-042 已确认的 7 步顺序（全系统唯一声明处）。</summary>
    public static readonly IReadOnlyList<WizardStepKey> OrderedSteps =
    [
        WizardStepKey.BasicInfo,
        WizardStepKey.Leveling,
        WizardStepKey.Mark,
        WizardStepKey.ReferenceImage,
        WizardStepKey.Roi,
        WizardStepKey.ScanArea,
        WizardStepKey.Recheck
    ];

    private readonly SubRecipe _recipe;

    public SubRecipeWizard(SubRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        _recipe = recipe;
        EnsureStepStates();
        Current = StateOf(WizardStepKey.BasicInfo);
    }

    /// <summary>向导绑定的子配方（步骤状态直接写回其中，随仓库持久化）。</summary>
    public SubRecipe Recipe => _recipe;

    /// <summary>当前所在步骤。</summary>
    public WizardStepState Current { get; private set; }

    /// <summary>全部完成（7 步均为 Completed）。</summary>
    public bool IsFinished
        => OrderedSteps.All(key => StateOf(key).Status == WizardStepStatus.Completed);

    /// <summary>第一个未完成（NotStarted/InProgress/Failed）的步骤；全部完成时为 null。</summary>
    public WizardStepKey? FirstIncomplete
    {
        get
        {
            foreach (var key in OrderedSteps)
            {
                if (StateOf(key).Status != WizardStepStatus.Completed) return key;
            }
            return null;
        }
    }

    /// <summary>
    /// 是否允许进入指定步骤（BR-19 + B-25 的统一判定，不抛异常，供界面刷新按钮可用性）。
    /// </summary>
    public bool CanEnter(WizardStepKey key)
    {
        int index = IndexOf(key);

        // 基础信息永远可进入（BR-19：入口步骤）。
        if (index == 0) return true;

        // B-25：前面任何一步失败，其后所有步骤禁止进入。
        for (int i = 0; i < index; i++)
        {
            if (StateOf(OrderedSteps[i]).Status == WizardStepStatus.Failed)
                return false;
        }

        // 已完成的步骤可重新进入（BR-19 可重入）。
        if (StateOf(key).Status == WizardStepStatus.Completed) return true;

        // 未完成步骤：只允许进入第一个未完成步骤（不得跳过中间步骤，B-25）。
        return FirstIncomplete == key;
    }

    /// <summary>进入步骤；被门禁拦截时抛 <see cref="InvalidOperationException"/>（含用户可读原因）。</summary>
    public void Enter(WizardStepKey key)
    {
        if (!CanEnter(key))
            throw new InvalidOperationException(BuildBlockReason(key));

        var state = StateOf(key);
        // BR-19：未开始或失败的步骤进入即视为「重新执行中」。
        if (state.Status is WizardStepStatus.NotStarted or WizardStepStatus.Failed)
            state.Status = WizardStepStatus.InProgress;

        Current = state;
    }

    /// <summary>尝试进入；返回是否成功（界面可直接刷新状态，不依赖异常）。</summary>
    public bool TryEnter(WizardStepKey key)
    {
        if (!CanEnter(key)) return false;
        Enter(key);
        return true;
    }

    /// <summary>进入下一步（T-042 顺序中的下一个允许步骤）。</summary>
    public bool TryEnterNext()
    {
        int index = IndexOf(Current.Key);
        if (index >= OrderedSteps.Count - 1) return false;
        return TryEnter(OrderedSteps[index + 1]);
    }

    /// <summary>进入上一步（BR-19：回退总是允许，回退不改变已完成状态）。</summary>
    public bool TryEnterPrevious()
    {
        int index = IndexOf(Current.Key);
        if (index <= 0) return false;
        return TryEnter(OrderedSteps[index - 1]);
    }

    /// <summary>标记当前步骤成功（B-25：成功后才允许下一步）。</summary>
    public void CompleteCurrent()
    {
        Current.Status = WizardStepStatus.Completed;
        Current.FailureReason = string.Empty;
    }

    /// <summary>标记当前步骤失败：显式记录原因，其后步骤立即被门禁锁定（B-25）。</summary>
    public void FailCurrent(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("失败原因不能为空（B-25：失败必须显式可见）。", nameof(reason));

        Current.Status = WizardStepStatus.Failed;
        Current.FailureReason = reason;
    }

    /// <summary>仅切换呈现焦点，不改变步骤状态（持久化后恢复视图位置用）。</summary>
    public void Focus(WizardStepKey key) => Current = StateOf(key);

    /// <summary>读取指定步骤状态。</summary>
    public WizardStepState StateOf(WizardStepKey key)
    {
        var state = _recipe.Steps.FirstOrDefault(s => s.Key == key);
        if (state == null) throw new InvalidOperationException($"缺少步骤 {key.Text()} 的状态记录（T-042 顺序被破坏）。");
        return state;
    }

    private void EnsureStepStates()
    {
        // 兼容旧数据：步骤状态与 T-042 顺序对齐，缺失则补齐为 NotStarted。
        if (_recipe.Steps.Count != OrderedSteps.Count ||
            !_recipe.Steps.Select(s => s.Key).SequenceEqual(OrderedSteps))
        {
            _recipe.Steps = OrderedSteps
                .Select(key => _recipe.Steps.FirstOrDefault(s => s.Key == key)
                               ?? new WizardStepState { Key = key })
                .ToList();
        }
    }

    private string BuildBlockReason(WizardStepKey key)
    {
        int index = IndexOf(key);

        for (int i = 0; i < index; i++)
        {
            var prev = StateOf(OrderedSteps[i]);
            if (prev.Status == WizardStepStatus.Failed)
                return $"步骤「{OrderedSteps[i].Text()}」失败：{prev.FailureReason}（B-25：失败禁止进入后续步骤，请先重新执行该步骤）";
        }

        var first = FirstIncomplete;
        if (first != null && first != key)
            return $"必须先完成「{first.Value.Text()}」步骤（B-25：成功后才允许下一步）。";

        return "当前步骤状态不允许进入该目标步骤。";
    }

    private static int IndexOf(WizardStepKey key)
    {
        for (int i = 0; i < OrderedSteps.Count; i++)
        {
            if (OrderedSteps[i] == key) return i;
        }
        return -1;
    }
}
