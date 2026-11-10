using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WaferInspect.Core.Data;
using WaferInspect.Core.Events;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Navigation;
using WaferInspect.Core.Security;
using WaferInspect.Core.Simulation;
using WaferInspectHMI.Models;
using WaferInspectHMI.Navigation;
using WaferInspectHMI.Services;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.ViewModels;

/// <summary>
/// 主窗口状态编排。属性与命令的公开名称是 XAML 绑定契约，重构期间保持不变：
/// 变更通知由 CommunityToolkit.Mvvm 源生成器产生，依赖由通用主机注入。
/// </summary>
public sealed partial class MainViewModel : ObservableBase, IDisposable
{
    private readonly INavigationCatalog _catalog;
    private readonly INavGroupFactory _navFactory;
    private readonly ISimulationData _simulation;
    private readonly IDataSource _dataSource;
    private readonly IMapRenderer _mapRenderer;
    private readonly ILiveDataPump _pump;
    private readonly IModuleViewModelRegistry _moduleRegistry;
    private readonly IAuthorizationService _authorization;
    private readonly IAlarmCenter _alarmCenter;
    private readonly H01RunViewModel _detection;
    private readonly H02RecipeViewModel _recipes;
    private readonly H04InspectionViewModel _inspection;
    private readonly ILogger<MainViewModel> _logger;
    private readonly List<double> _yield = new();
    private readonly Random _rng = new(20261109);
    private int _tick;
    private bool _disposed;

    private static readonly Dictionary<string, string[]> ChannelColors = new()
    {
        ["PL"]  = new[] { "#E9F1FD", "#9CC4F8", "#F5B942", "#E5484D" },
        ["EL"]  = new[] { "#E7F4F1", "#8FD9C8", "#F5B942", "#E5484D" },
        ["AOI"] = new[] { "#ECEFFB", "#AEB9F0", "#F5A942", "#DC3F43" }
    };

    public MainViewModel(
        INavigationCatalog catalog,
        INavGroupFactory navFactory,
        ISimulationData simulation,
        IDataSource dataSource,
        IMapRenderer mapRenderer,
        ILiveDataPump pump,
        IModuleViewModelRegistry moduleRegistry,
        IAuthorizationService authorization,
        IAlarmCenter alarmCenter,
        H01RunViewModel detection,
        H02RecipeViewModel recipes,
        H04InspectionViewModel inspection,
        ILogger<MainViewModel> logger)
    {
        _catalog = catalog;
        _navFactory = navFactory;
        _simulation = simulation;
        _dataSource = dataSource;
        _mapRenderer = mapRenderer;
        _pump = pump;
        _moduleRegistry = moduleRegistry;
        _authorization = authorization;
        _alarmCenter = alarmCenter;
        _detection = detection;
        _recipes = recipes;
        _inspection = inspection;
        _logger = logger;

        // 模块内导航（例如「配方管理」跳 H02）复用主窗口统一导航入口。
        _detection.NavigationRequested += (_, key) => Navigate(key);

        // H09 最小闭环（P1）：告警中心页展示实时活动报警，蜂鸣器解除 / 手动处理（BR-74/75）。
        _alarmCenter.AlarmsChanged += (_, _) => RefreshLiveAlarms();

        BuildNav();

        // 选中态 → 导航的双向绑定：UIA / 自动化 / 程序化选中导航项与鼠标点击等价
        // （Navigate 内部先改 CurrentView 再同步选中，因此不会形成递归）。
        foreach (var item in NavGroups.SelectMany(g => g.Items))
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(NavItem.IsSelected) && item.IsSelected && CurrentView != item.Key)
                    Navigate(item.Key);
            };
        }

        BuildDevices();
        BuildDefects();
        BuildTasks();
        BuildAlarms();
        BuildYield();
        BuildDies();
        BuildModuleTable("overview");

        ClockText = DateTime.Now.ToString("HH:mm:ss");
        _pump.Tick += OnTick;
    }

    public event EventHandler<string> WindowActionRequested;

    /* ---------------- 绑定契约：命令 ----------------
     * 除 ChannelCommand / WindowCommand 因属性名冲突做别名外，
     * 其余命令均由 [RelayCommand] 源生成器产生同名属性。 */

    /// <summary>通道切换命令（生成器方法名不能与 Channel 属性同名，故用别名）。</summary>
    public ICommand ChannelCommand => SwitchChannelCommand;

    /// <summary>标题栏三态按钮命令（同上，避免与方法 / 类型名冲突）。</summary>
    public ICommand WindowCommand => WindowActionCommand;

    /* ---------------- 绑定契约：可观察状态（源生成器） ---------------- */
    [ObservableProperty] private string _currentView = "overview";
    [ObservableProperty] private string _channel = "PL";
    [ObservableProperty] private string _pageTitle = "运行总览";
    [ObservableProperty] private string _pageCrumb = "工作台 / 运行总览";
    [ObservableProperty] private string _pageSubtitle = "产线 A · FAB-03 · 设备与检测任务实时状态";
    [ObservableProperty] private bool _isOverview = true;
    [ObservableProperty] private bool _isTasks;
    [ObservableProperty] private bool _isRecipes;
    [ObservableProperty] private bool _isDefects;
    [ObservableProperty] private bool _isAlarms;
    [ObservableProperty] private bool _isLive;
    [ObservableProperty] private bool _isGeneric;
    [ObservableProperty] private bool _isModuleSkeleton;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentModuleId))]
    [NotifyPropertyChangedFor(nameof(CurrentModuleTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentModuleStages))]
    private ModuleViewModelBase _currentModule;
    [ObservableProperty] private string _clockText;
    [ObservableProperty] private string _statusMessage = "已连接 · PL-01 / SECS-GEM · 控制器 READY";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseLabel))]
    [NotifyPropertyChangedFor(nameof(RefreshState))]
    private bool _paused;

    [ObservableProperty] private List<DieItem> _dies;
    [ObservableProperty] private int _dieTotal;
    [ObservableProperty] private int _dieNormal;
    [ObservableProperty] private int _dieLight;
    [ObservableProperty] private int _dieWarn;
    [ObservableProperty] private int _dieBad;
    [ObservableProperty] private string _dieYieldText;

    [ObservableProperty] private PointCollection _yieldPoints;
    [ObservableProperty] private PointCollection _areaPoints;
    [ObservableProperty] private double _currentDotX;
    [ObservableProperty] private double _currentDotY;
    [ObservableProperty] private string _currentYieldText = "98.42%";

    [ObservableProperty] private DataTable _moduleTable;

    /* ---------------- 绑定契约：一次性构建的集合与计算属性 ---------------- */
    /// <summary>H01 检测任务页 ViewModel（P1 完整实现），由 DetectionView 直接绑定。</summary>
    public H01RunViewModel Detection => _detection;

    /// <summary>H02 配方与 Job 管理页 ViewModel（P2），由 RecipeView 直接绑定。</summary>
    public H02RecipeViewModel Recipes => _recipes;

    /// <summary>H04 检测算法与 ROI 配置页（P2），由 DefectAnalysisView 直接绑定；与向导共用唯一数据源。</summary>
    public H04InspectionViewModel Inspection => _inspection;

    public List<NavGroup> NavGroups { get; private set; }
    public List<DeviceItem> Devices { get; private set; }
    public List<DefectItem> Defects { get; private set; }
    public List<TaskItem> Tasks { get; private set; }
    public List<AlarmItem> Alarms { get; private set; }

    // 顶栏身份来自登录会话（T-017 统一角色模型）；Roles 按继承序存放，末位为最高角色。
    public string OperatorName => _authorization.CurrentUser?.DisplayName ?? "未登录";
    public string OperatorRole
    {
        get
        {
            var roles = _authorization.CurrentUser?.Roles;
            return roles is { Count: > 0 } ? RoleModel.DisplayNameOf(roles[^1]) : "未登录";
        }
    }
    public string AppVersion => "v3.8.2";
    public string PauseLabel => Paused ? "继续刷新" : "暂停刷新";
    public string RefreshState => Paused ? "已暂停" : "实时刷新中";
    public string CurrentModuleId => CurrentModule?.ModuleId ?? string.Empty;
    public string CurrentModuleTitle => CurrentModule?.DisplayName ?? string.Empty;
    public string CurrentModuleStages => CurrentModule?.MigrationStages ?? string.Empty;

    /* ---------------- KPI ---------------- */
    public string OnlineDevices => "12 / 13 在线";
    public string RunningTasks => "3 个任务";
    public string ShiftYield => "98.42%";
    public string DefectTotal => "1,248 颗";

    public int UnackedCount => Alarms.Count(a => !a.Acked);
    public string AlarmBadgeText => $"{UnackedCount} 条待处理";

    /* ---------------- H09 最小闭环：活动机台报警（BR-74/75） ---------------- */

    /// <summary>来自 <see cref="IAlarmCenter"/> 的实时活动报警；与检测任务页共用同一报警中心。</summary>
    [ObservableProperty] private List<AlarmItem> _liveAlarms = [];

    private void RefreshLiveAlarms()
    {
        LiveAlarms = _alarmCenter.ActiveAlarms.Select(a => new AlarmItem
        {
            Id = a.AlarmId,
            Severity = a.Severity == AlarmSeverity.High ? "高等级" : "低等级",
            Title = a.Message,
            Description = a.Severity == AlarmSeverity.High
                ? "未处理时拦截开始运行（BR-74）"
                : "随蜂鸣器解除一并清除（BR-74）",
            Time = a.StartTime.ToString("HH:mm:ss"),
            SeverityBrush = B(a.Severity == AlarmSeverity.High ? "#C62828" : "#B45309"),
            SeverityBack = B(a.Severity == AlarmSeverity.High ? "#FDECEC" : "#FFF3E0"),
            Acked = false
        }).ToList();
    }

    /// <summary>蜂鸣器解除（F-3.6.1-03）：低等级报警随之一并清除（BR-74）；蜂鸣器/四色灯设备联动搁置（T-025）。</summary>
    [RelayCommand]
    private void BuzzerClear()
    {
        int cleared = _alarmCenter.ClearBuzzer();
        StatusMessage = cleared > 0
            ? $"蜂鸣器解除：{cleared} 条低等级报警一并清除（BR-74）；高等级报警须手动处理"
            : "蜂鸣器解除：当前无可随声音清除的低等级报警";
    }

    /// <summary>手动处理高等级报警（BR-75）；处理后运行拦截自动解除。</summary>
    [RelayCommand]
    private void HandleAlarm(string id)
    {
        if (!_alarmCenter.Handle(id))
        {
            StatusMessage = $"报警 {id} 不存在或已处理";
            return;
        }
        RefreshLiveAlarms();
        StatusMessage = $"报警 {id} 已手动处理（BR-75）";
        if (!_alarmCenter.IsRunBlocked)
            StatusMessage += "；运行拦截已解除";
    }

    /// <summary>EFEM 错误清除入口（F-3.1.2-26，B-18 归 H09）；设备动作搁置（T-029）。</summary>
    [RelayCommand]
    private void EfemErrorClear()
        => StatusMessage = "[搁置] EFEM 错误清除需下发设备指令，设备连接未接入（T-029），指令未下发";

    /* ---------------- 构建数据 ---------------- */
    private static SolidColorBrush B(string hex) => (SolidColorBrush)new BrushConverter().ConvertFrom(hex);

    private void BuildNav() => NavGroups = _navFactory.Create(_catalog.DefaultViewKey);

    private void BuildDevices()
    {
        Devices = new List<DeviceItem>
        {
            new() { Name = "PL 光学检测主机", Code = "PL-01", Status = "运行中", Metrics = "温度 23.4 °C · 产能 42 片/h", StatusBrush = B("#15803D"), StatusBack = B("#E7F6EC") },
            new() { Name = "AOI 外观检测单元", Code = "AOI-02", Status = "运行中", Metrics = "温度 24.1 °C · 产能 58 片/h", StatusBrush = B("#15803D"), StatusBack = B("#E7F6EC") },
            new() { Name = "EL 电致发光单元", Code = "EL-03", Status = "运行中", Metrics = "温度 23.8 °C · 产能 36 片/h", StatusBrush = B("#15803D"), StatusBack = B("#E7F6EC") },
            new() { Name = "EFEM 传输机械手", Code = "EFEM-01", Status = "待机", Metrics = "温度 22.9 °C · 产能 —", StatusBrush = B("#B45309"), StatusBack = B("#FFF3E0") },
            new() { Name = "真空泵组", Code = "VAC-02", Status = "故障", Metrics = "温度 41.2 °C · 真空 8.2e-3 Pa", StatusBrush = B("#C62828"), StatusBack = B("#FDECEC") }
        };
    }

    private void BuildDefects()
    {
        Defects = new List<DefectItem>
        {
            new() { Name = "颗粒", Count = "528", Pct = 42.3, PctText = "42.3%", BarBrush = B("#2F6FE4") },
            new() { Name = "划痕", Count = "337", Pct = 27.0, PctText = "27.0%", BarBrush = B("#38A2C9") },
            new() { Name = "图形缺陷", Count = "225", Pct = 18.0, PctText = "18.0%", BarBrush = B("#E0A22B") },
            new() { Name = "污染", Count = "112", Pct = 9.0, PctText = "9.0%", BarBrush = B("#E0685C") },
            new() { Name = "其他", Count = "46", Pct = 3.7, PctText = "3.7%", BarBrush = B("#93A3B8") }
        };
    }

    private void BuildTasks()
    {
        Tasks = new List<TaskItem>
        {
            Row("TSK-261109-0187", "LOT-261109-A05", "WFR-20261109-039", "PL-STD-300mm-R3", "CH-01", 100, "09:12:04", "已完成", false),
            Row("TSK-261109-0188", "LOT-261109-A06", "WFR-20261109-040", "PL-STD-300mm-R3", "CH-02", 100, "09:34:41", "已完成", false),
            Row("TSK-261109-0189", "LOT-261109-A06", "WFR-20261109-041", "AOI-FINESCAN-R5", "CH-01", 100, "09:58:12", "已完成", false),
            Row("TSK-261109-0190", "LOT-261109-A07", "WFR-20261109-042", "PL-STD-300mm-R3", "CH-01", 74, "10:28:16", "运行中", true),
            Row("TSK-261109-0191", "LOT-261109-A07", "WFR-20261109-043", "EL-HIGHGAIN-R2", "CH-03", 46, "10:31:52", "运行中", true),
            Row("TSK-261109-0192", "LOT-261109-A08", "WFR-20261109-044", "AOI-FINESCAN-R5", "CH-02", 21, "10:36:09", "运行中", true),
            Row("TSK-261109-0193", "LOT-261109-A08", "WFR-20261109-045", "PL-STD-300mm-R3", "CH-03", 38, "10:39:47", "异常暂停", false),
            Row("TSK-261109-0194", "LOT-261109-A09", "WFR-20261109-046", "PL-STD-300mm-R3", "—", 0, "待调度", "排队中", false),
            Row("TSK-261109-0195", "LOT-261109-A09", "WFR-20261109-047", "EL-HIGHGAIN-R2", "—", 0, "待调度", "排队中", false)
        };
    }

    private static TaskItem Row(string id, string lot, string wafer, string recipe, string chamber,
        double progress, string start, string status, bool running)
    {
        var (fg, bg) = status switch
        {
            "运行中"   => ("#2563EB", "#EAF2FF"),
            "已完成"   => ("#15803D", "#E7F6EC"),
            "异常暂停" => ("#C62828", "#FDECEC"),
            "排队中"   => ("#46566C", "#EEF2F7"),
            _          => ("#B45309", "#FFF3E0")
        };
        return new TaskItem
        {
            Id = id, Lot = lot, Wafer = wafer, Recipe = recipe, Chamber = chamber,
            Progress = progress, Start = start, StatusText = status, IsRunning = running,
            StatusBrush = B(fg), StatusBack = B(bg)
        };
    }

    private void BuildAlarms()
    {
        Alarms = new List<AlarmItem>
        {
            Alm("AL-2411", "严重", "腔体真空度超限", "PL-01 · CH-01 · E-2041 · 真空度 8.2×10⁻³ Pa", "10:41:26", "#C62828", "#FDECEC", false),
            Alm("AL-2410", "警告", "检测结果良率低于控制下限", "AOI-02 · W-1183 · WFR-045 晶粒良率 96.8%", "10:38:04", "#B45309", "#FFF3E0", false),
            Alm("AL-2409", "警告", "激光器功率波动", "EL-03 · W-0927 · 输出功率波动 ±3.4%", "10:32:51", "#B45309", "#FFF3E0", false),
            Alm("AL-2408", "提示", "配方版本已更新", "RECIPE-SVC · I-0312 · 已同步至全部腔体", "10:18:33", "#2563EB", "#EAF2FF", true),
            Alm("AL-2407", "提示", "设备例行自检完成", "EFEM-01 · I-0288 · 定位偏差 0.012 mm", "09:55:10", "#2563EB", "#EAF2FF", true)
        };
    }

    private static AlarmItem Alm(string id, string sev, string title, string desc, string time, string fg, string bg, bool acked)
        => new()
        {
            Id = id, Severity = sev, Title = title, Description = desc, Time = time,
            SeverityBrush = B(fg), SeverityBack = B(bg), Acked = acked
        };

    private void BuildYield()
    {
        _yield.AddRange(_dataSource.CreateYieldSeries());
        RebuildChart();
    }

    private void RebuildChart()
    {
        var pts = new PointCollection();
        for (int i = 0; i < _yield.Count; i++)
            pts.Add(new Point(i * 560.0 / (_yield.Count - 1), _simulation.ChartY(_yield[i])));
        YieldPoints = pts;

        var area = new PointCollection();
        foreach (var p in pts) area.Add(p);
        area.Add(new Point(560, 118));
        area.Add(new Point(0, 118));
        AreaPoints = area;

        CurrentDotX = 560;
        CurrentDotY = _simulation.ChartY(_yield[^1]);
        CurrentYieldText = $"{_yield[^1]:F2}%";
    }

    private void BuildDies()
    {
        int ci = Channel switch { "PL" => 0, "EL" => 1, _ => 2 };
        var colors = ChannelColors[Channel].Select(B).ToArray();
        var map = _mapRenderer.Render(new MapRenderRequest(ci, Channel));
        string[] labels = ["正常", "轻微", "警告", "缺陷"];

        Dies = map.Cells.Select(cell => new DieItem
        {
            X = cell.X, Y = cell.Y, Level = cell.Level, Fill = colors[cell.Level],
            Tip = $"R{cell.Row + 1:00} · C{cell.Column + 1:00}   {labels[cell.Level]}   通道 {Channel}"
        }).ToList();
        DieTotal = map.Total; DieNormal = map.Normal; DieLight = map.Light;
        DieWarn = map.Warning; DieBad = map.Bad;
        DieYieldText = $"{map.YieldPercentage:F2}%";
    }

    private void BuildModuleTable(string view)
    {
        var t = new DataTable("Module");
        void Cols(params string[] names) { foreach (var n in names) t.Columns.Add(n); }

        switch (view)
        {
            case "wafer":
                Cols("晶粒坐标", "检测通道", "缺陷等级", "缺陷颗数", "判定");
                foreach (var d in Dies.Where(d => d.Level >= 2).Take(18))
                    t.Rows.Add(d.Tip.Split("   ")[0], Channel, d.Level == 3 ? "缺陷" : "警告", d.Level * 3 + 2, d.Level == 3 ? "需复检" : "观察");
                break;
            case "defects":
                Cols("缺陷编号", "晶圆 / 坐标", "分类", "尺寸", "置信度", "判定");
                t.Rows.Add("DF-00421", "WFR-042 / R07C14", "颗粒团聚", "12.4 µm", "98.7%", "需复检");
                t.Rows.Add("DF-00402", "WFR-041 / R12C08", "划痕", "84 µm", "99.1%", "判废");
                t.Rows.Add("DF-00381", "WFR-040 / R18C11", "污染", "26 µm", "96.8%", "判废");
                break;
            case "reports":
                Cols("报告编号", "批次 / 晶圆", "晶粒良率", "缺陷数", "生成时间", "审核状态");
                t.Rows.Add("RPT-261109-0088", "LOT-A05 / WFR-039", "98.12%", "86", "10:02:18", "已审核");
                t.Rows.Add("RPT-261109-0090", "LOT-A06 / WFR-041", "96.80%", "128", "10:24:02", "待复检");
                t.Rows.Add("RPT-261109-0091", "LOT-A07 / WFR-042", "98.42%", "63", "10:38:55", "待审核");
                break;
            case "devices":
                Cols("设备名称", "编号", "状态", "温度", "产能", "连续运行");
                foreach (var d in Devices) t.Rows.Add(d.Name, d.Code, d.Status, d.Metrics.Split('·')[0].Trim(), d.Metrics.Contains("片/h") ? d.Metrics.Split('·')[1].Trim() : "—", d.Status == "故障" ? "—" : "18 d 06 h");
                break;
            case "recipes":
                Cols("配方编号", "名称", "通道", "版本", "曝光时间", "控制下限", "状态");
                t.Rows.Add("RCP-0012", "PL-STD-300mm-R3", "PL", "R3", "120 ms", "LCL 97.2%", "已发布");
                t.Rows.Add("RCP-0013", "EL-HIGHGAIN-R2", "EL", "R2", "240 ms", "LCL 96.8%", "已发布");
                t.Rows.Add("RCP-0014", "AOI-FINESCAN-R5", "AOI", "R5", "80 ms", "LCL 97.5%", "已发布");
                break;
            case "calibration":
                Cols("标定项目", "设备", "最近执行", "结果", "有效期");
                t.Rows.Add("暗场校正", "PL-01", "2026-11-09 08:12", "合格", "2026-11-16");
                t.Rows.Add("像素标定", "AOI-02", "2026-11-09 08:24", "合格", "2026-11-16");
                t.Rows.Add("激光功率标定", "EL-03", "2026-11-08 19:40", "待执行", "—");
                t.Rows.Add("真空计校验", "VAC-02", "2026-11-07 14:08", "超期", "2026-11-08");
                break;
            case "users":
                Cols("工号", "姓名", "角色", "权限组", "最近登录", "状态");
                t.Rows.Add("E10247", "陈明远", "工艺工程师", "ENGINEER", "2026-11-09 08:02", "在线");
                t.Rows.Add("E10312", "李文博", "设备工程师", "MAINTENANCE", "2026-11-09 09:14", "在线");
                t.Rows.Add("E10088", "王海燕", "产线主管", "SUPERVISOR", "2026-11-08 20:41", "离线");
                break;
            case "settings":
                Cols("设置项", "说明", "当前值", "作用范围");
                t.Rows.Add("实时数据自动刷新", "以 1 Hz 拉取设备数据", "启用", "当前操作员");
                t.Rows.Add("告警声音提示", "严重告警播放提示音", "启用", "全局");
                t.Rows.Add("异常自动暂停任务", "突破控制下限即暂停", "关闭", "产线 A");
                break;
            default:
                Cols("项目", "状态", "说明");
                t.Rows.Add("产线 A", "运行中", "FAB-03 · 12 / 13 台设备在线");
                break;
        }
        ModuleTable = t;
    }

    /* ---------------- 行为：命令由 [RelayCommand] 生成 ---------------- */
    [RelayCommand]
    private void Navigate(string key)
    {
        CurrentView = key;
        foreach (var item in NavGroups.SelectMany(g => g.Items)) item.IsSelected = item.Key == key;

        // 折叠不影响选中态：目标视图若位于已收起的分组，导航时自动展开，保证选中项可见。
        var targetKey = _catalog.GroupKeyOf(key);
        if (targetKey != null)
        {
            var targetGroup = NavGroups.FirstOrDefault(g => g.Key == targetKey);
            if (targetGroup != null && !targetGroup.IsExpanded) targetGroup.IsExpanded = true;
        }
        foreach (var group in NavGroups) group.Raise(nameof(NavGroup.HasSelection));

        var page = _catalog.PageOf(key) ?? _catalog.PageOf(_catalog.DefaultViewKey);
        PageTitle = page.Title;
        PageCrumb = page.Crumb;
        PageSubtitle = page.Subtitle;
        CurrentModule = _moduleRegistry.ForView(key);

        IsOverview = key == "overview";
        IsTasks = key == "tasks";
        IsRecipes = key == "recipes";
        IsDefects = key == "defects";
        IsAlarms = key == "alarms";
        IsLive = key == "live";
        IsModuleSkeleton = page.IsMigrationSkeleton;
        IsGeneric = !(IsOverview || IsTasks || IsRecipes || IsDefects || IsAlarms || IsLive || IsModuleSkeleton);
        if (IsGeneric) BuildModuleTable(key);

        StatusMessage = $"当前视图：{PageCrumb}";
        _logger.LogInformation("导航切换：{ViewKey}（{PageTitle}）", key, PageTitle);
    }

    /// <summary>切换单个分组的展开状态；选中项与当前视图保持不变。</summary>
    [RelayCommand]
    private void ToggleGroup(string key)
    {
        var group = NavGroups.FirstOrDefault(g => g.Key == key);
        if (group == null) return;

        group.IsExpanded = !group.IsExpanded;
        StatusMessage = group.IsExpanded
            ? $"已展开导航分组：{group.Title}"
            : $"已收起导航分组：{group.Title}（选中项保持不变）";
    }

    [RelayCommand]
    private void Ack(string id)
    {
        var a = Alarms.FirstOrDefault(x => x.Id == id);
        if (a == null || a.Acked) return;
        a.Acked = true;
        RefreshAlarmState();
        StatusMessage = $"告警 {id} 已确认，剩余 {UnackedCount} 条待处理";
    }

    private bool CanAckAll => UnackedCount > 0;

    [RelayCommand(CanExecute = nameof(CanAckAll))]
    private void AckAll()
    {
        foreach (var a in Alarms.Where(x => !x.Acked)) a.Acked = true;
        RefreshAlarmState();
        StatusMessage = "已确认全部告警，当前无待处理告警";
    }

    private void RefreshAlarmState()
    {
        Raise(nameof(UnackedCount)); Raise(nameof(AlarmBadgeText));
        AckAllCommand.NotifyCanExecuteChanged();
        var badge = NavGroups.SelectMany(g => g.Items).FirstOrDefault(n => n.Key == "alarms");
        if (badge != null) badge.Badge = UnackedCount.ToString();
    }

    [RelayCommand]
    private void NewTask()
    {
        string id = $"TSK-261109-{196 + Tasks.Count:0000}";
        Tasks.Insert(0, Row(id, "LOT-261109-A09", $"WFR-20261109-{48 + Tasks.Count:000}", "PL-STD-300mm-R3", "—", 0, DateTime.Now.ToString("HH:mm:ss"), "排队中", false));
        Raise(nameof(Tasks));
        StatusMessage = $"检测任务 {id} 已创建并进入排队队列";
    }

    [RelayCommand]
    private void Pause()
    {
        Paused = !Paused;
        StatusMessage = Paused ? "实时数据刷新已暂停" : "实时数据刷新已恢复";
    }

    [RelayCommand]
    private void SwitchChannel(string ch)
    {
        if (!ChannelColors.ContainsKey(ch)) return;
        Channel = ch;
        BuildDies();
        if (CurrentView == "wafer") BuildModuleTable("wafer");
        StatusMessage = $"已切换至 {ch} 通道图谱";
    }

    [RelayCommand]
    private void WindowAction(string action)
        => WindowActionRequested?.Invoke(this, action ?? string.Empty);

    [RelayCommand]
    private void Export()
        => StatusMessage = "导出任务已创建，文件将保存至 D:\\Reports";

    public void ShowNotification(string message) => StatusMessage = message;

    private void OnTick(object sender, EventArgs e)
    {
        _tick++;
        ClockText = DateTime.Now.ToString("HH:mm:ss");
        if (Paused) return;

        double last = _yield[^1];
        double next = _dataSource.NextYield(last, _rng);
        _yield.Add(next); _yield.RemoveAt(0);
        RebuildChart();

        if (_tick % 2 == 0)
            foreach (var t in Tasks.Where(t => t.IsRunning && t.Progress < 100))
                t.Progress = Math.Min(100, t.Progress + _rng.NextDouble() * 0.8);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pump.Tick -= OnTick;
    }
}
