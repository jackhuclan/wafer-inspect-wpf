# AGENTS.md · 仓库组织规范

> 本文件是**项目结构与文件归属的唯一规范**，面向在本仓库工作的开发者与 AI 代理。
> 新增项目、新增文件、调整解决方案分层前先读本文件；与 README（面向使用者）分工不同：
> README 讲"怎么构建与使用"，本文件讲"东西放在哪里、为什么"。
> 组织规范参照 `aoi_twin_demo` 仓库，按本仓库当前规模裁剪。

---

## 1. 顶层布局

```
wafer-inspect-wpf/
├─ WaferInspect.slnx         解决方案（唯一入口）
├─ .editorconfig             代码风格（解决方案级）
├─ Directory.Build.props     全解决方案共享元数据：版本单点、TreatWarningsAsErrors、Deterministic
├─ Directory.Packages.props  中央包管理（CPM）：所有 PackageReference 的版本在此
├─ nuget.config              包源配置
├─ .gitignore
├─ README.md                 使用说明（构建 / 运行 / 测试 / 目录）
├─ AGENTS.md                 本文件：组织规范
├─ src/                      源码项目（可交付产物）
├─ tests/                    测试项目（不发布）
├─ tools/                    工具项目（验收 / 冒烟，不随主程序发布）
├─ assets/                   运行时资产（当前无，预留）
├─ docs/                     验收清单与报告（当前无，预留；报告与证据同批提交）
└─ .github/workflows/        CI（restore + build + test）
```

**Solution Items = 解决方案级文件**，必须留在仓库根目录：MSBuild 的
`Directory.Build.props` / `Directory.Packages.props` 只在目录树向上查找时生效，挪走即失效。
在 Visual Studio 中通过 `Solution Items` 文件夹集中呈现。

---

## 2. 代码目录的用途与归属规则

### 2.1 `src/` — 源码项目

依赖方向严格单向：`WaferInspect.App ──► WaferInspect.Core`，禁止反向引用。

| 项目 | 目标框架 | 职责 | 禁止 |
|---|---|---|---|
| `WaferInspect.Core` | `net10.0` | 纯数据契约与纯计算：导航/页面 Owner、数据源、设备命令、统一事件、鉴权、Map 布局统计、确定性模拟算法、`AddWaferInspectCore()` | 引用任何 UI / Windows 类型；臆造设备协议或角色映射 |
| `WaferInspect.App` | `net10.0-windows` (WinExe) | WPF 主程序：Views / ViewModels / Models / Converters / Navigation / Services / Themes，以及 Generic Host 组合根 | 在 `Core` 里塞 UI 类型；在视图里 `new` ViewModel |

`WaferInspect.App` 内部子文件夹按职责命名，不再引入解决方案级子分组：

```
src/WaferInspect.App/
├─ App.xaml(.cs)              应用入口、资源字典合并、Generic Host 组合根
├─ Views/                     窗口与 XAML 视图（构造函数注入 ViewModel）：DetectionView（检测任务九栏）、
│                             RecipeView（配方管理 Job CRUD + 参数表单 + 子配方向导入口）、
│                             DefectAnalysisView / ParameterValidationDialog / MapManagementView、
│                             MapRegionCanvas（H04 区域圈选）与 MapGridView（H05 唯一网格绘制）、
│                             SubRecipeWizardDialog（七步向导窗口）与配套对话框 SelectJob / EfemSettings /
│                             WaferConfirm / JobInfo / RenameJob
├─ ViewModels/                页面状态编排、[ObservableProperty] / [RelayCommand]
│  └─ Modules/                H01~H10 模块 ViewModel：H01/H02/H03/H04/H05（H01RunViewModel /
│                             H02RecipeViewModel / SubRecipeWizardViewModel 含 H03+H04 图形交互 /
│                             H04InspectionViewModel / H04ParameterValidationViewModel /
│                             H05MapViewModel）已实现，其余为骨架
├─ Models/                    绑定模型（ObservableBase / NavGroup / NavItem / 任务 / 告警 …）
├─ Navigation/                把 Core 的纯配置转成运行时导航模型（实例服务）
├─ Services/                  1 Hz 数据泵与其 IHostedService 托管；
│                             H01/H02/H04/H05DialogService（对话框边界实现）
├─ Converters/                XAML 值转换器
└─ Themes/                    语义色板、控件模板、状态样式

src/WaferInspect.Core/
├─ Navigation/                14 项导航、页面元数据与 H 模块 Owner
├─ Data/                      IDataSource 实时良率读取边界
├─ Devices/                   IDeviceGateway 与 H06 唯一命令层
├─ Events/                    H09 统一事件源；AlarmCenter（BR-74/75 统一告警中心）
├─ Security/                  H10 统一会话、显式权限判定、认证链路与统一角色模型；SelfCheckService（BR-77）
├─ Maps/                      H05 唯一 Owner：WaferMapRenderer（B-06/B-07 圆片坐标与统计）、
│                             MapTypeCatalog（BR-43 八类 Bin 可编辑目录 / 快捷键 / T-043 引用检查，
│                             B-11 单一数据源；兼容别名 SimulatedMapTypeCatalog）、
│                             MapLevelPalette（三通道 4 级颜色，H01 着色唯一来源）、
│                             MapGridModel（BR-51/52 唯一 [列,行] 换算、Bin 着色、BR-53 显式刷新）
├─ Recipes/                   H02 Job 域：JobDefinition（F-3.2.1-06~28）、JobValidator（必填/BR-15/16/17/78）、
│                             MapFileImporter（TXT/CSV，XML/SINF 显式未接入）、IJobRepository（CRUD + 名称唯一）、
│                             SubRecipeModels/SubRecipeWizard/SubRecipeRepository（T-005 字段、T-042 七步、B-25 门禁、BR-19 可重入）
├─ Alignment/                 H03 对位域：TemplateMatchingEngine（B-08 唯一实现）、SyntheticFovImage、
│                             LightCalibrationAnalyzer、MarkAlignmentService、ReferenceImagePlanner、MapGridState
├─ Inspection/                H04 检测配置域：IRoiScanAreaStore + RoiScanAreaStore（B-01/B-02 唯一数据源、
│                             AC-P2-03）、InspectionModels（ROI/扫描区域/抽检/可扩展参数/T-014）、
│                             InspectionValidator（F-3.2.2-75 / BR-37~41）
├─ Run/                       H01 运行域唯一 Owner：WaferRunController（BR-06/08/09/10/14/74）、
│                             DefectRecord（B-23 全系统唯一缺陷模型，H01/H04/H08 共用）、
│                             ManualLoadSequencer（BR-05）、JobCatalog（BR-10）、RunModels、LogFormat（BR-18）
├─ Simulation/                确定性模拟算法 ISimulationData
└─ DependencyInjection/       AddWaferInspectCore()
```

**归属判定顺序**：
1. 与菜单 / 视图标识相关的纯数据 → `Core/Navigation`；
2. WPF 对象、窗口、样式 → `App/Views` + `App/Themes`；
3. 只服务某个页面的状态编排 → `App/ViewModels`；
4. 运行时绑定模型 → `App/Models`；
5. 只在验收 / 现场运行时使用 → `tools/`。

### 2.2 `tests/` — 测试项目

命名固定 `WaferInspect.<被测层>.Tests`。当前 `WaferInspect.Core.Tests`（net10.0 + xunit）
覆盖 14 项导航与页面 Owner、分组/默认展开、Core 服务装配、数据源替换、Map 三通道历史快照、
设备命令透传、统一事件、默认拒绝鉴权、统一角色模型展开、认证成败与登录门禁（含 `BR-76` 初始账号断言）、
运行状态机（`RunControllerTests`：晶圆内计数/结晶提交/暂停恢复/结批/BR-74 拦截等 12 项）与
手动装载序列 / Job 目录（`ManualLoadAndJobCatalogTests` 5 项）、Job 仓库与 Map 导入
（`JobRepositoryTests` 19 项）、子配方向导（`SubRecipeWizardTests` 16 项）、H03 对位（`AlignmentEngineTests` 26 项）、
H04 ROI / 扫描区域唯一数据源（`RoiScanAreaTests` 27 项，覆盖 BR-35~42、AC-P2-03/05、AC-H04-01~04）、
H05 MapType 与 Map 网格（`MapTypeCatalogTests` 17 项，覆盖 BR-43~45、BR-51~53、AC-P2-06、AC-H05-01/02/05、T-043），共 156 项；
`BusinessRuleAttribute` / `BusinessRuleAssert` 提供 BR 编号断言框架。

规则：测试工程不得被 `src/` 引用；不写仓库目录，临时数据放 `Path.GetTempPath()`；
涉及 WPF 的用例需独立 STA 宿主，当前仅测纯配置层，不引入 WPF 依赖。

### 2.3 `tools/` — 工具项目

`WaferInspect.Smoke`（net10.0-windows + UseWPF）：启动 Release 产物，经 UIAutomation 以初始账号
（BR-76：admin / 123456，T-027 已确认保留）真实登录——登录门禁（AC-P0-04）生效后未认证不出现主窗口——
再断言进程存活、`MainWindowHandle != 0`，输出窗口句柄/标题并结束进程；失败时输出顶层窗口枚举与登录
错误文本辅助定位。不随主程序发布，CI 中不强制运行。

---

## 3. Visual Studio 解决方案分层

解决方案与磁盘结构一致，分四组呈现，不允许项目平铺在根：

```
Solution 'WaferInspect'
├─ Solution Items/  (.editorconfig / AGENTS.md / README.md / Directory.*.props / nuget.config / .gitignore)
├─ src/             (WaferInspect.Core, WaferInspect.App)
├─ tests/           (WaferInspect.Core.Tests)
└─ tools/           (WaferInspect.Smoke)
```

`WaferInspect.slnx` 用 `<Folder>` 分组、`<File>` 挂解决方案级文件、`<Project>` 挂项目。
**新增项目必须同时改两处**：磁盘目录 + slnx 对应分组；漏改会导致 CI 不编译。
分组名固定为 `Solution Items` / `src` / `tests` / `tools`。

---

## 4. 与参考规范的差异（渐进项，允许存在但需在此登记）

| 项 | 参考 `aoi_twin_demo` | 本仓库现状 | 原因 / 后续 |
|---|---|---|---|
| 分层数量 | Core / ViewModel / Adapters / Rendering / App | Core / App 两层 | 当前为单窗口上位机，无设备协议与 3D 渲染层 |
| `ViewModel` 层 | `net10.0`，禁止引用 WPF | 仍在 `App/ViewModels` | `MainViewModel` 依赖 `DispatcherTimer`、`Geometry`、`Brush`；下沉需先剥离 WPF 类型 |
| `Nullable` | `enable` | `Core` 已 `enable`；`App` 仍 `disable`（Directory.Build.props 全局注明，Gradual） | 全量启用 App 层会产生 120 条 CS86xx 警告，需整体改造绑定属性契约；随 ViewModel 下沉一并切换 |
| MVVM 框架 | 参考仓库自行实现基类 | `CommunityToolkit.Mvvm 8.4.2`：`ObservableObject` + `[ObservableProperty]` / `[RelayCommand]` 源生成器 | 删除手写 `Observable` / `RelayCommand`；保留 `ObservableBase.Raise()` 兼容跨对象通知 |
| 应用主机 | 参考仓库的通用主机 | `Microsoft.Extensions.Hosting 10.0.12`：DI、日志、`IHostedService` 生命周期 | `App.OnStartup` 建 Host、`App.OnExit` 停止并释放；`StartupUri` 已移除，窗口由容器解析 |
| `assets/` / `docs/` | 有实体目录与验收物证 | `docs/` 已有迁移 PRD 与验收依据 | `assets/` 仍预留 |

---

## 5. 新增文件 / 项目的检查清单

- [ ] 位置符合 §2 归属规则，命名符合 `WaferInspect.<层>`；
- [ ] 目标框架与所在层一致（Core / tests / tools = `net10.0`，App = `net10.0-windows`）；
- [ ] 依赖方向单向，无反向引用；`WaferInspect.slnx` 已加入对应分组；
- [ ] 菜单分组 / 默认展开状态只改 `src/WaferInspect.Core/Navigation/NavConfiguration.cs` 一处；
- [ ] PackageReference 只写 `Include`，版本在 `Directory.Packages.props`（CPM）；Core 服务注册走 `AddWaferInspectCore()`；
- [ ] 窗口 / ViewModel 由 Generic Host 解析，禁止在 View 的 code-behind 里 `new MainViewModel(...)`；新增有生命周期的服务用 `IHostedService`；
- [ ] `dotnet build WaferInspect.slnx -c Release` 0 警告 0 错误（`TreatWarningsAsErrors=true`）；
- [ ] `dotnet test WaferInspect.slnx` 全绿；`tools/WaferInspect.Smoke` 启动冒烟通过；
- [ ] 版本号只改 `Directory.Build.props` 一处；提交信息用中文 Conventional Commits，尾注版本号（如 `feat(nav): … (0.2.0)`）。

---

## 6. Blade 迁移实现规约（最高优先级）

凡编写或调整历史系统相关代码，必须同时遵守 `docs/PRD-Blade-Migration.md` 1.7 与下列约束；
阶段状态与 F↔R↔AC 追溯见 `docs/P0-Migration-Status.md`、`docs/P1-Migration-Status.md`。

| 编号 | 实现约束 |
|---|---|
| `E-01` | 每个 `F-` 均有承接实现；不得遗漏、删减、弱化或降级为静态展示 |
| `E-02` | 主流程、全部分支与状态迁移完整；暂停/继续/停止/结批/失败/中止/不保存等不得省略 |
| `E-03` | 阈值、默认值、枚举、门禁、优先级与公式按 `BR-` 原样实现 |
| `E-04` | 权限与校验统一走 H10 / 对应 Owner；禁止一律允许或绕过 |
| `E-05` | 异常处理、失败提示与边界条件保留；不得吞异常或把失败转换成成功 |
| `E-06` | 输入输出格式、单位、快捷键、颜色、命名及兼容行为按历史契约保留 |
| `E-07` | 禁止用框架默认值、常量、静态占位、空实现或默认行为替代历史逻辑 |
| `E-08` | 未确认行为必须有扩展点和 `// 【待确认 T-xxx】`，不得静默省略；高级/未启用功能也须保留 |
| `E-09` | 模块关闭前完成 F 逐条勾销、BR 断言、AC 验收、双向覆盖与回归 |
| `E-10` | 任何偏离均须登记需求变更或待确认项及理由；阶段状态文档不得虚报完成度 |

P0 特殊边界：`T-017` / `T-027` 已于 2026-11-09 闭环（角色映射采纳 PRD 3.3 推定并固化于
`Core/Security/RoleModel`；初始账号 admin / 123456 按 BR-76 原样保留并登记于 `InMemoryAccountStore`），
P0 已于 0.5.0 关闭；后续如需变更角色集或账号策略必须先修订 PRD 台账再改代码。

P1 特殊边界（0.6.0）：检测任务（H01）界面功能全量迁移完成并经 UIAutomation 全流程实测；
按用户指示**暂时跳过设备连接相关迁移**，13 项设备动作明确搁置并登记于
`docs/P1-Migration-Status.md` §4（恢复条件随项附注）——搁置期间禁止伪造设备成功、
禁止下发任何指令，界面动作一律以「[搁置] 未下发」日志与提示标注。
`T-029` 未闭环前不得注册虚构的生产 `IDeviceGateway`。

P2 特殊边界（1.0.0，H04 已交付）：ROI 与扫描区域的**唯一 Owner** 是 `Core/Inspection/RoiScanAreaStore`
（B-01/B-02、AC-P2-03）；向导步骤 5/6、缺陷分析页、参数验证页一律通过 `IRoiScanAreaStore` 读写，
禁止任何界面自建副本或直接写 `SubRecipe.RoiZones` / `ScanArea` / `Inspection`。
关闭参数页自动保存（BR-40）由 `ParameterValidationDialog.Closing → SaveOnClose()` 统一触发，
不得改成「点保存才保存」或「关闭丢弃」。设备侧预扫描 / 图片采集 / AI 推理继续搁置（T-029/T-035），
结果必须标注「确定性离线演算」。

P2 特殊边界（1.1.0，H05 P2 部分已交付）：`Core/Maps/IMapTypeCatalog` 是 MapType / 8 类 Bin /
快捷键 / 等级颜色的**唯一数据源**（B-11）；H02 图谱选择、子配方步骤 1、H01 首页着色、
H03/H04 网格坐标、H08 复判快捷键一律订阅 `Changed` 或调用 `ColorForLevel` / `ResolveShortcut`，
禁止任何页面自建颜色表或快捷键映射。`MapGridModel` 是 `[列,行]` 坐标换算与 Bin 着色的唯一实现，
`FormatDieCoordinate` 负责 1-based Die 行列 → 0-based 显示坐标，界面不得自行 `-1`。
生成 Map 图必须先有图谱文件（BR-45）；复判后不点刷新颜色不变（BR-53），`MapGridModel.Refresh`
是唯一显式刷新入口。`R-H05-04/06` 属 P4-B，当前只保留显式「未迁移」登记，不得伪造 Wafer 扫描 12 步流程 / Sinf 入口；
`R-H05-05`（Die 笔刷 / 整行整列 / 矩形批量 / 外扩内缩）已在 1.5.0（P4-A）由独立「Wafer 扫描」页真实交付，
复用 `MapGridModel`+`MapGridView`（B-06），不得另起第二份绘制规则。`AC-H05-04` 的复判全景 Map 已在 1.2.0
复用 `MapGridView` 收口（坐标/着色规则唯一来自 H05 Core，无第二份绘制规则），首页圆片概览仍消费 `WaferMapRenderer`
（绘制规则同样唯一来自 `MapLevelPalette`）。**结果导出动作 B-13 已在 H08 落地（R-H08-06 统一服务）：报表类生成自洽内部 CSV、复制 WaferId/相对路径真实可用，Sinf/Klarf 因 T-037 显式占位不伪造**。**P3 增量 8（1.4.0）：按 Die 查看 R-H08-05 已真实落地（双击/方向键导航 + 该 Die 缺陷联动 + BR-73 无图提示）；GoldenDie/回溯图/复判图（F-3.9.3-20/23）视图框架已搭并诚实 `[搁置]`；Sinf 生成 B-12（`ISinfGenerator`，T-037）与 AI 重新推理（`IRecheckService.ReInfer`，R-H08-07/BR-66，T-035）为**明确不可用入口**，不伪造工业格式 / AI 结果**。**P4-A（1.5.0）：H05 Wafer 扫描独立导航项 + 扫描参数（BR-46/47/48）+ Map 编辑工具（R-H05-05）真实交付；直接编辑即时刷新（`NeedsRefresh=false`）与 H08 复判 `Reassign` 的 BR-53「待刷新」语义区分明确；全景图（F-3.7.3-10）诚实 `[搁置]`（T-029）；P4-B 的 R-H05-04 / BR-50（Sinf）于 `WaferScanViewModel.DeferredNote` 登记未迁移**。

WPF 实现约定（0.6.0 起，源于实测踩坑）：
- **禁用 `ContentControl` + `ContentTemplate` 渲染列表**：Content 为 null 时模板不实例化，
  列表整体空白；集合一律内联 `ItemsControl` / `DataGrid`；
- 元素上不得同时设 `Visibility`（绑定父 VM）与 `DataContext`（绑定子 VM）——Visibility 会在
  错误的 DataContext 上解析失败导致常显，需用外层 Grid 分担 Visibility；
- 所有可交互控件必须带 `AutomationProperties.AutomationId`（供 UIAutomation / Smoke 驱动）；
  RadioButton 导航项依赖「选中态 `IsSelected` 变化 → Navigate」绑定，程序化 Select 与
  鼠标点击等价，勿再新增仅依赖 Command 的导航路径；
- **`<Run Text="{Binding ...}"/>` 必须显式写 `Mode=OneWay`**（1.0.0 实测踩坑）：`Run.Text`
  默认 TwoWay，绑定只读属性会抛 `XamlParseException`，且异常在布局阶段反复抛出；
- **界面异常处理器不得同步弹 MessageBox**（1.0.0 实测踩坑）：同步 `MessageBox.Show` 开启嵌套
  消息循环，同一异常反复进入 `DispatcherUnhandledException` 最终导致 0xC00000FD 栈溢出；
  当前实现为「先写 `ui-error.log` + `Dispatcher.BeginInvoke` 异步弹窗」；
- H04 共享 Map 画布（`Views/MapRegionCanvas`）与 H05 `MapGridView` 都用单张 `WriteableBitmap`
  绘制 20×20 Die 网格，禁止把 400 个子元素塞进可视化树 / UIA 树；`State="{Binding Map}"` /
  `Model="{Binding Map}"` 是唯一的输入契约，`IMapRegionCanvasHost.AddRegion/ToggleDie` 是 H04 输出契约；
- **`ComboBox IsEditable="False"` 时必须绑定 `SelectedItem` 而非 `Text`**（1.1.0 实测踩坑）：
  `Text` 只反映 `SelectedItem`，直接绑定会显示空白；`SelectedItem` 尚未解析到选项时源属性可能
  被写回 null，保存逻辑须判空并保留原值，不能静默改成默认枚举；
- 界面异常落盘 `ui-error.log` 是排障首选：1.0.0 的栈溢出真因是 `<Run>` TwoWay 绑定只读属性，
  1.1.0 的空白下拉则是 ComboBox 绑定方式，两者都靠「异常日志 + 截图」而非猜测定位。
