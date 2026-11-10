namespace WaferInspectHMI.ViewModels.Modules;

// H01（P1）与 H02（P2）已由完整实现取代（H01RunViewModel / H02RecipeViewModel），本文件只保留尚未迁移的骨架。
public sealed class H03AlignmentViewModel() : ModuleViewModelBase("H03", "对位建模与模板匹配", "P2/P4");
public sealed class H04InspectionViewModel() : ModuleViewModelBase("H04", "检测算法与 ROI 配置", "P2");
public sealed class H05MapViewModel() : ModuleViewModelBase("H05", "图谱与 Map 数据管理", "P0/P2/P3/P4");
public sealed class H06DeviceControlViewModel() : ModuleViewModelBase("H06", "图像采集与设备运动控制", "P0/P2/P4");
public sealed class H07ConfigurationViewModel() : ModuleViewModelBase("H07", "设备配置与系统参数", "P4");
public sealed class H08AnalysisViewModel() : ModuleViewModelBase("H08", "数据分析与人工复判", "P3");
public sealed class H09EventViewModel() : ModuleViewModelBase("H09", "报警、日志与故障处理", "P1/P5");
public sealed class H10IdentityViewModel() : ModuleViewModelBase("H10", "身份、用户与权限", "P0/P1/P5");
