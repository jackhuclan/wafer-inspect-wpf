namespace WaferInspect.Core.Recipes;

/// <summary>
/// 子配方仓库（`R-H02-05`）：子 Recipe 新增 / 编辑 / 复制 / 重命名 / 删除。
/// 名称在**同一个父 Job 内唯一**（跨 Job 允许重名）；写操作统一走 <see cref="SubRecipeValidator"/>。
/// </summary>
public interface ISubRecipeRepository
{
    /// <summary>指定 Job 名下的子配方快照（按名称排序）。</summary>
    IReadOnlyList<SubRecipe> ListOf(string parentJobId);

    /// <summary>内容变化通知。</summary>
    event EventHandler? Changed;

    /// <summary>
    /// 新建子配方：基础信息初值从父 Job 公共参数继承（4.2.1 数据流），
    /// 名称 / 图谱 Notch 方向等字段由向导步骤 1 补齐。
    /// </summary>
    SubRecipe Create(string parentJobId, string name);

    SubRecipe Update(SubRecipe recipe);

    SubRecipe Rename(string subRecipeId, string newName);

    void Delete(string subRecipeId);

    SubRecipe Duplicate(string subRecipeId);

    SubRecipe? Find(string subRecipeId);
}

/// <summary>
/// 原型阶段的子配方仓库：内存实现（与 InMemoryJobRepository 同构）。
/// 真实持久化随 H07 配置单点确定后替换。
/// </summary>
public sealed class InMemorySubRecipeRepository : ISubRecipeRepository
{
    private readonly IJobRepository _jobs;
    private readonly List<SubRecipe> _items = [];
    private int _sequence = 5000;

    public InMemorySubRecipeRepository(IJobRepository jobs) => _jobs = jobs;

    public event EventHandler? Changed;

    public IReadOnlyList<SubRecipe> ListOf(string parentJobId)
        => _items.Where(r => string.Equals(r.ParentJobId, parentJobId, StringComparison.Ordinal))
            .OrderBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();

    public SubRecipe? Find(string subRecipeId)
        => _items.FirstOrDefault(r => string.Equals(r.Id, subRecipeId, StringComparison.Ordinal));

    public SubRecipe Create(string parentJobId, string name)
    {
        var parent = _jobs.Find(parentJobId)
            ?? throw new InvalidOperationException($"父 Job 不存在：{parentJobId}（请先保存并刷新 Job 列表）。");

        name = name?.Trim() ?? string.Empty;
        EnsureNameAvailable(parentJobId, name, ignoreId: null);

        var recipe = new SubRecipe
        {
            Id = $"SUB-{++_sequence}",
            ParentJobId = parentJobId,
            Name = name,
            // 基础信息初值继承父 Job（4.2.1 数据流），必填缺口由向导步骤 1 补齐。
            BasicInfo = InheritFromJob(parent)
        };
        // 子配方名称与基础信息名称保持同一份（向导步骤 1 的「名称」字段即此处 Name）。
        recipe.BasicInfo.Name = name;

        _items.Add(recipe);
        Raise();
        return recipe.Clone();
    }

    public SubRecipe Update(SubRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var existing = Find(recipe.Id)
            ?? throw new InvalidOperationException($"子配方不存在：{recipe.Id}（可能已被删除，请刷新）。");

        string name = recipe.Name?.Trim() ?? string.Empty;
        EnsureNameAvailable(existing.ParentJobId, name, ignoreId: existing.Id);

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("子配方名称必填（F-3.2.2-06）。");

        var updated = recipe.Clone();
        updated.Name = name;
        updated.BasicInfo.Name = name;   // 名称与基础信息保持同一份（T-005「名称」字段）

        // 基础信息整体校验：向导步骤 1 完成后必填项已齐，此处防止后续编辑把已通过的值改坏。
        var issues = SubRecipeValidator.Validate(updated.BasicInfo);
        if (issues.Count > 0)
            throw new InvalidOperationException(
                "基础信息未通过校验：" + string.Join("；", issues.Select(i => $"{i.Field}：{i.Message}")));

        _items[_items.IndexOf(existing)] = updated;
        Raise();
        return updated.Clone();
    }

    public SubRecipe Rename(string subRecipeId, string newName)
    {
        var existing = Find(subRecipeId)
            ?? throw new InvalidOperationException($"子配方不存在：{subRecipeId}。");

        newName = newName?.Trim() ?? string.Empty;
        EnsureNameAvailable(existing.ParentJobId, newName, ignoreId: existing.Id);

        existing.Name = newName;
        Raise();
        return existing.Clone();
    }

    public void Delete(string subRecipeId)
    {
        var existing = Find(subRecipeId);
        if (existing == null) return;

        _items.Remove(existing);
        Raise();
    }

    public SubRecipe Duplicate(string subRecipeId)
    {
        var source = Find(subRecipeId)
            ?? throw new InvalidOperationException($"子配方不存在：{subRecipeId}。");

        var copy = source.Clone();
        copy.Id = $"SUB-{++_sequence}";
        copy.Name = MakeCopyName(source);
        _items.Add(copy);
        Raise();
        return copy.Clone();
    }

    private static string MakeCopyName(SubRecipe source)
    {
        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{source.Name}-副本{i}";
            if (!string.Equals(candidate, source.Name, StringComparison.Ordinal)) return candidate;
        }
        return $"{source.Name}-副本";
    }

    private void EnsureNameAvailable(string parentJobId, string name, string? ignoreId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("子配方名称必填（F-3.2.2-06）。");

        bool conflict = _items.Any(r =>
            string.Equals(r.ParentJobId, parentJobId, StringComparison.Ordinal) &&
            !string.Equals(r.Id, ignoreId, StringComparison.Ordinal) &&
            string.Equals(r.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

        if (conflict)
            throw new InvalidOperationException($"同名子配方已存在：{name}（同一 Job 内名称必须唯一，F-3.2.2-01）。");
    }

    /// <summary>新建子配方时从父 Job 继承基础信息初值（T-005 字段口径）。</summary>
    private static SubRecipeBasicInfo InheritFromJob(JobDefinition parent) => new()
    {
        Name = string.Empty,
        PatternType = parent.PatternType,
        Size = parent.Size,
        Material = parent.Material,
        ThicknessMm = parent.ThicknessMm,
        DieXmm = parent.DieXmm,
        DieYmm = parent.DieYmm,
        NotchAngle = parent.NotchAngle,
        MapTypeId = parent.MapTypeId,
        MapFileType = parent.MapFileType,
        ColumnDirection = parent.ColumnDirection,
        RowDirection = parent.RowDirection,
        MapFilePath = parent.MapFilePath,
        MapNotchAngle = parent.NotchAngle,
        BarcodeCode = parent.BarcodeCode,
        OcrJob = parent.OcrJob,
        OcrAngle = parent.OcrAngle,
        OcrPosition = parent.OcrPosition,
        LocateDieX = parent.LocateDieX,
        LocateDieY = parent.LocateDieY,
        Orientation = parent.Orientation,
        ColumnOffset = parent.ColumnOffset,
        RowOffset = parent.RowOffset,
        ColRow = parent.ColRow,
        Xy = parent.Xy,
        IncludeExclude = parent.IncludeExclude,
        TraceImage = parent.TraceImage
    };

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
