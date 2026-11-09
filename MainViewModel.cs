using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WaferInspectHMI;

/// <summary>轻量 ICommand 实现，避免引入额外 MVVM 依赖。</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object> _execute;
    private readonly Func<object, bool> _canExecute;

    public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object parameter) => _execute(parameter);
}

public sealed class MainViewModel : Observable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<double> _yield = new();
    private readonly Random _rng = new(20261109);
    private int _tick;
    private string _currentView = "overview";
    private string _channel = "PL";

    private static readonly Dictionary<string, string[]> ChannelColors = new()
    {
        ["PL"]  = new[] { "#E9F1FD", "#9CC4F8", "#F5B942", "#E5484D" },
        ["EL"]  = new[] { "#E7F4F1", "#8FD9C8", "#F5B942", "#E5484D" },
        ["AOI"] = new[] { "#ECEFFB", "#AEB9F0", "#F5A942", "#DC3F43" }
    };

    public MainViewModel()
    {
        BuildNav();
        BuildDevices();
        BuildDefects();
        BuildTasks();
        BuildAlarms();
        BuildYield();
        BuildDies();
        BuildModuleTable("overview");

        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? "overview"));
        AckCommand = new RelayCommand(p => Ack(p as string));
        AckAllCommand = new RelayCommand(_ => AckAll(), _ => UnackedCount > 0);
        NewTaskCommand = new RelayCommand(_ => AddTask());
        PauseCommand = new RelayCommand(_ => TogglePause());
        ChannelCommand = new RelayCommand(p => SetChannel(p as string ?? "PL"));
        WindowCommand = new RelayCommand(p => WindowActionRequested?.Invoke(this, p as string ?? ""));
        ExportCommand = new RelayCommand(_ => StatusMessage = "导出任务已创建，文件将保存至 D:\\Reports");

        _timer.Tick += OnTick;
        _timer.Start();
        ClockText = DateTime.Now.ToString("HH:mm:ss");
    }

    public event EventHandler<string> WindowActionRequested;

    /* ---------------- 命令 ---------------- */
    public ICommand NavigateCommand { get; }
    public ICommand AckCommand { get; }
    public ICommand AckAllCommand { get; }
    public ICommand NewTaskCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ChannelCommand { get; }
    public ICommand WindowCommand { get; }
    public ICommand ExportCommand { get; }

    /* ---------------- 导航与视图状态 ---------------- */
    public List<NavGroup> NavGroups { get; private set; }
    public string CurrentView => _currentView;
    public string PageTitle { get; private set; } = "运行总览";
    public string PageCrumb { get; private set; } = "工作台 / 运行总览";
    public string PageSubtitle { get; private set; } = "产线 A · FAB-03 · 设备与检测任务实时状态";
    public bool IsOverview { get; private set; } = true;
    public bool IsTasks { get; private set; }
    public bool IsAlarms { get; private set; }
    public bool IsLive { get; private set; }
    public bool IsGeneric { get; private set; }

    /* ---------------- 顶栏与状态栏 ---------------- */
    public string ClockText { get; private set; }
    public string OperatorName => "陈工";
    public string OperatorRole => "工艺工程师";
    public string AppVersion => "v3.8.2";
    public string StatusMessage { get; private set; } = "已连接 · PL-01 / SECS-GEM · 控制器 READY";
    public bool Paused { get; private set; }
    public string PauseLabel => Paused ? "继续刷新" : "暂停刷新";
    public string RefreshState => Paused ? "已暂停" : "实时刷新中";

    /* ---------------- KPI ---------------- */
    public string OnlineDevices => "12 / 13 在线";
    public string RunningTasks => "3 个任务";
    public string ShiftYield => "98.42%";
    public string DefectTotal => "1,248 颗";

    /* ---------------- 晶圆图 ---------------- */
    public List<DieItem> Dies { get; private set; }
    public string Channel => _channel;
    public int DieTotal { get; private set; }
    public int DieNormal { get; private set; }
    public int DieLight { get; private set; }
    public int DieWarn { get; private set; }
    public int DieBad { get; private set; }
    public string DieYieldText { get; private set; }

    /* ---------------- 实时曲线 ---------------- */
    public PointCollection YieldPoints { get; private set; }
    public PointCollection AreaPoints { get; private set; }
    public double CurrentDotX { get; private set; }
    public double CurrentDotY { get; private set; }
    public string CurrentYieldText { get; private set; } = "98.42%";

    /* ---------------- 列表数据 ---------------- */
    public List<DeviceItem> Devices { get; private set; }
    public List<DefectItem> Defects { get; private set; }
    public List<TaskItem> Tasks { get; private set; }
    public List<AlarmItem> Alarms { get; private set; }
    public DataTable ModuleTable { get; private set; }
    public int UnackedCount => Alarms.Count(a => !a.Acked);
    public string AlarmBadgeText => $"{UnackedCount} 条待处理";

    /* ---------------- 构建数据 ---------------- */
    private static Geometry G(string path) => Geometry.Parse(path);

    private void BuildNav()
    {
        NavGroups = new List<NavGroup>
        {
            new() { Title = "工作台", Items = new List<NavItem>
            {
                new() { Key = "overview", Label = "运行总览", Icon = G("M3 12h4l3-7 4 14 3-7h4"), IsSelected = true },
                new() { Key = "live",     Label = "实时监控", Icon = G("M2 12h4l3-6 4 12 3-6h6"), Badge = "LIVE" }
            }},
            new() { Title = "检测业务", Items = new List<NavItem>
            {
                new() { Key = "tasks",    Label = "检测任务", Icon = G("M3 4h18v16H3z M8 9h8 M8 13h8 M8 17h5"), Badge = "3" },
                new() { Key = "wafer",    Label = "晶圆地图", Icon = G("M3 12h18 M12 3v18 M5 7l14 10 M19 7L5 17") },
                new() { Key = "defects",  Label = "缺陷分析", Icon = G("M4 4h16v16H4z M9 9h6v6H9z") },
                new() { Key = "reports",  Label = "结果报表", Icon = G("M4 20V4 M4 20h16 M8 16v-4 M12 16V8 M16 16v-6 M20 16V7") }
            }},
            new() { Title = "设备与配置", Items = new List<NavItem>
            {
                new() { Key = "devices",      Label = "设备管理",   Icon = G("M5 5h14v14H5z M9 9h6v6H9z M9 2v3 M15 2v3 M9 19v3 M15 19v3 M2 9h3 M2 15h3 M19 9h3 M19 15h3") },
                new() { Key = "recipes",      Label = "配方管理",   Icon = G("M4 7h16 M4 12h16 M4 17h16 M9 4v6 M15 10v6") },
                new() { Key = "calibration",  Label = "标定与校验", Icon = G("M12 3l8 3v6h-8V3z M4 12h16v9H4z M9 16h6") }
            }},
            new() { Title = "系统", Items = new List<NavItem>
            {
                new() { Key = "alarms",   Label = "告警中心",   Icon = G("M6 16h12l-2-3V9H8v4l-2 3z M10 19h4"), Badge = "3" },
                new() { Key = "users",    Label = "用户与权限", Icon = G("M6 5h6v6H6z M3 20v-2h12v2 M17 7h4v4h-4z M16 20v-2h6v2") },
                new() { Key = "settings", Label = "系统设置",   Icon = G("M4 7h16 M4 12h16 M4 17h16 M9 4v6 M15 10v6") }
            }}
        };
    }

    private static SolidColorBrush B(string hex) => (SolidColorBrush)new BrushConverter().ConvertFrom(hex);

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

    private static double Rand(double n)
    {
        double x = Math.Sin(n * 127.1 + 0.73) * 43758.5453;
        return x - Math.Floor(x);
    }

    private void BuildYield()
    {
        for (int i = 0; i < 61; i++)
        {
            double v = 98.28 + Math.Sin(i / 6.5) * 0.20 + Math.Sin(i / 2.3) * 0.07 + (Rand(i * 3 + 1) - .5) * 0.09;
            _yield.Add(Math.Clamp(v, 97.5, 99.5));
        }
        _yield[18] = 97.31;
        _yield[19] = 97.86;
        _yield[60] = 98.42;
        RebuildChart();
    }

    private static double ChartY(double v) => 118 - (v - 96.5) / 3.5 * 104;

    private void RebuildChart()
    {
        var pts = new PointCollection();
        for (int i = 0; i < _yield.Count; i++)
            pts.Add(new Point(i * 560.0 / (_yield.Count - 1), ChartY(_yield[i])));
        YieldPoints = pts;

        var area = new PointCollection();
        foreach (var p in pts) area.Add(p);
        area.Add(new Point(560, 118));
        area.Add(new Point(0, 118));
        AreaPoints = area;

        CurrentDotX = 560;
        CurrentDotY = ChartY(_yield[^1]);
        CurrentYieldText = $"{_yield[^1]:F2}%";
        Raise(nameof(YieldPoints)); Raise(nameof(AreaPoints));
        Raise(nameof(CurrentDotX)); Raise(nameof(CurrentDotY)); Raise(nameof(CurrentYieldText));
    }

    private void BuildDies()
    {
        int ci = _channel switch { "PL" => 0, "EL" => 1, _ => 2 };
        var colors = ChannelColors[_channel].Select(B).ToArray();
        const int N = 21;
        const double pitch = 10.9, size = 9.2, cx = 120, cy = 120, R = 113;
        var list = new List<DieItem>();
        int[] counts = new int[4];

        for (int r = 0; r < N; r++)
        for (int c = 0; c < N; c++)
        {
            double x = cx + (c - (N - 1) / 2.0) * pitch;
            double y = cy + (r - (N - 1) / 2.0) * pitch;
            double dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (dist > R - 4) continue;
            double seed = Rand((r * 31 + c * 7) * 13 + ci * 97);
            double edge = dist / R;
            int lv = seed < 0.80 - 0.16 * edge ? 0 : seed < 0.925 - 0.09 * edge ? 1
                   : seed < 0.982 - 0.035 * edge ? 2 : 3;
            double cluster = Math.Sqrt((x - (cx + 40)) * (x - (cx + 40)) + (y - (cy - 32)) * (y - (cy - 32)));
            if (cluster < 23 && Rand((int)(seed * 997) + ci * 13) > 0.34)
                lv = Rand(seed * 311) > 0.46 ? 3 : 2;

            counts[lv]++;
            list.Add(new DieItem
            {
                X = x - size / 2, Y = y - size / 2, Level = lv, Fill = colors[lv],
                Tip = $"R{r + 1:00} · C{c + 1:00}   {new[] { "正常", "轻微", "警告", "缺陷" }[lv]}   通道 {_channel}"
            });
        }

        Dies = list;
        DieTotal = counts.Sum(); DieNormal = counts[0]; DieLight = counts[1];
        DieWarn = counts[2]; DieBad = counts[3];
        DieYieldText = $"{((1 - counts[3] / (double)DieTotal) * 100):F2}%";
        Raise(nameof(Dies)); Raise(nameof(DieTotal)); Raise(nameof(DieNormal));
        Raise(nameof(DieLight)); Raise(nameof(DieWarn)); Raise(nameof(DieBad)); Raise(nameof(DieYieldText));
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
                    t.Rows.Add(d.Tip.Split("   ")[0], _channel, d.Level == 3 ? "缺陷" : "警告", d.Level * 3 + 2, d.Level == 3 ? "需复检" : "观察");
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
        Raise(nameof(ModuleTable));
    }

    /* ---------------- 行为 ---------------- */
    private void Navigate(string key)
    {
        _currentView = key;
        foreach (var item in NavGroups.SelectMany(g => g.Items)) item.IsSelected = item.Key == key;

        (PageTitle, PageCrumb, PageSubtitle) = key switch
        {
            "live"        => ("实时监控", "工作台 / 实时监控", "产线 A · 全部检测通道 1 Hz 实时数据流"),
            "tasks"       => ("检测任务", "检测业务 / 检测任务", "任务队列、执行进度与检测结果追溯"),
            "wafer"       => ("晶圆地图", "检测业务 / 晶圆地图", "多通道晶粒级缺陷密度与坐标定位"),
            "defects"     => ("缺陷分析", "检测业务 / 缺陷分析", "缺陷分类、趋势与帕累托分析"),
            "reports"     => ("结果报表", "检测业务 / 结果报表", "检测报告生成、审核与归档"),
            "devices"     => ("设备管理", "设备与配置 / 设备管理", "13 台设备 · 12 台在线 · 1 台故障"),
            "recipes"     => ("配方管理", "设备与配置 / 配方管理", "检测参数模板、版本与下发状态"),
            "calibration" => ("标定与校验", "设备与配置 / 标定与校验", "量测溯源、标定计划与合规状态"),
            "alarms"      => ("告警中心", "系统 / 告警中心", "告警分级、确认闭环与事件追溯"),
            "users"       => ("用户与权限", "系统 / 用户与权限", "操作员账号、角色与访问授权"),
            "settings"    => ("系统设置", "系统 / 系统设置", "运行参数、通知与数据策略"),
            _             => ("运行总览", "工作台 / 运行总览", "产线 A · FAB-03 · 设备与检测任务实时状态")
        };

        IsOverview = key == "overview";
        IsTasks = key == "tasks";
        IsAlarms = key == "alarms";
        IsLive = key == "live";
        IsGeneric = !(IsOverview || IsTasks || IsAlarms || IsLive);
        if (IsGeneric) BuildModuleTable(key);

        foreach (var n in new[] { nameof(CurrentView), nameof(PageTitle), nameof(PageCrumb), nameof(PageSubtitle),
                                 nameof(IsOverview), nameof(IsTasks), nameof(IsAlarms), nameof(IsLive), nameof(IsGeneric) })
            Raise(n);
        StatusMessage = $"当前视图：{PageCrumb}";
        Raise(nameof(StatusMessage));
    }

    private void Ack(string id)
    {
        var a = Alarms.FirstOrDefault(x => x.Id == id);
        if (a == null || a.Acked) return;
        a.Acked = true;
        RefreshAlarmState();
        StatusMessage = $"告警 {id} 已确认，剩余 {UnackedCount} 条待处理";
        Raise(nameof(StatusMessage));
    }

    private void AckAll()
    {
        foreach (var a in Alarms.Where(x => !x.Acked)) a.Acked = true;
        RefreshAlarmState();
        StatusMessage = "已确认全部告警，当前无待处理告警";
        Raise(nameof(StatusMessage));
    }

    private void RefreshAlarmState()
    {
        Raise(nameof(UnackedCount)); Raise(nameof(AlarmBadgeText));
        var badge = NavGroups.SelectMany(g => g.Items).FirstOrDefault(n => n.Key == "alarms");
        if (badge != null) { badge.Badge = UnackedCount.ToString(); badge.Raise(nameof(NavItem.Badge)); badge.Raise(nameof(NavItem.BadgeVisibility)); }
    }

    private void AddTask()
    {
        string id = $"TSK-261109-{196 + Tasks.Count:0000}";
        Tasks.Insert(0, Row(id, "LOT-261109-A09", $"WFR-20261109-{48 + Tasks.Count:000}", "PL-STD-300mm-R3", "—", 0, DateTime.Now.ToString("HH:mm:ss"), "排队中", false));
        Raise(nameof(Tasks));
        StatusMessage = $"检测任务 {id} 已创建并进入排队队列";
        Raise(nameof(StatusMessage));
    }

    private void TogglePause()
    {
        Paused = !Paused;
        Raise(nameof(Paused)); Raise(nameof(PauseLabel)); Raise(nameof(RefreshState));
        StatusMessage = Paused ? "实时数据刷新已暂停" : "实时数据刷新已恢复";
        Raise(nameof(StatusMessage));
    }

    public void ShowNotification(string message)
    {
        StatusMessage = message;
        Raise(nameof(StatusMessage));
    }

    private void SetChannel(string ch)
    {
        if (!ChannelColors.ContainsKey(ch)) return;
        _channel = ch;
        BuildDies();
        if (_currentView == "wafer") BuildModuleTable("wafer");
        Raise(nameof(Channel));
        StatusMessage = $"已切换至 {ch} 通道图谱";
        Raise(nameof(StatusMessage));
    }

    private void OnTick(object sender, EventArgs e)
    {
        _tick++;
        ClockText = DateTime.Now.ToString("HH:mm:ss");
        Raise(nameof(ClockText));
        if (Paused) return;

        double last = _yield[^1];
        double next = Math.Clamp(last + (_rng.NextDouble() - .5) * 0.17 + (98.42 - last) * 0.07, 97.55, 99.55);
        if (_rng.NextDouble() < 0.012) next -= 0.75;
        _yield.Add(next); _yield.RemoveAt(0);
        RebuildChart();

        if (_tick % 2 == 0)
            foreach (var t in Tasks.Where(t => t.IsRunning && t.Progress < 100))
                t.Progress = Math.Min(100, t.Progress + _rng.NextDouble() * 0.8);
    }
}
