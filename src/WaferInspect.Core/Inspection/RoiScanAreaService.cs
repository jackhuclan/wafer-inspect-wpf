using WaferInspect.Core.Recipes;
using WaferInspect.Core.Run;

namespace WaferInspect.Core.Inspection;

/// <summary>
/// ROI / 扫描区域**唯一数据源**（B-01/B-02、AC-P2-03、AC-H04-01）。
/// 配方向导步骤 5/6（编辑态）、缺陷分析的参数验证页（调试态）、H01 扫描中止调参
/// 全部经本接口读写同一份状态；任何界面都不得自建副本或独立写入 Recipe。
/// 关闭参数页由 <see cref="Save"/> 统一落盘（BR-40 / AC-P2-05）。
/// </summary>
public interface IRoiScanAreaStore
{
    /// <summary>状态变化通知（参数为子配方 Id），两个界面据此刷新，保证同一份数据可见。</summary>
    event EventHandler<string>? Changed;

    /// <summary>读取只读快照；首次访问时从子配方快照载入，之后本存储即为权威数据。</summary>
    RoiScanAreaSnapshot Snapshot(string recipeId);

    /* ---- 步骤 5：ROI 与检测设置列表（F-3.2.2-68~75） ---- */
    RoiZone AddRoiZone(string recipeId, MapRect rect, string? name = null, RoiTargetKind kind = RoiTargetKind.Zone);
    void UpdateRoiZone(string recipeId, int index, MapRect rect, string name);
    void RemoveRoiZone(string recipeId, int index);
    void ClearRoiZones(string recipeId);
    DetectionItem SaveDetectionItem(string recipeId, DetectionItem item);
    void RemoveDetectionItem(string recipeId, string itemId);

    /* ---- 步骤 6 / 参数验证：扫描区域与抽检（F-3.2.2-76~79、F-3.8.1-03/04、BR-37~39） ---- */
    void AddScanRegion(string recipeId, MapRect rect, string source = "Map 圈选");
    void ClearScanRegions(string recipeId);
    void SetFullWafer(string recipeId, bool fullWafer);
    void SetSamplingEnabled(string recipeId, bool enabled);
    void ToggleSamplingDie(string recipeId, int row, int column);
    void ClearSamplingDies(string recipeId);

    /* ---- ROI 参数设置页（T-014 可扩展集合 / BR-35/36） ---- */
    void SetParameter(string recipeId, string key, string value);
    void AddParameter(string recipeId, AlgorithmParameter parameter);
    void RemoveParameter(string recipeId, string key);
    void SetImageFolder(string recipeId, string folderPath);
    void SelectImage(string recipeId, string imagePath);

    /* ---- 参数验证与预扫描（F-3.8.1-02~12、BR-40~42） ---- */
    IReadOnlyList<DieCoordinate> DiesInScanArea(string recipeId, int columns, int rows);
    VerificationOutcome TestSingle(string recipeId);
    VerificationOutcome TestAll(string recipeId, IReadOnlyList<string> imagePaths);
    VerificationOutcome Prescan(string recipeId, int columns, int rows);

    /* ---- 持久化（唯一写回 Recipe 的出口） ---- */
    void ApplyTo(string recipeId, SubRecipe recipe);
    bool Save(string recipeId);
}

/// <summary>
/// 唯一数据源的内存实现。子配方仓库只作为持久化介质：
/// 所有编辑先落到本存储并广播 <see cref="Changed"/>，再由 <see cref="ApplyTo"/> /
/// <see cref="Save"/> 写回 Recipe，杜绝「首页与算法参数各自维护 ROI」（5.6 清单第 6 项）。
/// </summary>
public sealed class RoiScanAreaStore : IRoiScanAreaStore
{
    private readonly ISubRecipeRepository _recipes;
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public RoiScanAreaStore(ISubRecipeRepository recipes) => _recipes = recipes;

    public event EventHandler<string>? Changed;

    public RoiScanAreaSnapshot Snapshot(string recipeId)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            return new RoiScanAreaSnapshot(
                recipeId,
                s.Zones.ToList(),
                s.Items.Select(i => i.Clone()).ToList(),
                s.ScanRegions.Select(r => r.Clone()).ToList(),
                s.SamplingDies.ToList(),
                s.Parameters.ToList(),
                s.FullWafer,
                s.SamplingEnabled,
                s.ImageFolderPath,
                s.SelectedImagePath,
                s.SavedVersion,
                s.LastSavedAt);
        }
    }

    /* ---------------- 步骤 5 ---------------- */

    public RoiZone AddRoiZone(string recipeId, MapRect rect, string? name = null, RoiTargetKind kind = RoiTargetKind.Zone)
    {
        EnsureId(recipeId);
        Normalize(rect);
        lock (_gate)
        {
            var s = Get(recipeId);
            int index = s.Zones.Count == 0 ? 0 : s.Zones.Max(z => z.Index) + 1;
            var zone = new RoiZone(index,
                string.IsNullOrWhiteSpace(name) ? $"{kind.Text()} {index + 1}" : name.Trim(),
                rect.X, rect.Y, rect.Width, rect.Height);
            s.Zones.Add(zone);
            Raise(recipeId);
            return zone;
        }
    }

    public void UpdateRoiZone(string recipeId, int index, MapRect rect, string name)
    {
        EnsureId(recipeId);
        Normalize(rect);
        lock (_gate)
        {
            var s = Get(recipeId);
            int at = s.Zones.FindIndex(z => z.Index == index);
            if (at < 0) throw new InvalidOperationException($"ROI 区域 {index} 不存在（请先添加区域）。");
            s.Zones[at] = s.Zones[at] with
            {
                Name = string.IsNullOrWhiteSpace(name) ? s.Zones[at].Name : name.Trim(),
                X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height
            };
            Raise(recipeId);
        }
    }

    public void RemoveRoiZone(string recipeId, int index)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            s.Zones.RemoveAll(z => z.Index == index);
            // 索引连续化，并显式清除失效的检测项引用（不静默保留悬空引用）。
            for (int i = 0; i < s.Zones.Count; i++)
            {
                var old = s.Zones[i];
                if (old.Index != i)
                {
                    foreach (var item in s.Items.Where(x => x.RoiZoneIndex == old.Index))
                        item.RoiZoneIndex = i;
                    s.Zones[i] = old with { Index = i };
                }
            }
            foreach (var item in s.Items.Where(x => x.RoiZoneIndex >= s.Zones.Count))
                item.RoiZoneIndex = null;
            Raise(recipeId);
        }
    }

    public void ClearRoiZones(string recipeId)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            s.Zones.Clear();
            foreach (var item in s.Items) item.RoiZoneIndex = null;
            Raise(recipeId);
        }
    }

    public DetectionItem SaveDetectionItem(string recipeId, DetectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureId(recipeId);
        if (string.IsNullOrWhiteSpace(item.Name))
            throw new InvalidOperationException("检测设置项名称必填（F-3.2.2-69）。");

        lock (_gate)
        {
            var s = Get(recipeId);
            var copy = item.Clone();
            copy.Id = string.IsNullOrWhiteSpace(copy.Id) ? $"ITEM-{s.Sequence++}" : copy.Id;
            copy.Saved = true;   // F-3.2.2-75 第 6 步「右下角保存」后才算完成
            int at = s.Items.FindIndex(i => i.Id == copy.Id);
            if (at >= 0) s.Items[at] = copy; else s.Items.Add(copy);
            Raise(recipeId);
            return copy.Clone();
        }
    }

    public void RemoveDetectionItem(string recipeId, string itemId)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            s.Items.RemoveAll(i => i.Id == itemId);
            Raise(recipeId);
        }
    }

    /* ---------------- 步骤 6 / 参数验证 ---------------- */

    public void AddScanRegion(string recipeId, MapRect rect, string source = "Map 圈选")
    {
        EnsureId(recipeId);
        Normalize(rect);
        lock (_gate)
        {
            var s = Get(recipeId);
            // BR-37/39：添加局部区域即离开「默认全片」状态；支持单个或多个。
            s.ScanRegions.Add(new ScanRegion
            {
                Name = $"扫描区域 {s.ScanRegions.Count + 1}",
                Rect = rect,
                Source = string.IsNullOrWhiteSpace(source) ? "Map 圈选" : source
            });
            s.FullWafer = false;
            Raise(recipeId);
        }
    }

    public void ClearScanRegions(string recipeId)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            // BR-39：清除扫描区域清空全部；BR-37：清空后回到默认全片。
            s.ScanRegions.Clear();
            s.FullWafer = true;
            Raise(recipeId);
        }
    }

    public void SetFullWafer(string recipeId, bool fullWafer)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            s.FullWafer = fullWafer;
            Raise(recipeId);
        }
    }

    public void SetSamplingEnabled(string recipeId, bool enabled)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            s.SamplingEnabled = enabled;
            if (enabled) s.FullWafer = false;
            Raise(recipeId);
        }
    }

    public void ToggleSamplingDie(string recipeId, int row, int column)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            // BR-38：双击选单个 Die，可设单个或多个；重复双击取消该 Die。
            int at = s.SamplingDies.FindIndex(d => d.Row == row && d.Column == column);
            if (at >= 0) s.SamplingDies.RemoveAt(at);
            else s.SamplingDies.Add(new SamplingDie(row, column));
            s.SamplingEnabled = true;
            s.FullWafer = false;
            Raise(recipeId);
        }
    }

    public void ClearSamplingDies(string recipeId)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            s.SamplingDies.Clear();
            Raise(recipeId);
        }
    }

    /* ---------------- ROI 参数设置页 ---------------- */

    public void SetParameter(string recipeId, string key, string value)
    {
        EnsureId(recipeId);
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("参数键不能为空。", nameof(key));
        lock (_gate)
        {
            var s = Get(recipeId);
            int at = s.Parameters.FindIndex(p => p.Key == key);
            if (at < 0) throw new InvalidOperationException($"算法参数 {key} 不存在（请先添加该参数项，T-014）。");
            s.Parameters[at] = s.Parameters[at] with { Value = value ?? string.Empty };
            Raise(recipeId);
        }
    }

    public void AddParameter(string recipeId, AlgorithmParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            if (s.Parameters.Any(p => p.Key == parameter.Key))
                throw new InvalidOperationException($"算法参数 {parameter.Key} 已存在（请用编辑而非重复添加）。");
            s.Parameters.Add(parameter);
            Raise(recipeId);
        }
    }

    public void RemoveParameter(string recipeId, string key)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            s.Parameters.RemoveAll(p => p.Key == key);
            Raise(recipeId);
        }
    }

    public void SetImageFolder(string recipeId, string folderPath)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            s.ImageFolderPath = folderPath?.Trim() ?? string.Empty;
            Raise(recipeId);
        }
    }

    public void SelectImage(string recipeId, string imagePath)
    {
        EnsureId(recipeId);
        lock (_gate)
        {
            var s = Get(recipeId);
            s.SelectedImagePath = imagePath?.Trim() ?? string.Empty;
            Raise(recipeId);
        }
    }

    /* ---------------- 验证与预扫描 ---------------- */

    /// <summary>
    /// BR-41：预扫描仅覆盖框选区域。抽检模式覆盖选中的 Die；全片模式覆盖整个 Map；
    /// 局部模式取所有扫描区域的并集（未框选任何区域时为空，不悄悄退回全片）。
    /// </summary>
    public IReadOnlyList<DieCoordinate> DiesInScanArea(string recipeId, int columns, int rows)
    {
        EnsureId(recipeId);
        if (columns <= 0 || rows <= 0) return [];

        lock (_gate)
        {
            var s = Get(recipeId);
            var result = new List<DieCoordinate>();
            var seen = new HashSet<(int, int)>();

            if (s.SamplingEnabled)
            {
                foreach (var die in s.SamplingDies)
                    if (die.Row >= 1 && die.Row <= rows && die.Column >= 1 && die.Column <= columns
                        && seen.Add((die.Row, die.Column)))
                        result.Add(new DieCoordinate(die.Row, die.Column));
                return result;
            }

            for (int row = 1; row <= rows; row++)
            for (int column = 1; column <= columns; column++)
            {
                bool covered = s.FullWafer || s.ScanRegions.Any(r => r.Rect.Contains(column, row));
                if (covered && seen.Add((row, column))) result.Add(new DieCoordinate(row, column));
            }
            return result;
        }
    }

    /// <summary>F-3.8.1-05：单幅图片测试只作用于「底部缺陷图片路径」的那一幅。</summary>
    public VerificationOutcome TestSingle(string recipeId)
    {
        var snapshot = Snapshot(recipeId);
        string image = snapshot.SelectedImagePath;
        IReadOnlyList<string> images = string.IsNullOrWhiteSpace(image) ? [] : [image];
        return Evaluate(recipeId, VerificationScope.SingleImage, images);
    }

    /// <summary>F-3.8.1-06：全部图片测试对该文件夹下的所有图片批量重检。</summary>
    public VerificationOutcome TestAll(string recipeId, IReadOnlyList<string> imagePaths)
    {
        EnsureId(recipeId);
        return Evaluate(recipeId, VerificationScope.AllImages,
            (imagePaths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>F-3.8.1-07：预扫描开始；真实运动/采集未下发（T-029），仅做确定性离线演算。</summary>
    public VerificationOutcome Prescan(string recipeId, int columns, int rows)
        => Evaluate(recipeId, VerificationScope.Prescan, [], columns, rows);

    /// <summary>把唯一数据源写回落盘载体；由向导完成步骤与参数页关闭时调用，禁止界面另写一份。</summary>
    public void ApplyTo(string recipeId, SubRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var snapshot = Snapshot(recipeId);

        recipe.RoiZones = snapshot.Zones.ToList();
        recipe.ScanArea = new ScanAreaConfig
        {
            FullWafer = snapshot.FullWafer,
            RoiIndexes = snapshot.FullWafer
                ? []
                : snapshot.Zones.Select(z => z.Index).ToList()
        };
        recipe.Inspection = new InspectionConfig
        {
            DetectionItems = snapshot.DetectionItems,
            ScanRegions = snapshot.ScanRegions,
            SamplingDies = snapshot.SamplingDies,
            Parameters = snapshot.Parameters,
            FullWafer = snapshot.FullWafer,
            SamplingEnabled = snapshot.SamplingEnabled,
            ImageFolderPath = snapshot.ImageFolderPath,
            SelectedImagePath = snapshot.SelectedImagePath,
            SavedVersion = snapshot.SavedVersion,
            LastSavedAt = snapshot.LastSavedAt
        };
    }

    /// <summary>
    /// BR-40 / AC-P2-05 / AC-H04-03：关闭参数页（右上角 ×）自动保存当前参数配置。
    /// 返回是否真正写入子配方仓库；仓库中无该子配方时返回 false，不伪造成功。
    /// </summary>
    public bool Save(string recipeId)
    {
        EnsureId(recipeId);
        var stored = _recipes.Find(recipeId);
        if (stored == null) return false;

        lock (_gate)
        {
            var s = Get(recipeId);
            s.SavedVersion++;
            s.LastSavedAt = DateTimeOffset.Now;
        }

        var updated = stored.Clone();
        ApplyTo(recipeId, updated);
        try
        {
            // 基础信息校验由仓库统一把关；不满足时返回 false，不把保存失败转成成功（E-05/E-10）。
            _recipes.Update(updated);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        Raise(recipeId);
        return true;
    }

    private VerificationOutcome Evaluate(
        string recipeId, VerificationScope scope, IReadOnlyList<string> images,
        int columns = 20, int rows = 20)
    {
        var snapshot = Snapshot(recipeId);
        var covered = DiesInScanArea(recipeId, columns, rows);
        var defects = new List<DefectRecord>();

        // 确定性离线演算：按 (行,列,参数阈值) 哈希稳定生成，同一输入恒得同一结果；
        // 真实检测/AI 推理依赖 H06 采集（T-029）与 AI 平台（T-035），不伪造设备执行。
        string threshold = AlgorithmParameterCatalog.ParameterTypeOf(snapshot.Parameters);
        for (int i = 0; i < covered.Count; i++)
        {
            var die = covered[i];
            int hash = Math.Abs(HashCode.Combine(recipeId, die.Row, die.Column, threshold, scope));
            if (hash % 5 != 0) continue;

            string image = images.Count == 0 ? string.Empty : images[i % images.Count];
            string region = snapshot.FullWafer
                ? "全片"
                : snapshot.ScanRegions.FirstOrDefault(r => r.Rect.Contains(die.Column, die.Row))?.Name ?? "抽检";
            double length = 8.0 + hash % 400 / 10.0;
            double width = 5.0 + hash % 250 / 10.0;
            defects.Add(new DefectRecord(
                Zone: region,
                Category: hash % 2 == 0 ? "亮缺陷" : "暗缺陷",
                Row: die.Row,
                Column: die.Column,
                Length: length,
                Width: width,
                Area: length * width,
                AiClassification: "离线演算（AI 模型未部署，T-035）",
                AiBin: "",
                ReviewState: "未复判",
                Note: $"{scope.Text()} · 覆盖 Die {covered.Count} 颗 · 参数类型 {threshold}",
                SourceImagePath: image));
        }

        string summary = $"{scope.Text()}：图片 {images.Count} 幅，覆盖 Die {covered.Count} 颗，检出 {defects.Count} 条缺陷"
                         + (scope == VerificationScope.SingleImage && images.Count == 0
                             ? "；未选择图片路径，需先在「原始图片路径」中指定（F-3.8.1-09）"
                             : string.Empty);
        return new VerificationOutcome(scope, images, covered, defects, summary);
    }

    private static void Normalize(MapRect rect)
    {
        ArgumentNullException.ThrowIfNull(rect);
        if (rect.Width <= 0 || rect.Height <= 0 || rect.X < 1 || rect.Y < 1)
            throw new ArgumentOutOfRangeException(nameof(rect), "区域起始行/列必须 >= 1（1-based Die 网格），宽高必须大于 0。");
    }

    private static void EnsureId(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId))
            throw new ArgumentException("子配方 Id 不能为空。", nameof(recipeId));
    }

    private State Get(string recipeId)
    {
        if (_states.TryGetValue(recipeId, out var state)) return state;

        // 首次访问从子配方快照载入；之后本存储即为权威数据（避免参数页未保存的编辑被覆盖）。
        state = new State();
        var recipe = _recipes.Find(recipeId);
        if (recipe != null)
        {
            var config = recipe.Inspection;
            state.Zones = recipe.RoiZones.ToList();
            state.Items = config.DetectionItems.Select(i => i.Clone()).ToList();
            state.ScanRegions = config.ScanRegions.Select(r => r.Clone()).ToList();
            state.SamplingDies = config.SamplingDies.ToList();
            state.Parameters = config.Parameters.Count == 0
                ? AlgorithmParameterCatalog.Defaults.ToList()
                : config.Parameters.ToList();
            state.FullWafer = config.FullWafer || recipe.ScanArea.FullWafer;
            state.SamplingEnabled = config.SamplingEnabled;
            state.ImageFolderPath = config.ImageFolderPath;
            state.SelectedImagePath = config.SelectedImagePath;
            state.SavedVersion = config.SavedVersion;
            state.LastSavedAt = config.LastSavedAt;
        }
        else
        {
            state.Parameters = AlgorithmParameterCatalog.Defaults.ToList();
        }
        _states[recipeId] = state;
        return state;
    }

    private void Raise(string recipeId) => Changed?.Invoke(this, recipeId);

    private sealed class State
    {
        public List<RoiZone> Zones { get; set; } = [];
        public List<DetectionItem> Items { get; set; } = [];
        public List<ScanRegion> ScanRegions { get; set; } = [];
        public List<SamplingDie> SamplingDies { get; set; } = [];
        public List<AlgorithmParameter> Parameters { get; set; } = [];
        public bool FullWafer { get; set; } = true;
        public bool SamplingEnabled { get; set; }
        public string ImageFolderPath { get; set; } = string.Empty;
        public string SelectedImagePath { get; set; } = string.Empty;
        public int SavedVersion { get; set; }
        public DateTimeOffset? LastSavedAt { get; set; }
        public int Sequence { get; set; } = 1;
    }
}

/// <summary>H04 结构校验：ROI 步骤与扫描区域步骤的门禁口径（B-25 由 H02 编排调用）。</summary>
public static class InspectionValidator
{
    /// <summary>
    /// F-3.2.2-68~75：至少 1 个 ROI 区域；每项检测设置必须保存（Saved）后才算完成；
    /// Pad 检测选 Pad、其他选 Zone；BR-36 有效/屏蔽区域不改变检测范围（此处仅校验结构）。
    /// </summary>
    public static IReadOnlyList<SubRecipeValidationIssue> ValidateRoi(RoiScanAreaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var issues = SubRecipeValidator.ValidateRoiZones(snapshot.Zones).ToList();

        if (snapshot.DetectionItems.Count == 0)
            issues.Add(new("DetectionItems", "至少需要 1 个检测设置项（F-3.2.2-69：每项为独立检测单元）"));

        foreach (var item in snapshot.DetectionItems)
        {
            if (!item.Saved)
                issues.Add(new("DetectionItems", $"检测项「{item.Name}」尚未保存（F-3.2.2-75 第 6 步：右下角保存后才算完成）"));
            if (string.IsNullOrWhiteSpace(item.AlgorithmType))
                issues.Add(new("DetectionItems", $"检测项「{item.Name}」算法类型必填（F-3.2.2-69）"));
            if (item.Kind == RoiTargetKind.Pad && item.ParameterType == InspectionText.DefaultParameterType)
            {
                // BR-35 是「常用」而非强制；Pad 检测只是提示可按实际选择其他，不判失败。
            }
        }
        return issues;
    }

    /// <summary>
    /// BR-37/39/41：全片扫描无需区域；局部扫描必须至少 1 个圈选区域或抽检 Die；
    /// 清除扫描区域后回到默认全片（由服务保证），此处只校验配置合法性。
    /// </summary>
    public static IReadOnlyList<SubRecipeValidationIssue> ValidateScanArea(RoiScanAreaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var issues = new List<SubRecipeValidationIssue>();
        if (!snapshot.FullWafer && snapshot.ScanRegions.Count == 0 && snapshot.SamplingDies.Count == 0)
            issues.Add(new("ScanArea", "未勾选全片扫描时，必须先在 ROI 步骤定义区域或在参数验证页添加扫描区域（BR-37/39/41）"));
        return issues;
    }
}
