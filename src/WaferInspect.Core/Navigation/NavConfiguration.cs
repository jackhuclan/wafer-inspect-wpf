namespace WaferInspect.Core.Navigation;

/// <summary>
/// 菜单项定义。IconPath 是 SVG 路径字符串，由 App 层转换为 Geometry；
/// 配置层不引用任何 UI / Windows 类型，可在 net10.0 下独立单测。
/// </summary>
public sealed record NavItemDefinition(
    string Key,
    string Label,
    string IconPath,
    string? Badge = null);

/// <summary>菜单组定义；DefaultExpanded 决定首次加载时的展开状态。</summary>
public sealed record NavGroupDefinition(
    string Key,
    string Title,
    bool DefaultExpanded,
    IReadOnlyList<NavItemDefinition> Items);

/// <summary>左侧导航的单一配置源：分组、组内项、默认展开状态与默认视图。</summary>
public static class NavConfiguration
{
    public const string DefaultViewKey = "overview";

    public static IReadOnlyList<NavGroupDefinition> Groups { get; } =
    [
        new NavGroupDefinition("workbench", "工作台", true,
        [
            new NavItemDefinition("overview", "运行总览", "M3 12h4l3-7 4 14 3-7h4"),
            new NavItemDefinition("live", "实时监控", "M2 12h4l3-6 4 12 3-6h6", "LIVE")
        ]),
        new NavGroupDefinition("business", "检测业务", true,
        [
            new NavItemDefinition("tasks", "检测任务", "M3 4h18v16H3z M8 9h8 M8 13h8 M8 17h5", "3"),
            new NavItemDefinition("wafer", "晶圆地图", "M3 12h18 M12 3v18 M5 7l14 10 M19 7L5 17"),
            new NavItemDefinition("defects", "缺陷分析", "M4 4h16v16H4z M9 9h6v6H9z"),
            new NavItemDefinition("analysis", "数据分析", "M4 19V5 M4 19h16 M8 15l3-4 3 2 5-7"),
            new NavItemDefinition("reports", "结果报表", "M4 20V4 M4 20h16 M8 16v-4 M12 16V8 M16 16v-6 M20 16V7")
        ]),
        new NavGroupDefinition("devices", "设备与配置", true,
        [
            new NavItemDefinition("devices", "设备管理",
                "M5 5h14v14H5z M9 9h6v6H9z M9 2v3 M15 2v3 M9 19v3 M15 19v3 M2 9h3 M2 15h3 M19 9h3 M19 15h3"),
            new NavItemDefinition("recipes", "配方管理", "M4 7h16 M4 12h16 M4 17h16 M9 4v6 M15 10v6"),
            new NavItemDefinition("calibration", "标定与校验", "M12 3l8 3v6h-8V3z M4 12h16v9H4z M9 16h6"),
            new NavItemDefinition("maps", "图谱管理", "M4 4h16v16H4z M4 10h16 M10 4v16 M15 10l5 5 M15 15h5"),
            new NavItemDefinition("waferscan", "Wafer 扫描", "M12 2a10 10 0 100 20 10 10 0 000-20z M12 6a6 6 0 100 12 6 6 0 000-12z M12 10a2 2 0 100 4 2 2 0 000-4z")
        ]),
        new NavGroupDefinition("system", "系统", false,
        [
            new NavItemDefinition("alarms", "告警中心", "M6 16h12l-2-3V9H8v4l-2 3z M10 19h4", "3"),
            new NavItemDefinition("users", "用户与权限", "M6 5h6v6H6z M3 20v-2h12v2 M17 7h4v4h-4z M16 20v-2h6v2"),
            new NavItemDefinition("settings", "系统设置", "M4 7h16 M4 12h16 M4 17h16 M9 4v6 M15 10v6")
        ])
    ];

    /// <summary>返回某个菜单项所属的组 Key，用于折叠组内的视图被导航时自动展开该组。</summary>
    public static string? GroupKeyOf(string itemKey)
        => Groups.FirstOrDefault(g => g.Items.Any(i => i.Key == itemKey))?.Key;
}
