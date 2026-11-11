# WaferInspect · 晶圆检测上位机

.NET 10 WPF 桌面端上位机原型：设备状态、检测任务、EL/PL/AOI 晶圆图谱、实时良率趋势与告警闭环。
左侧导航按配置分组，每组可独立展开 / 收起。

## 环境

- .NET SDK 10.0.x（含 `Microsoft.WindowsDesktop.App` 运行时）
- Windows（WPF / `net10.0-windows`）

## 构建与运行

```bash
dotnet restore WaferInspect.slnx
dotnet build WaferInspect.slnx -c Release
dotnet run --project src/WaferInspect.App/WaferInspect.App.csproj -c Release
```

直接运行产物：

```bash
src/WaferInspect.App/bin/Release/net10.0-windows/WaferInspectHMI.exe
```

## 测试与冒烟

```bash
dotnet test WaferInspect.slnx -c Release
dotnet run --project tools/WaferInspect.Smoke/WaferInspect.Smoke.csproj -c Release
```

`WaferInspect.Smoke` 会启动 Release 产物，经 UIAutomation 以初始账号（BR-76：admin / 123456）
**真实登录**后断言进程存活且已创建主窗口句柄（登录门禁生效后未认证不出现主窗口），
输出窗口标题；失败时输出顶层窗口枚举与登录错误文本辅助定位。退出码 `0` 为通过。也可显式传入 exe 路径：

```bash
dotnet run --project tools/WaferInspect.Smoke -c Release -- <绝对路径/WaferInspectHMI.exe>
```

## 目录

| 目录 | 用途 |
|---|---|
| `src/WaferInspect.Core` | 纯契约与计算：`Navigation/`、`Data/`、`Devices/`、`Events/`、`Security/`、`Maps/`、`Recipes/`、`Alignment/`、`Inspection/`、`Run/`、`Simulation/`、`DependencyInjection/` |
| `src/WaferInspect.App` | WPF 主程序：`Views/`（检测任务、配方管理、缺陷分析、图谱管理、子配方向导、参数验证页与共享 Map 画布 / 网格控件）、`ViewModels/`（含 `Modules/` 的 H01~H10，其中 H01/H02/H03/H04/H05 已实现）、`Models/`、`Services/` 等 |
| `tests/WaferInspect.Core.Tests` | 导航、Core 装配、Map 快照、数据替换、设备/事件/鉴权、角色模型与登录门禁、运行状态机、Job 仓库与 Map 导入、子配方七步向导、H03 对位引擎、H04 ROI 唯一数据源（BR-35~42 / AC-P2-03/05）、H05 MapType 与 Map 网格（BR-43~45 / BR-51~53 / AC-P2-06 / AC-H05-01/02/05）单测（156 项） |
| `tools/WaferInspect.Smoke` | 启动冒烟工具 |
| `.github/workflows/build.yml` | CI：restore + Release 构建 + 单元测试 |

组织规范见 [AGENTS.md](AGENTS.md)，代码风格见 `.editorconfig`，
版本与共享元数据在 `Directory.Build.props`，NuGet 包版本在 `Directory.Packages.props`（CPM）。

## 架构（1.10.1）

| 关注点 | 实现 |
|---|---|
| MVVM | `CommunityToolkit.Mvvm 8.4.2`：`ObservableObject` 基类 + `[ObservableProperty]` / `[RelayCommand]` 源生成器；XAML 绑定属性名与命令名保持不变 |
| 组合根 | `Microsoft.Extensions.Hosting 10.0.12` 的 Generic Host：`App.OnStartup` 注册并启动、`App.OnExit` 停止并释放；`StartupUri` 已移除，`MainWindow` 与 `MainViewModel` 均由容器解析 |
| 依赖注入 | `Core` 提供 `AddWaferInspectCore()`：导航、`IDataSource`、Map、事件、鉴权与设备命令层；`App` 追加数据泵、模块 ViewModel 注册表、窗口 |
| 生命周期 | 1 Hz 节拍由 `DispatcherTimerLiveDataPump` 产生，`SimulationHostedService : IHostedService` 随主机启停；日志由通用主机提供（默认 Warning 级别） |
| 可测性 | 确定性良率算法与导航目录在 `Core`（net10.0、无 WPF 类型），测试项目用真实 Host 验证装配与历史数值 |
| 可插拔能力 | H06 扫描执行抽象为 `IScanExecutor`（默认 `OfflineScanExecutor` 离线演算，`DeviceScanExecutor` 预留位诚实抛异常）；Sinf 导出抽象为 `ISinfExporter`（P7）；依赖规范（T-029/T-035/T-037）到位即 drop-in 替换，不伪造 |

包版本集中维护在 `Directory.Packages.props`（CPM），各 `csproj` 的 `PackageReference` 不写版本号。

启动链路：`App.OnStartup` → 构建 Host → `host.Start()`（启动数据泵）→ 登录窗口（`LoginWindow`，唯一登录入口）→ 认证成功经 `LoginGate` 校验 → 解析 `MainWindow`（注入 `MainViewModel`）→ `Show()`；取消登录则应用退出。

## Blade 迁移状态（P0 / P1 / P2 已关闭，P3 已关闭，P4-A / P4-B 已关闭，P5 已关闭，P6 已关闭，P7 已关闭，P8 已关闭）

P0 基线：4 组 14 项导航，新增「数据分析」(`analysis`) 与「图谱管理」(`maps`)，
两页均明确标识为尚未迁移的骨架，不展示伪造的历史业务数据。H01~H10 ViewModel 已注册，
实时良率通过 `IDataSource` 读取，Map 布局统计由 H05 `IMapRenderer` 唯一提供。

0.5.0 激活应用级登录门禁：统一角色模型（`Core/Security/RoleModel`，T-017 已确认）、
认证链路与初始账号（T-027 已确认，BR-76 按历史行为保留）。

0.6.0 关闭 P1（检测任务主干 C-01）：`Core/Run` 落地 `WaferRunController`（唯一运行状态机，
BR-06/08/09/10/14/74）、`ManualLoadSequencer`（BR-05 五步顺序）、`SimulatedJobCatalog`（BR-10）与
`InMemoryAlarmCenter`（BR-74/75）、`SelfCheckService`（BR-77）；检测任务页九栏界面全量迁移
（端口/模式、料盒 25 槽、扫描进度、图谱视图、快捷操作、晶圆/批次/缺陷/日志），并经
UIAutomation 全流程实测（选 Job → 应用 → 开始运行 → 实时进度与缺陷列表）。设备连接相关的
13 项动作按用户指示**明确搁置**（未伪造成功、未下发指令，日志以「[搁置]」标注），清单与恢复
条件见 [`docs/P1-Migration-Status.md`](docs/P1-Migration-Status.md) §4。

0.7.0 交付 P2 增量 1（`T-001`/`T-002`/`T-021` 闭环）：Job CRUD + 公共参数表单 + Map 导入器 + MapType 基线。
0.8.0 交付 P2 增量 2（`T-005`/`T-042` 闭环）：子配方七步向导域与界面（B-25 门禁 / BR-19 可重入 / 进度持久化）。
0.9.0 交付 P2 增量 3（`T-024` 闭环）：`Core/Alignment` 落地 H03 对位能力族——`TemplateMatchingEngine`
为 **B-08 全系统唯一模板匹配实现**（AC-P2-04，H05 Wafer 扫描将复用同一单例）、BR-21 光照校准、
BR-26 Mark 方向校验、T-024 点位重置二次确认、GoldenDie 唯一生成（B-09，H08 只读）；向导步骤 2/3/4
接入视窗图像拖拽框选自动拉平（模拟视窗显式标注，H06 采集随 T-029 搁置）、Map 定位网格拖动/锁定/
双击 Die 填行列、参考图像生成与 FOV 手动调整入口。

1.0.0 交付 P2 增量 4（H04 检测算法与 ROI 配置，用户确认顺序 H03 → H04 → H05 的第二项）：
`Core/Inspection` 落地 `IRoiScanAreaStore` —— **B-01/B-02 与 AC-P2-03 的唯一数据源**，
配方向导步骤 5/6、缺陷分析页与「算法参数调试 · 参数验证」页共用同一单例，
D-01/D-02 的「首页与配方各自维护 ROI」已消除。含 ROI 7 步流程（F-3.2.2-68~75）、
扫描区域/抽检（BR-37~39）、参数验证 11 项与 8 步调试闭环（F-3.8.1-02~12）、
关闭页面自动保存（BR-40 / AC-P2-05）、T-014 可扩展算法参数集合（非固定滑块）、
B-23 统一 `DefectRecord`（H01/H04/H08 共用）。预扫描仅覆盖框选区域（BR-41），
设备采集/运动/AI 推理仍按 T-029/T-035 搁置，结果为确定性离线演算并显式标注，不伪造设备执行。
H04 已交付，P2 未关闭（剩余 H05 MapType 编辑与快捷键 / AC-P2-06）；明细见
[`docs/P2-Migration-Status.md`](docs/P2-Migration-Status.md)。完整验收状态、
F↔R↔AC 追溯和待确认项见 [`docs/P0-Migration-Status.md`](docs/P0-Migration-Status.md)，实现硬约束见 `AGENTS.md` §6。

1.1.0 交付 P2 增量 5（H05 图谱与 Map 数据管理，用户确认顺序 H03 → H04 → H05 的最后一项）：
`Core/Maps` 把 `IMapTypeCatalog` 从只读基线重写为**可编辑目录**——BR-43 固定 8 类 Bin
（保留原拼写 `ForBiddenDie`）、Bin 代码/类别名/等级/颜色/快捷键/备注/数量全字段可编辑、
图谱类型 CRUD + T-043 删除保护与 Job 引用检查；`MapLevelPalette` 收敛三通道 4 级颜色，
H01 首页 Die 着色改由 MapType 目录唯一供给（B-11）；`MapGridModel` 提供 BR-51/52 唯一
`[列,行]` 坐标换算、Bin 着色、双击选中、X/Y 跳转与 BR-53 显式刷新；`MapGridView` 是唯一网格绘制
控件（单张位图）。「图谱管理」页由 P0 骨架替换为 MapType 编辑、8 类 Bin 列表、Map 图预览与
快捷键验证；H02 图谱选择、子配方步骤 1、H03/H04 网格坐标、H01 着色全部订阅同一目录。
`AC-P2-06`、`AC-H05-01/02/05` 通过，UIA 实测：BR-45 无图谱文件门禁、Bin 颜色 `#FF00AA`
同步到 H01 `Level0`、快捷键 `G→GD`、BR-53 刷新版本 v3、X/Y 跳转 `[3,7]`。
**P2 其余项（H02~H05 增量 1~5）均已交付**：`AC-H05-04` 的坐标/着色规则唯一来自 H05 Core，
复判全景 Map 已在 1.2.0 复用 `MapGridView` 收口（无第二份绘制规则）；首页圆片概览仍消费 `WaferMapRenderer`
（绘制规则同样唯一来自 `MapLevelPalette`，非功能缺口）。**P2 关闭**。`R-H05-04/05/06`（Wafer 扫描 12 步、
Die 笔刷/批量、Sinf 生成与导出）原属 P4/P3；其中 **R-H05-05（Die 笔刷 / 整行整列 / 矩形批量 / 外扩内缩）已在 1.5.0（P4-A）由独立「Wafer 扫描」页真实交付**，复用 `MapGridModel`+`MapGridView`（B-06/B-11）；**R-H05-04（12 步流程编排 B-24）与 R-H05-06 / BR-50（Sinf 诚实内部导出）已在 1.6.0（P4-B）交付**——流程编排层真实落地、依赖 H03/H06 的步骤（3~9）诚实 `[搁置]`（E-08/E-10），Sinf 为自洽内部 CSV 并明确标注「非工业 Sinf 字节流，T-037 待确认」，不伪造工业格式。

1.2.0 交付 P3 首增量 6（H08 数据分析与人工复判，C-02「数据分析」承载页）：
`Core/Analysis` 落地 `WaferResult`（F-3.9.2 良率口径）、`WaferResultStore`（B-23 确定性种子 + F-3.9.2-01~02 检索）
与 `RecheckService`（**B-16 复判执行唯一实现**：人工/批量写回，`AiRecheck` 在 T-035 模型未部署时**仅返回
明确搁置结果、不伪造推理**，E-10）；`MapGridModel` 扩展 `LoadWafer`/`Reassign`/`RecolorWafer` 供复判全景 Map
复用 H05 唯一控件 `MapGridView`，**收口 `AC-H05-04`（复判全景 Map 控件统一，无第二份绘制规则）**，
并维持 BR-53（复判后不自动刷新颜色）与 B-11（颜色/快捷键来自 `IMapTypeCatalog`）。
「数据分析」页由 P0 骨架替换为 Wafer 检索、复判全景 Map、复判前/后/AI 良率面板与缺陷多维筛选；
AI 复判（T-035 模型未部署）显式登记未迁移。明细见
[`docs/P3-Migration-Status.md`](docs/P3-Migration-Status.md)。

1.3.0 交付 P3 增量 7（结果输出 B-13/B-14 / R-H08-06）：落地统一结果导出服务 `IResultExportService`，
缺陷详情 / 全局量测 / 2D 量测报表生成自洽内部 CSV，复制 WaferId / 相对路径真实可用；
Sinf / Klarf 因 `T-037` 文件格式规范未确认，导出时显式返回 `[T-037 待确认]` 失败提示、不写文件、不伪造工业格式
（Sinf 生成底层唯一实现 B-12 仍属 H05 P3/P4 待实施，1.4.0 已作为明确不可用入口登记）。明细同上。

1.4.0 交付 P3 增量 8（按 Die 查看 R-H08-05 + GoldenDie/回溯图/复判图 + Sinf 生成 B-12 + AI 重新推理 R-H08-07）：
按 Die 查看为真实可用功能（双击 / 方向键 / 箭头导航选中 Die → 详情 + 该 Die 缺陷联动 + BR-73 无图提示）；
GoldenDie / 回溯图 / 复判图搭真实视图框架并诚实标注 `[搁置]`（T-029 图片未接入 / B-09 待 H03 生成）；
Sinf 生成（`ISinfGenerator`，B-12 / T-037）与 AI 重新推理（`IRecheckService.ReInfer`，R-H08-07 / BR-66 / T-035）
为**明确不可用入口**，不伪造工业格式 / AI 结果。明细见 [`docs/P3-Migration-Status.md`](docs/P3-Migration-Status.md)。

1.5.0 交付 **P4-A（H05 Wafer 扫描，PRD 4.5.2 / UI-3.7.3）**：新增独立导航项「Wafer 扫描」（Owner H05，位于「设备与配置」分组，与 H08「数据分析」同构）；交付扫描参数（晶圆大小 BR-46 / 扫描速度 BR-47 校验 100–500 / 扩膜 BR-48 / 创建方式 F-3.7.3-09）与 Map 编辑工具——Die 笔刷（F-3.7.3-19）、整行整列（F-3.7.3-20）、矩形批量（F-3.7.3-22）、外扩内缩（F-3.7.3-21/23，R-H05-05）。编辑复用唯一 `MapGridModel`+`MapGridView`（B-06），Bin/颜色唯一来自 `IMapTypeCatalog`（B-11）；直接编辑即时刷新（`NeedsRefresh=false`），与 H08 复判 `Reassign` 的 BR-53「待刷新」语义明确区分。全景图（F-3.7.3-10）诚实 `[搁置]`（H06 采集 T-029 未接入）；12 步流程（R-H05-04）与 Sinf 导出（BR-50 / B-12）属 P4-B 未迁移，登记于 `WaferScanViewModel.DeferredNote`。明细见 [`docs/P4-Migration-Status.md`](docs/P4-Migration-Status.md)。

1.6.0 交付 **P4-B（H05 Wafer 扫描续，PRD 4.5.2 / UI-3.7.3-25）**：12 步 Map 创建流程编排（B-24 / F-3.7.3-25）——`ScanFlowStep` 模型 + stepper 面板（自由跳转 BR-19、`RunFlow` 逐条执行），本地步（1 创建方式 / 2 选图谱 / 10 Die 笔刷 / 11 Ink·ReferenceDie / 12 Sinf）真实完成，依赖 H03/H06 的步骤（3~9）诚实标记 `[搁置]`（不伪造设备/算法结果，E-08/E-10）；Ink 墨点笔刷（BR-49）——新增 Die 物理墨点属性 `MapGridCell.Inked` + `MapGridView.InkMode` 墨点渲染 + `EditInk`/`ClearAllInk`（墨点跨刷新保留），ReferenceDie 复用 B-11 笔刷；Sinf 导出（BR-50 / B-12）为**诚实内部导出**——自洽 Map 数据 CSV，文件头明确标注「非工业 Sinf 字节流，T-037 待确认」，不伪造工业格式。明细见 [`docs/P4-Migration-Status.md`](docs/P4-Migration-Status.md)。

1.7.0 交付 **P5（H03 参考图像 / Golden Die 规划与落格，B-09）**：把 Core 已落地的 `ReferenceImagePlanner` 确定性规划能力接入 Wafer 扫描页——输入参考 Die 数 + 视野缩放 FOV，本地生成 Golden Die 四色分类（黄=待检测 / 蓝=预聚焦 / 绿=选中参考 Die / 红=参考 Die）与 BR-29 耗时指数，并支持 BR-30 FOV 变更标记 `NeedsRegeneration` 与 F-3.2.2-64/66 手动调整；规划出的参考 Die 经 `MapGridModel.EditCell` 真实落格为 B-11「参考 Die（RD）」。12 步流程步骤 5（匹配搜索圆）重分类为 `Planned`（本地规划完成、设备执行仍搁置 B-10/T-029），诚实区分「规划可交付」与「设备执行待接入」。真实图像拟合属 H06（T-029 搁置），本增量不伪造采集结果（E-08/E-10）。明细见 [`docs/P5-Migration-Status.md`](docs/P5-Migration-Status.md)。

1.8.0 交付 **P6（H06 扫描执行 / 对位，离线演算沙盒）**：新增 `OfflineScanExecutor`（Core·`Alignment`），在合成 FOV（`SyntheticFovImage`）上真实跑 **B-08 唯一匹配引擎**（`TemplateMatchingEngine`）与 **H03 对位服务**（`MarkAlignmentService`），返回 `OfflineScanReport`（`IsOffline` 恒 true、诚实标注非真实设备结果）；Wafer 扫描页新增「H06 扫描执行/对位」面板（匹配阈值输入 + 运行离线扫描 / 降低分数重试 / 离线对位），12 步流程步骤 **7（灰度模板+Die 匹配）与 8（降分重试）现可真实跑通并标记 `Planned`**。诚实边界：未注册生产 `IDeviceGateway`（T-029 未闭环前禁止）、未接入真实相机/AI（T-035 搁置）、绝不伪造设备扫描结果（E-08/E-10）；步骤 3/4/6/9 仍依赖真实设备采集/ROI/锁定/填充，维持 `Deferred`。明细见 [`docs/P6-Migration-Status.md`](docs/P6-Migration-Status.md)。

1.9.0 交付 **P7（T-037 可插拔 Sinf 导出骨架，BR-50 / B-12）**：把 Sinf 导出抽象为 `ISinfExporter` 接口，默认实现 `InternalCsvSinfExporter`（自洽内部 CSV，文件头明确标注「非工业 Sinf 字节流，T-037 待确认」，与 H08 报表内部 CSV 先例一致）；工业字节流实现 `IndustrialSinfExporter` 为**预留位**——`Export` 抛 `SinfFormatNotConfirmedException`（T-037 未确认，绝不伪造工业格式，E-08/E-10），UI 中「工业 Sinf 字节流」选项依 T-037 状态禁用。调用方（`WaferScanViewModel`）经 `ActiveSinfExporter` 按 `SelectedSinfExporterKind` 解析活动导出器，规范到位即 drop-in 替换、调用方与 UI 不变。明细见 [`docs/P7-Migration-Status.md`](docs/P7-Migration-Status.md)。

## 左侧导航配置

菜单只在一个地方定义：`src/WaferInspect.Core/Navigation/NavConfiguration.cs`。

```csharp
new NavGroupDefinition("system", "系统", /* DefaultExpanded */ false,
[
    new NavItemDefinition("alarms", "告警中心", "M6 16h12l-2-3V9H8v4l-2 3z M10 19h4", "3"),
    ...
])
```

- `DefaultExpanded` 控制首次加载时该组是展开还是收起（示例里"系统"组默认收起）；
- 折叠组只隐藏组内项，不改变选中项；导航到被折叠组内的视图时会自动展开该组；
- 组标题的展开 / 折叠箭头、项数徽标、蓝色选中指示点，以及菜单项的选中高亮条，
  共同构成状态反馈；样式集中在 `Themes/Theme.xaml` 的 `NavGroupToggle` / `NavRadio`。

## 已知限制

- `Nullable` 暂为 `disable`（ViewModel 仍依赖 WPF 类型，详见 AGENTS.md §4）；
- 数据仍为 1 Hz 模拟数据；良率读取已切换到可替换的 `IDataSource`，检测运行由 `WaferRunController` 模拟推进；
- 设备连接未接入（T-029 / T-030 待确认）：所有真实设备动作暂被搁置，界面动作以「[搁置] 未下发」日志与提示明确标注，不伪造成功；H04 单幅/全部图片测试与预扫描均为确定性离线演算，AI 推理随 T-035 搁置；
- ROI 具体算法参数项（T-014）与高级参数行为（T-032）待实机 / 算法文档确认：当前以检测 / 量测 / 全局三组可扩展集合 + 显式标注的占位项保留，未用固定滑块替代；
- 图谱相关 P4 能力：Die 笔刷 / 整行整列 / 矩形批量 / 外扩内缩（R-H05-05）已在 1.5.0（P4-A）由独立「Wafer 扫描」页真实交付；**Wafer 扫描 12 步流程（R-H05-04 / B-24）与 Sinf 诚实内部导出（R-H05-06 / BR-50，B-12 唯一实现属 H05）已在 1.6.0（P4-B）交付**——流程编排层真实落地、依赖 H03/H06 的步骤（3~9）诚实 `[搁置]`，Sinf 为自洽内部 CSV 并标注「非工业 Sinf 字节流（T-037 待确认）」，不伪造工业格式；**H03 参考图像（B-09）Golden Die 规划与落格已在 1.7.0（P5）由 Wafer 扫描页接入 `ReferenceImagePlanner` 真实交付**：本地确定性规划四色分类 + BR-29 耗时指数 + BR-30 重生成标记，规划结果经 `MapGridModel.EditCell` 落格为 B-11 参考 Die（RD），流程步骤 5 重分类为 `Planned`；真实图像拟合仍属 H06（T-029 搁置）不伪造；**H06 扫描执行/对位离线演算沙盒已在 1.8.0（P6）交付**：`OfflineScanExecutor` 在合成 FOV 上真实运行 B-08 引擎与 H03 对位、结果标注「确定性离线演算、非真实设备」，12 步流程步骤 7/8 标记 `Planned`；**Sinf 导出已在 1.9.0（P7）抽象为可插拔 `ISinfExporter`**：默认内部 CSV（诚实，T-037 待确认），工业字节流为预留实现位（`IndustrialSinfExporter` 抛诚实异常、UI 禁用选项），规范到位即替换、不伪造；**结果导出动作（B-13）已在 H08 落地：报表类生成自洽内部 CSV、复制 WaferId/相对路径真实可用，Sinf/Klarf 因 T-037 显式占位不伪造**；`AC-H05-04` 的复判全景 Map 已在 1.2.0 复用 `MapGridView` 收口，首页圆片概览仍消费 `WaferMapRenderer`（绘制规则唯一来自 H05 Core）；**H06 扫描执行/对位已在 1.10.0（P8）抽象为可插拔 `IScanExecutor`**：默认 `OfflineScanExecutor`（合成 FOV + B-08 引擎 + H03 对位，确定性离线演算）经 `WaferScanViewModel` 装配缝委托，`DeviceScanExecutor` 为预留实现位（T-029/T-035 未闭环时 `Execute` 抛 `DeviceScanNotConfirmedException`、绝不注册生产 `IDeviceGateway`、绝不伪造设备结果），UI 依 T 状态禁用「真实设备扫描」选项；闭环后仅实现 `DeviceScanExecutor` 并切换 `SelectedScanExecutorKind = Device` 即完成真实设备替换，调用方与 UI 不变；
- MapType 目录仍为内存实现（重启回到种子数据），T-043「多套并存 / 删除保护 / 引用检查」按合理默认实现并显式登记，实机规则待确认；
- 配套的高保真 HTML 原型不在本仓库内。

## 1.10.1 界面样式统一（UI 打磨）

基于用户反馈的 4 项样式修复，保持既有功能与配色体系不变：

- **运行总览标题栏垂直居中**：概览区 6 个 Grid 标题栏（晶圆检测结果 / 实时良率趋势 / 缺陷分类分布 / 设备状态 / 检测任务 / 告警信息）的 DockPanel 改为 `VerticalAlignment=Center`；
- **文本框统一圆角**：`Themes/Theme.xaml` 新增隐式 `TextBox` 样式（圆角 6、悬停 / 焦点蓝色描边、禁用态灰底），全应用 TextBox 自动生效；移除各处 `FieldBox` 显式样式与引用；
- **下拉框换 HandyControl `hc:ComboBox`**：全应用约 40 处 `ComboBox` 迁移为 `hc:ComboBox`（自带圆角模板，零模板风险），移除 `FieldCombo` 样式与引用；
- **数字输入框换 HandyControl `hc:NumericUpDown`**：明确数字字段（Form*/Bi*/Region*/Jump*/Level/Quantity 等字符串属性，以及 MatchThreshold/FovZoom/ReferenceDieCount 等数值属性）迁移为 `hc:NumericUpDown`，新增 `StringToDoubleConverter`（`Converters/Converters.cs`）处理字符串 ↔ double 绑定；
- **表格样式增强**：保留原生 `DataGrid`（HandyControl 未提供 DataGrid 控件），通过共享 `Grid`/`GenericGrid` 样式增强外观——圆角描边、隔行底色（AlternatingRowBackground）、表头底纹，更美观统一；
- **启动期资源排序约束**：全局隐式 `TextBox` 样式须置于「语义色板」画刷定义之后——`Theme.xaml` 在 `App.InitializeComponent()` 期解析，`StaticResource` 不支持前向引用，否则会在启动期抛 `XamlParseException` 致进程静默退出（无窗口、无日志），已修复；
- 构建 0 警告 0 错误，Core 200 + App 36 = 236 测试全绿。
