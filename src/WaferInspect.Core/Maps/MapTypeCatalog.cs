namespace WaferInspect.Core.Maps;

/// <summary>
/// BR-43：8 种 Bin 类型**固定枚举**（保留历史原拼写 <c>ForBiddenDie</c>）。
/// F-3.7.2-03 的「增加 / 移除 Bin 代码」只能在这 8 类中启用 / 停用，不能新增第 9 种类型。
/// </summary>
public enum BinKind
{
    GoodDie,
    DefectDie,
    ReferenceDie,
    OriginalDie,
    BlankDie,
    ForBiddenDie,
    MatchFailDie,
    ExceptionDie
}

/// <summary>8 类 Bin 的唯一中文命名、默认代码 / 等级 / 颜色 / 快捷键来源。</summary>
public static class BinKindCatalog
{
    public static IReadOnlyList<BinKind> FixedKinds { get; } =
    [
        BinKind.GoodDie, BinKind.DefectDie, BinKind.ReferenceDie, BinKind.OriginalDie,
        BinKind.BlankDie, BinKind.ForBiddenDie, BinKind.MatchFailDie, BinKind.ExceptionDie
    ];

    public static string Text(this BinKind kind) => kind switch
    {
        BinKind.GoodDie => "良品",
        BinKind.DefectDie => "缺陷",
        BinKind.ReferenceDie => "参考",
        BinKind.OriginalDie => "初始",
        BinKind.BlankDie => "无图案",
        BinKind.ForBiddenDie => "外围无效",
        BinKind.MatchFailDie => "匹配失败",
        BinKind.ExceptionDie => "异常",
        _ => kind.ToString()
    };

    /// <summary>BR-44：Bin 代码影响后续检测中不同 Die 显示的类别名称；良品历史一般 GD。</summary>
    public static string DefaultCode(this BinKind kind) => kind switch
    {
        BinKind.GoodDie => "GD",
        BinKind.DefectDie => "DD",
        BinKind.ReferenceDie => "RD",
        BinKind.OriginalDie => "OD",
        BinKind.BlankDie => "BD",
        BinKind.ForBiddenDie => "FD",
        BinKind.MatchFailDie => "MF",
        BinKind.ExceptionDie => "ED",
        _ => kind.ToString()
    };

    /// <summary>BR-44：等级影响显示优先级；0 = 良品（与前道区分合并）。</summary>
    public static int DefaultLevel(this BinKind kind) => kind switch
    {
        BinKind.GoodDie => 0,
        BinKind.ReferenceDie => 1,
        BinKind.OriginalDie => 1,
        BinKind.BlankDie => 2,
        BinKind.MatchFailDie => 2,
        BinKind.DefectDie => 3,
        BinKind.ForBiddenDie => 3,
        BinKind.ExceptionDie => 3,
        _ => 0
    };

    public static string DefaultShortcut(this BinKind kind) => kind switch
    {
        BinKind.GoodDie => "Space",
        BinKind.DefectDie => "D",
        BinKind.ReferenceDie => "R",
        BinKind.OriginalDie => "O",
        BinKind.BlankDie => "B",
        BinKind.ForBiddenDie => "F",
        BinKind.MatchFailDie => "M",
        BinKind.ExceptionDie => "E",
        _ => string.Empty
    };

    public static BinDefinition CreateDefault(this BinKind kind, int index, string colorHex) => new(
        index, kind, kind.DefaultCode(), kind.Text(), kind.DefaultLevel(), colorHex,
        kind.DefaultShortcut(), "系统默认（F-3.7.2-10 备注）");
}

/// <summary>
/// H05 唯一调色板：把历史首页三通道的 4 级颜色收敛到一处（B-11）。
/// MapType Bin 颜色编辑会同步更新对应等级的所有通道颜色，保证「修改后所有消费方同步」（AC-H05-01）。
/// </summary>
public static class MapLevelPalette
{
    public static IReadOnlyList<string> Channels { get; } = ["PL", "EL", "AOI"];

    private static readonly Dictionary<string, string[]> Defaults = new()
    {
        ["PL"] = ["#E9F1FD", "#9CC4F8", "#F5B942", "#E5484D"],
        ["EL"] = ["#E7F4F1", "#8FD9C8", "#F5B942", "#E5484D"],
        ["AOI"] = ["#ECEFFB", "#AEB9F0", "#F5A942", "#DC3F43"]
    };

    public static IReadOnlyList<string> For(string channel)
        => Defaults.TryGetValue(channel ?? string.Empty, out var colors) ? colors : Defaults["PL"];

    public static string ColorFor(string channel, int level)
    {
        var colors = For(channel);
        return colors[Math.Clamp(level, 0, colors.Count - 1)];
    }
}

/// <summary>
/// Bin 定义（F-3.7.2-05~10 / BR-69 七项）：Bincode、Note、Bin（类别名）、快捷键、颜色、等级、数量。
/// <see cref="Kind"/> 取自 8 类固定枚举；其余字段全部可编辑。
/// </summary>
public sealed record BinDefinition(
    int Index,
    BinKind Kind,
    string Code,
    string Name,
    int Level,
    string ColorHex,
    string Shortcut,
    string Note = "",
    int Quantity = 0);

/// <summary>
/// 图谱类型（H05 唯一 Owner，B-16/D-05）：Job 只持有 Id 引用而非副本（B-11）。
/// <c>MapFilePath</c> 对应 BR-45「生成 Map 图需先导入图谱文件」。
/// </summary>
public sealed record MapTypeDefinition(
    string Id,
    string Name,
    IReadOnlyList<BinDefinition> Bins,
    string MapFilePath = "",
    string Description = "");

/// <summary>MapType 目录：H02 图谱选择、H01 着色、H08 复判 Bin 等级与快捷键共同消费（B-11）。</summary>
public interface IMapTypeCatalog
{
    IReadOnlyList<MapTypeDefinition> Types { get; }

    /// <summary>内容变化通知；所有消费方据此刷新，保证只有一份数据（AC-H05-01）。</summary>
    event EventHandler? Changed;

    MapTypeDefinition? Find(string id);

    /// <summary>该图谱类型下尚可启用的固定 Bin 种类（BR-43：不得新增第 9 种）。</summary>
    IReadOnlyList<BinKind> AvailableKinds(string mapTypeId);

    MapTypeDefinition Create(string name);

    MapTypeDefinition Duplicate(string id);

    MapTypeDefinition Rename(string id, string name);

    MapTypeDefinition Update(MapTypeDefinition type);

    /// <summary>T-043：删除保护 + Job 引用检查；被引用时明确拒绝，不静默删除。</summary>
    void Delete(string id);

    void Refresh();

    BinDefinition AddBin(string mapTypeId, BinKind kind);

    void UpdateBin(string mapTypeId, int index, BinDefinition bin);

    void RemoveBin(string mapTypeId, int index);

    /// <summary>AC-H05-02：快捷键解析，供 H08 人工复判消费；未绑定返回 null。</summary>
    BinDefinition? ResolveShortcut(string mapTypeId, string key);

    /// <summary>B-11：H01 首页 Die 按等级着色时读取的唯一颜色来源。</summary>
    string ColorForLevel(string channel, int level);
}

/// <summary>
/// 可编辑的内存图谱库（真实持久化随 H07 配置单点确定后替换）。
/// 种子数据保留历史 Id / 名称，Bin 改为 BR-43 的 8 类固定枚举，颜色沿用三通道 4 级调色板，
/// 因此 H01 首页默认视觉完全不变，但 Bin 颜色编辑会同步反映到所有消费方。
/// </summary>
public class EditableMapTypeCatalog : IMapTypeCatalog
{
    private readonly Func<IReadOnlyList<string>>? _referencedIds;
    private readonly Dictionary<string, string[]> _levelColors = new(StringComparer.Ordinal);
    private readonly List<MapTypeDefinition> _types = [];
    private int _sequence = 3000;

    public EditableMapTypeCatalog(Func<IReadOnlyList<string>>? referencedIds = null)
    {
        _referencedIds = referencedIds;
        foreach (string channel in MapLevelPalette.Channels)
            _levelColors[channel] = MapLevelPalette.For(channel).ToArray();

        _types.Add(new MapTypeDefinition("MAP-PL-300", "PL 标准图谱 · 300mm", StandardBins(),
            "assets/map-samples/aoi-finescan-200mm.csv", "历史种子图谱（8 类 Bin 全量启用）"));
        _types.Add(new MapTypeDefinition("MAP-EL-200", "EL 高增益图谱 · 200mm", StandardBins(),
            "assets/map-samples/aoi-finescan-200mm.csv", "历史种子图谱（8 类 Bin 全量启用）"));
    }

    public IReadOnlyList<MapTypeDefinition> Types
        => _types.OrderBy(t => t.Name, StringComparer.CurrentCulture).ToList();

    public event EventHandler? Changed;

    public MapTypeDefinition? Find(string id)
        => _types.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    public IReadOnlyList<BinKind> AvailableKinds(string mapTypeId)
    {
        var type = Require(mapTypeId);
        return BinKindCatalog.FixedKinds.Where(k => type.Bins.All(b => b.Kind != k)).ToList();
    }

    public MapTypeDefinition Create(string name)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) throw new InvalidOperationException("图谱类型名称必填（F-3.7.2-02）。");
        if (_types.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"同名图谱类型已存在：{name}（T-043：多套并存时名称必须唯一）。");

        var created = new MapTypeDefinition($"MAP-{++_sequence}", name, StandardBins(), string.Empty, "新建图谱类型");
        _types.Add(created);
        Raise();
        return created;
    }

    public MapTypeDefinition Duplicate(string id)
    {
        var source = Require(id);
        string name = $"{source.Name}-副本";
        for (int i = 2; _types.Any(t => t.Name == name); i++) name = $"{source.Name}-副本{i}";

        var copy = source with { Id = $"MAP-{++_sequence}", Name = name, Bins = source.Bins.ToList() };
        _types.Add(copy);
        Raise();
        return copy;
    }

    public MapTypeDefinition Rename(string id, string name)
    {
        var existing = Require(id);
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) throw new InvalidOperationException("图谱类型名称必填。");
        if (_types.Any(t => t.Id != id && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"同名图谱类型已存在：{name}。");

        var renamed = existing with { Name = name };
        _types[_types.IndexOf(existing)] = renamed;
        Raise();
        return renamed;
    }

    public MapTypeDefinition Update(MapTypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Require(type.Id);
        string name = (type.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new InvalidOperationException("图谱类型名称必填。");
        if (_types.Any(t => t.Id != type.Id && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"同名图谱类型已存在：{name}。");

        ValidateBins(type.Bins);
        var updated = type with { Name = name, Bins = type.Bins.ToList() };
        Replace(updated);
        Raise();
        return updated;
    }

    public void Delete(string id)
    {
        var existing = Require(id);
        if (_types.Count == 1)
            throw new InvalidOperationException("至少需要保留 1 套图谱类型（T-043：删除保护）。");

        var referenced = _referencedIds?.Invoke() ?? [];
        if (referenced.Contains(id, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"图谱类型「{existing.Name}」已被 Job 引用，禁止删除（T-043：引用检查；请先修改引用它的 Job）。");

        _types.Remove(existing);
        Raise();
    }

    public void Refresh() => Raise();

    public BinDefinition AddBin(string mapTypeId, BinKind kind)
    {
        var type = Require(mapTypeId);
        if (!BinKindCatalog.FixedKinds.Contains(kind))
            throw new InvalidOperationException("Bin 类型必须属于 BR-43 的 8 种固定枚举，不能新增第 9 种。");
        if (type.Bins.Any(b => b.Kind == kind))
            throw new InvalidOperationException($"Bin 类型「{kind.Text()}」已存在（F-3.7.2-03：增加未启用的类型）。");

        int index = type.Bins.Count == 0 ? 0 : type.Bins.Max(b => b.Index) + 1;
        var bin = kind.CreateDefault(index, MapLevelPalette.ColorFor("PL", kind.DefaultLevel()));
        var bins = type.Bins.ToList();
        bins.Add(bin);
        Replace(type with { Bins = bins });
        Raise();
        return bin;
    }

    public void UpdateBin(string mapTypeId, int index, BinDefinition bin)
    {
        ArgumentNullException.ThrowIfNull(bin);
        var type = Require(mapTypeId);
        var bins = type.Bins.ToList();
        int at = bins.FindIndex(b => b.Index == index);
        if (at < 0) throw new InvalidOperationException($"Bin 代码 {index} 不存在。");

        var old = bins[at];
        // F-3.7.2-05：Bin 类型可改，但必须仍在 8 类固定枚举内且不重复。
        bins[at] = bin with { Index = index };
        ValidateBins(bins);

        // AC-H05-01：Bin 颜色改动同步到所有通道的同等级着色（H01 消费 ColorForLevel）。
        if (!string.Equals(old.ColorHex, bin.ColorHex, StringComparison.OrdinalIgnoreCase))
        {
            foreach (string channel in MapLevelPalette.Channels)
                _levelColors[channel][Math.Clamp(bin.Level, 0, 3)] = bin.ColorHex;
        }
        Replace(type with { Bins = bins });
        Raise();
    }

    public void RemoveBin(string mapTypeId, int index)
    {
        var type = Require(mapTypeId);
        if (type.Bins.Count <= 1)
            throw new InvalidOperationException("至少需要保留 1 个 Bin 代码（F-3.7.2-03）。");
        var bins = type.Bins.Where(b => b.Index != index).ToList();
        if (bins.Count == type.Bins.Count) throw new InvalidOperationException($"Bin 代码 {index} 不存在。");
        Replace(type with { Bins = bins });
        Raise();
    }

    public BinDefinition? ResolveShortcut(string mapTypeId, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var type = Find(mapTypeId);
        return type?.Bins.FirstOrDefault(b =>
            string.Equals(b.Shortcut?.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public string ColorForLevel(string channel, int level)
        => _levelColors.TryGetValue(channel ?? string.Empty, out var colors)
            ? colors[Math.Clamp(level, 0, colors.Length - 1)]
            : MapLevelPalette.ColorFor("PL", level);

    /// <summary>默认 8 类 Bin（BR-43 顺序），颜色按等级取自 PL 调色板，保证首页默认视觉不变。</summary>
    private static IReadOnlyList<BinDefinition> StandardBins()
        => BinKindCatalog.FixedKinds
            .Select((kind, i) => kind.CreateDefault(i, MapLevelPalette.ColorFor("PL", kind.DefaultLevel())))
            .ToList();

    private static void ValidateBins(IReadOnlyList<BinDefinition> bins)
    {
        ArgumentNullException.ThrowIfNull(bins);
        if (bins.Count == 0) throw new InvalidOperationException("至少需要 1 个 Bin 代码（F-3.7.2-04）。");
        if (bins.Count > BinKindCatalog.FixedKinds.Count)
            throw new InvalidOperationException("Bin 类型不得超过 BR-43 的 8 种固定枚举。");
        if (bins.Select(b => b.Kind).Distinct().Count() != bins.Count)
            throw new InvalidOperationException("同一图谱类型内 Bin 类型不可重复（BR-43）。");
        if (bins.Any(b => !BinKindCatalog.FixedKinds.Contains(b.Kind)))
            throw new InvalidOperationException("存在 BR-43 之外的 Bin 类型。");

        foreach (var bin in bins)
        {
            if (string.IsNullOrWhiteSpace(bin.Code))
                throw new InvalidOperationException($"Bin「{bin.Kind.Text()}」代码必填（BR-44）。");
            if (bin.Level is < 0 or > 3)
                throw new InvalidOperationException($"Bin「{bin.Kind.Text()}」等级必须在 0~3 之间（BR-44）。");
            if (!IsColorHex(bin.ColorHex))
                throw new InvalidOperationException($"Bin「{bin.Kind.Text()}」颜色必须为 #RRGGBB 或 #AARRGGBB 格式（F-3.7.2-08）。");
        }

        var codes = bins.Select(b => b.Code.Trim()).ToList();
        if (codes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != codes.Count)
            throw new InvalidOperationException("Bin 代码不可重复（BR-44：代码影响 Die 类别名称）。");

        var shortcuts = bins.Where(b => !string.IsNullOrWhiteSpace(b.Shortcut)).Select(b => b.Shortcut.Trim()).ToList();
        if (shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != shortcuts.Count)
            throw new InvalidOperationException("快捷键不可重复（F-3.7.2-09：供 H08 人工复判使用）。");
    }

    private static bool IsColorHex(string color)
    {
        if (string.IsNullOrWhiteSpace(color) || color[0] != '#') return false;
        if (color.Length is not (7 or 9)) return false;
        return color.Skip(1).All(Uri.IsHexDigit);
    }

    private MapTypeDefinition Require(string id)
        => Find(id ?? string.Empty)
           ?? throw new InvalidOperationException($"图谱类型不存在：{id}（可能已被删除，请刷新）。");

    private void Replace(MapTypeDefinition type)
    {
        int at = _types.FindIndex(t => string.Equals(t.Id, type.Id, StringComparison.Ordinal));
        if (at < 0) throw new InvalidOperationException($"图谱类型不存在：{type.Id}。");
        _types[at] = type;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 兼容别名：P0 基线的 <c>SimulatedMapTypeCatalog</c> 构造签名被既有测试与装配代码引用，
/// 保留为可编辑目录的无引用检查版本，避免历史调用方被破坏（E-01）。
/// 生产组合根注册的是 <see cref="EditableMapTypeCatalog"/>（带 Job 引用检查）。
/// </summary>
public sealed class SimulatedMapTypeCatalog : EditableMapTypeCatalog
{
    public SimulatedMapTypeCatalog() : base(null) { }
}
