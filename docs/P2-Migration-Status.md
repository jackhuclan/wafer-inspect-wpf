# P2 迁移状态 · 配方、对位与算法

> 对应 `docs/PRD-Blade-Migration.md` §8.2 `P2`。本文件只记录**事实状态**，不虚报完成度（E-10）。
> 阶段结论：**P2 未关闭**。已交付「增量 1：H02 Job 管理与公共参数」（0.7.0）、
> 「增量 2：子配方七步向导」（0.8.0，`T-005`/`T-042` 闭环）与
> 「增量 3：H03 对位建模与模板匹配」（0.9.0，`T-024` 闭环）与
> 「增量 4：H04 ROI / 扫描区域唯一数据源」（1.0.0）。剩余顺序 **H05**。

## 1. 本轮范围与结论

| 项 | 内容 |
|---|---|
| 交付增件 | 增量 1（0.7.0）+ 增量 2（0.8.0）+ 增量 3：H03 对位建模（0.9.0）+ 增量 4：H04 检测算法与 ROI 配置（1.0.0，2026-11-10） |
| 覆盖 | 增量 1：`H02` Job CRUD + 公共参数、`H05` MapType 只读基线；增量 2：子配方七步向导（T-005 全字段 + B-25/BR-19）；增量 3：`H03` 对位能力族（B-08 唯一引擎、BR-21、Mark 网格、BR-26、T-024、GoldenDie B-09）；增量 4：`H04` ROI / 扫描区域唯一数据源（B-01/B-02、AC-P2-03）、参数验证 11 项 + 8 步闭环（R-H04-03）、关闭自动保存（AC-P2-05 / BR-40）、T-014 可扩展参数集合、B-23 统一 `DefectRecord` |
| 不覆盖 | `R-H03-05`（Wafer 扫描对位，B-24 编排归 `H05` 12 步 Map 创建流程）、`H05` MapType 编辑与 8 类 Bin 快捷键（`AC-P2-06`）、`H08` 数据分析（P3）、H06 真实采集与 AI 推理（T-029 / T-035 搁置） |
| 前置闭环 | `T-001`/`T-002`/`T-021`（增量 1）、`T-005`/`T-042`（增量 2）、`T-024`（增量 3）；增量 4 不依赖新的待确认项（T-014 以可扩展集合 + 显式占位处理，见 §4） |
| 阶段门禁 | `AC-P2-01`~`AC-P2-05` 通过（`-03`：向导与参数验证页同一份 ROI / 扫描区域；`-04`：B-08 引擎唯一注册；`-05`：关闭参数页自动保存）；`AC-P2-06` 未实现 → P2 未关闭 |

## 2. 已交付明细（H02 增量 1）

### 2.1 领域与接口（`src/WaferInspect.Core`）

| 文件 | 内容 |
|---|---|
| `Recipes/JobModels.cs` | `JobDefinition`（`F-3.2.1-06~28` 共 23 个字段组，含 6 个「推荐忽略」保留项）、枚举 `WaferPatternType`/`WaferSize`/`MapFileType`/`AxisDirection`/`OcrPosition`/`TraceImagePolicy`、`JobEnumText`（中英映射单一来源）、`JobValidator`、`JobValidationException` |
| `Recipes/MapFileImporter.cs` | `F-3.2.1-11/17/18`：TXT / CSV 解析，回填 Die X/Y、行列数与条码分类代码；BR-78 扩展名与声明类型不匹配即失败 |
| `Recipes/JobRepository.cs` | `IJobRepository` + `InMemoryJobRepository`：新增 / 复制 / 重命名 / 删除 / 刷新；名称唯一；写操作统一走校验；`Changed` 事件驱动列表刷新 |
| `Maps/MapTypeCatalog.cs` | `IMapTypeCatalog` + `SimulatedMapTypeCatalog`：H05 为 MapType 唯一 Owner，8 类 Bin（名称/快捷键/等级/配色）供 Job「图谱选择」引用 |

### 2.2 界面（`src/WaferInspect.App`）

| 文件 | 内容 |
|---|---|
| `Views/RecipeView.xaml(.cs)` | 左 Job 列表（`JobGrid`）+ CRUD 工具栏（`BtnJobNew/Duplicate/Rename/Delete/Refresh`）；右公共参数表单（基础信息 / Die 与 Notch / 图谱与 Map 文件 / OCR / 高级推荐忽略项 / 回溯图策略）+ 校验反馈 + 保存（`BtnJobSave`） |
| `Views/RenameJobDialog.xaml(.cs)` | `F-3.2.1-03` 名称编辑弹窗（名称唯一性由仓库层判定） |
| `ViewModels/Modules/H02RecipeViewModel.cs` | 表单编排与枚举↔文本映射；校验失败逐字段呈现；不复制 `JobValidator` 规则 |
| `ViewModels/Modules/H02Items.cs` | `JobRow` 只读投影 |
| `ViewModels/Modules/IH02DialogService.cs` + `Services/H02DialogService.cs` | 对话框边界（选 Map 文件 / 编辑名称 / 确认 / 提示） |
| `Views/MainWindow.xaml` | 新增「配方管理」视图承载：`Visibility` 留在外层 Grid，内层绑定 `Recipes`（沿用 H01 的既有约定） |

### 2.3 功能点勾销（`F-3.2.1-*`）

| F | 落点 |
|---|---|
| `F-3.2.1-01` 新增 | `BtnJobNew` → `NewJobCommand` → `BeginNew` |
| `F-3.2.1-02` 复制 | `BtnJobDuplicate` → `DuplicateJobCommand`（复制全部参数 + 唯一副本名） |
| `F-3.2.1-03` 重命名 | `BtnJobRename` → `RenameJobDialog` → `Rename` |
| `F-3.2.1-04` 删除 | `BtnJobDelete` → 二次确认 → `Delete` |
| `F-3.2.1-05` 刷新 | `BtnJobRefresh` → `Refresh` |
| `F-3.2.1-06~10` 基础信息 | 名称（必填）/ 晶圆类型（3 枚举，默认有图案有 Mark）/ 尺寸 / 材料（Si 只读）/ 厚度（禁用但可见） |
| `F-3.2.1-11~13` Die 与 Notch | Die X/Y 可编辑（Map 导入自动回填，失败保留手动）；Notch 默认 270 |
| `F-3.2.1-14~18` 图谱与 Map | 图谱选择（H05 MapType）/ 文件类型 / 文件 + 浏览 + 导入 / 列行方向 / 条码分类代码 |
| `F-3.2.1-19~21` OCR | OCR Job / 识别角度 / 位置（设备动作随 `T-029` 搁置，参数先落库） |
| `F-3.2.1-22~27` 推荐忽略项 | 「高级参数」展开区全量保留可编辑可持久化 |
| `F-3.2.1-28` 回溯图存储 | 4 选项下拉：继承系统（不覆盖）/ 上传 / 不上传 / 只上传缺陷回溯图（BR-15） |
| `F-3.2.1-29` 保存 | `BtnJobSave` → `SaveJobCommand`（新建 / 更新分流） |

### 2.4 业务规则断言

| BR | 断言位置 |
|---|---|
| `BR-15` | `JobRepositoryTests.BR15_TraceImage_IsJobLevelAndNullable`（`null` = 继承全局） |
| `BR-16` | `BR16_DefaultPatternType_IsPatternWithMark`（3 枚举 + 默认值） |
| `BR-17` | `BR17_DefaultNotchAngle_Is270` |
| `BR-78` | `BR78_DeclaredTypeMustMatchFileExtension`、`BR78_Importer_RejectsMismatchedExtension`、`Importer_UnsupportedFormat_ReportsExplicitly` |
| `T-004` 保留项 | `IgnoredByRecommendationFields_AreEditableAndPersisted`（8 个字段读写往返） |

## 2ter. 增量 3 交付明细（H03 对位建模与模板匹配，0.9.0）

### 领域与接口（`src/WaferInspect.Core/Alignment/`，全部经 `AddWaferInspectCore()` 单例注册）

| 类型 | 职责与规则口径 |
|---|---|
| `TemplateMatchingEngine` | **B-08 全系统唯一模板匹配实现**（AC-P2-04）：灰度 = 零均值归一化互相关（ZNCC）全视窗搜索；形状 = 边缘密度评分；BR-23 形状模板区域 > 视窗 25% 给出「不宜过大」建议（不判失败）、灰度无大小限制；BR-24 分数**严格大于**阈值才成功；无对比（切割道/空白，方差低于噪声阈值）→ 0 分并提示。`H05` Wafer 扫描接入时必须复用同一单例 |
| `SyntheticFovImage` | 确定性模拟视窗图像（die 阵列 + 切割道 + 每 die 特征块 + 种子噪声）；真实采集属 `H06`（`T-029` 搁置），来源显式标注，引擎不区分图像来源 |
| `LightCalibrationAnalyzer` | BR-21：均值 < 40 过暗、> 215 过亮、饱和占比 > 25% → 显式建议，不静默通过 |
| `MarkAlignmentService` | BR-26 方向校验（水平 2 点同行 / 竖直 2 点同列；三点 / 2+1 级 C 保留）；`F-3.2.2-48/50` 图谱行列 → 平台坐标映射（含网格偏移换算）；BR-31 切割道中心定位；**`T-024`：点位重置未经确认调用直接抛异常** |
| `MapGridState` | `F-3.2.2-45/46/47`：加载后可拖动、确定后锁定不可移动、重置清空 |
| `ReferenceImagePlanner` | `F-3.2.2-60/67`：确定性等距散布选参考 Die；分类 黄=待检测 / 蓝=预聚焦 / 红=可能没有 / 绿=选中参考；BR-29 耗时指数随数量单调；BR-30 视野缩放修改 → NeedsRegeneration；B-09 GoldenDie 唯一生成（`H08` 只读） |
| `ThumbnailRules` | BR-27 缩略图比例 1/2ⁿ；BR-28 倍镜一致性 |

### 数据模型扩展（`Recipes/SubRecipeModels.cs`）

- `LevelingConfig`：`LastExecutionPassed` / `LastExecutionScore` / `LastExecutionImageSource`（F-3.2.2-39 执行结果随子配方持久化）；
- `MarkSettingsConfig`（新增）：缩略图比例/倍镜/速度（BR-27/28、F-3.2.2-41~43）、拉平方向（F-3.2.2-49）、网格锁定与偏移；
- `ReferenceImageConfig`：参考 Die 数量、视野缩放、Block / Golden 创建参数（级 C 保留）、`Generated`、`ManualAdjusted`、`GoldenDie`。

### 界面接入（`SubRecipeWizardDialog`）

| 步骤 | 图形化能力 |
|---|---|
| 2 拉平 | 视窗图像（480×360 模拟源显式标注）**拖拽框选自动执行**（F-3.2.2-38）+ 区域坐标键盘/UIA 等价路径；结果 / 分数 / BR-21 光照校准 / 引擎建议显式呈现；**未执行成功禁止完成本步**（F-3.2.2-39） |
| 3 Mark | Map 定位网格（优先导入 Map 文件，缺省 12×12 并标注）：**拖动对位 → 确定锁定（F-3.2.2-46）→ 双击 Die 自动填行列（F-3.2.2-59）**；「指定 Die 左上角」写平台 X/Y（F-3.2.2-50）；移动到中心 / 移动到模板 `[搁置]`（T-029）；**点位重置二次确认**（T-024 / F-3.2.2-56）；拉平方向 / 缩略图字段（级 C 保留）；**未锁定网格禁止完成本步** |
| 4 参考图像 | 参考 Die 数量 + 视野缩放 →「创建参考图像」生成 GoldenDie（B-09，`H08` 只读）；分类统计 + 耗时指数呈现；BR-30 修改缩放即时提示重新生成；FOV 图谱手动调整入口（F-3.2.2-64/66）；**未生成禁止完成本步**（F-3.2.2-67） |

### 业务规则断言（`AlignmentEngineTests`，26 项）

B-08 引擎一致性（config 与显式参数同路径）/ BR-21 过暗/过亮/饱和/正常 / BR-23 形状过大建议 / BR-24 严格阈值边界 / BR-26 水平·竖直·三点·2+1 / BR-27 1/2ⁿ / BR-29 耗时单调 / BR-30 缩放再生 / BR-31 切割道中心 / T-024 未确认拒绝·确认清空 / F-3.2.2-46 锁定后不可移动 / F-3.2.2-60 分类计数 / 模拟图像确定性。

### 修复（本轮回归）

`SubRecipeWizardViewModel.Persist` 重建向导后未恢复当前步骤焦点 → 命令级持久化（执行拉平 / 创建参考图像）把视图焦点重置回步骤 1，后续「保存并完成本步」作用在错误步骤上；失败路径的校验面板被重载清空（B-25 失败不可见）→ 两处已修复并有 UIA 门禁正反用例覆盖。

## 2quater. 增量 4 交付明细（H04 ROI / 扫描区域唯一数据源，1.0.0）

### 领域与接口（`src/WaferInspect.Core/Inspection/`，`IRoiScanAreaStore` 经 `AddWaferInspectCore()` 单例注册）

| 类型 | 职责与规则口径 |
|---|---|
| `IRoiScanAreaStore` / `RoiScanAreaStore` | **B-01/B-02 与 AC-P2-03 的唯一数据源**：ROI 区域、检测设置列表、扫描区域、抽检 Die、算法参数集合、图片路径与自动保存版本都在一处；所有编辑广播 `Changed(recipeId)`，向导步骤 5/6、缺陷分析页、参数验证页三处消费者读同一快照。`ApplyTo` 是写回 Recipe 的唯一出口，`Save` 是落盘唯一出口 |
| `InspectionModels` | `MapRect`（1-based Die 网格，全系统唯一坐标口径）、`RoiTargetKind`（Zone/Pad，F-3.2.2-68）、`RegionExtraction`（模板匹配/二值化，F-3.2.2-71）、`DetectionItem`（每项独立检测单元 + Golden 模板区域 + BR-35 参数类型 + 保存标志）、`ScanRegion`/`SamplingDie`、`InspectionConfig`（随 Recipe 持久化）、`RoiScanAreaSnapshot`、`VerificationOutcome` |
| `AlgorithmParameterCatalog` | T-014 默认占位参数目录（检测 / 量测 / 全局 9 项，全部标注「【待确认 T-014】」），支持 `AddParameter`/`RemoveParameter`/`SetParameter`；**R-H04-06**：可扩展集合，不用固定滑块替代。`Global.ParameterType` 默认 Surface（BR-35） |
| `InspectionValidator` | F-3.2.2-69/75：至少 1 个 ROI 区域 + 至少 1 个检测项且每项必须 `Saved`；BR-37/39/41：局部扫描必须有圈选区域或抽检 Die。BR-36 有效/屏蔽区域**仅**限制提取算法生效范围，不参与扫描覆盖计算 |
| `DefectRecord`（`Core/Run/RunModels.cs`） | B-23 / R-H04-05 全系统唯一缺陷模型：保留 F-3.1.8 的 7 列，补齐 AI 分类 / AI Bin / 复判状态 / 备注 / 原始图片路径 5 个可选字段（默认值保证 H01 既有行为不变），H01/H04/H08 共用 |
| 离线验证 | `TestSingle`（F-3.8.1-05 只作用于选中图片）、`TestAll`（F-3.8.1-06 遍历全部图片）、`Prescan`（F-3.8.1-07 + BR-41 仅覆盖框选 Die）；确定性哈希演算，统一返回 `[搁置] 未下发（T-029）`，不伪造设备执行 |

### 界面（`src/WaferInspect.App`）

| 文件 | 内容 |
|---|---|
| `Views/MapRegionCanvas.xaml(.cs)` | 共享 20×20 Map 圈选画布：单张 `WriteableBitmap` 绘制（避免 400 子元素进入可视化 / UIA 树）；拖拽 = 添加区域（步骤 5 生成 ROI、步骤 6 / 参数验证生成扫描区域），双击 Die = 抽检（BR-38）；契约只有 `State="{Binding Map}"` 与 `IMapRegionCanvasHost.AddRegion/ToggleDie` |
| `Views/DefectAnalysisView.xaml(.cs)` + `H04InspectionViewModel` | 「缺陷分析」页（PRD 5.2 的 `defects` 落点，替换静态 DataGrid 骨架）：子配方选择、共享 Map 画布、全片 / 抽检开关、区域与 ROI 列表、T-014 参数摘要、B-23 缺陷结果列表 + 原始图片路径（F-3.8.1-09）、「打开参数验证页…」入口、同步证据文本 |
| `Views/ParameterValidationDialog.xaml(.cs)` + `H04ParameterValidationViewModel` | 参数验证页 11 项与 8 步闭环：加载文件夹（02）、添加/清除扫描区域（03/04）、单幅/全部图片测试（05/06）、预扫描（07）、缺陷结果列表（08）、原图路径（09）、ROI 参数设置页（10，动态参数项 + 新增/应用）、参数自动保存（11）、闭环流程说明（12）；右上角 × 与页脚按钮都走 `Closing → SaveOnClose()`（BR-40） |
| `Views/SubRecipeWizardDialog.xaml` + `SubRecipeWizardViewModel` | 步骤 5 接入 7 步 ROI 流程（选 Zone/Pad → 选检测项 → 命名 → 绘制/提取区域 → Surface 或其他参数类型 → 右下角保存 → 下一步）、Map 拖拽圈选与区域表；步骤 6 接入全片 / 局部区域 / 抽检 Die；校验与持久化均取 store 快照 |
| `IH04DialogService` / `H04DialogService` | 对话框边界（文件夹定位、本地图片枚举、打开参数验证页、提示与确认） |
| `MainViewModel` / `MainWindow.xaml` | 新增 `IsDefects` 视图承载，缺陷分析页由 `Inspection` VM 绑定；`IsGeneric` 判定排除 `defects` |

### 业务规则断言（`RoiScanAreaTests`，27 项）

BR-35（Surface 默认且可改）/ BR-36（有效/屏蔽区域不改变检测覆盖）/ BR-37（默认全片、清除后恢复）/ BR-38（抽检可多个、重复双击取消）/ BR-39（区域可多个、清除清空）/ BR-40（关闭自动保存 + 重进保持 + 仓库缺失返回 false 不伪造）/ BR-41（预扫描仅框选区域）/ BR-42（闭环呈现）；AC-P2-03（同一快照、ApplyTo 单一投影）/ AC-H04-01~04（区域一致性、单幅 vs 全部、自动保存、覆盖判定）；T-014 动态集合；B-23 统一缺陷模型。

### 实测证据（UIAutomation，2026-11-10）

- 向导 7 步全流程收口，步骤 5 门禁正反用例（无 ROI / 未保存检测项 → 显式失败并锁后续）；
- 向导步骤 6 引用 ROI 生成 1 个扫描区域 → 缺陷分析页 `H04SyncText` 立即显示「扫描区域 1 个 / ROI 1 个 / 全片=False」（**AC-P2-03 / AC-H04-01**）；
- 参数验证页再添加 1 个区域并把 `Detection.Threshold` 从 120 改为 177 → 关闭后主页显示「扫描区域 2 个 · 自动保存版本 v1 · 检测阈值=177」→ 重进参数页 `Val2Sync` 显示「检测阈值=177」（**AC-P2-05 / AC-H04-03**）；
- 单幅 / 全部 / 预扫描结果均覆盖 25 颗 Die（16 + 9 的并集，BR-41），统一标注 `[搁置] 未下发（T-029）`；
- 运行期 `ui-error.log` 为空，无未处理界面异常。

### 启动期缺陷修复（1.0.0）

`DefectAnalysisView` 的 `<Run Text="{Binding Key}"/>` 未显式 `Mode=OneWay`，TwoWay 绑定只读属性抛
`XamlParseException`；同步 `MessageBox` 的嵌套消息循环使异常反复进入 `DispatcherUnhandledException`，
最终 0xC00000FD 栈溢出。已全部 `Mode=OneWay`，并把异常处理改为「先落盘 `ui-error.log` +
`Dispatcher.BeginInvoke` 异步弹窗」。两条约定已写入 `AGENTS.md`。

## 2bis. 增量 2 交付明细（子配方七步向导，0.8.0）

### 领域与接口（`src/WaferInspect.Core`）

| 文件 | 内容 |
|---|---|
| `Recipes/SubRecipeModels.cs` | `WizardStepKey`（7 步枚举，声明顺序即向导顺序，T-042 固化）、`WizardStepStatus`、`SubRecipeBasicInfo`（T-005 确认 5 分区 33 字段，图谱 Notch 方向必填）、`LevelingConfig`（BR-22/23/24）、`MarkPoint`/`ReferenceImageConfig`/`RoiZone`/`ScanAreaConfig`（AC-P2-03）、`RecheckConfig`（BR-20/R-H02-07）、`SubRecipeValidator`（基础信息必填 + BR-16/17/78 + Mark 三点结构 BR-25/26 + ROI 结构 + 拉平分数） |
| `Recipes/SubRecipeWizard.cs` | B-25/BR-19 编排：`OrderedSteps` 唯一声明、`CanEnter/TryEnter/Enter`（跳步与失败拦截）、`CompleteCurrent/FailCurrent`（失败显式原因）、`Focus`（持久化后恢复视图焦点） |
| `Recipes/SubRecipeRepository.cs` | `ISubRecipeRepository` + 内存实现：CRUD + 复制（含 7 步进度）、Job 内名称唯一、新建从父 Job 继承基础信息初值、`Changed` 事件 |

### 界面（`src/WaferInspect.App`）

| 文件 | 内容 |
|---|---|
| `Views/SubRecipeWizardDialog.xaml(.cs)` | 向导窗口：页头子配方 CRUD（`BtnSubNew/Duplicate/Rename/Delete`）+ 左侧 7 步列表（点击进入/重入，禁用态展示 B-25 拦截）+ 右侧步骤内容（Visibility 切换 7 面板，不用 ContentControl+Template）+ 页脚上一步/保存并完成本步/下一步 |
| `ViewModels/Modules/SubRecipeWizardViewModel.cs` | 步骤 1 基础信息 T-005 全字段表单；步骤 2~7 各步数据编辑与门禁；失败显式呈现并锁后续；完成即持久化（进度重开不丢） |
| `ViewModels/Modules/SubRecipeWizardItems.cs` | `SubRecipeRow`/`WizardStepRow`/`MarkRow`/`RoiZoneRow` 呈现行 |
| `IH02DialogService` | 新增 `PickImageFile`（参考图像）与 `ShowSubRecipeWizard`（DI 工厂开窗） |
| `Views/RecipeView.xaml` | Job 工具栏新增「子配方向导…」入口（`BtnSubRecipeWizard`） |
| `Converters/Converters.cs` | 新增 `NonEmptyToVisibilityConverter`（`NonEmptyVis`，失败原因非空才显示） |

### 图形化能力接入边界（显式登记，不伪造）

| 步骤 | 本轮数据级完成 | 图形化/设备能力（下一增量） |
|---|---|---|
| 2 拉平 | 模式/模板类型/匹配分数配置 + BR-24 分数校验 | 框选区域自动执行（H03；模板类型与匹配分数为全系统共用配置 B-08） |
| 3 Mark | 三点行列/平台坐标 + BR-25/26 结构校验 | 全景图加载、Map 网格拖动、双击 Die 定位（H03/H05） |
| 4 参考图像 | 本地图像文件选择 | H06 采集（设备连接搁置 T-029） |
| 5 ROI | 区域表（名称/X/Y/宽/高）+ 结构校验 | 图形化圈选（H04，AC-P2-03 单一数据源） |
| 6 扫描区域 | 全片开关或引用 ROI 区域 | 与 H04 联动的区域选择 UI |
| 7 复判设置 | 模式/相机（BR-20 默认）写入子配方，供 H08 读取 | 保存后双击 Map 定位 Die 的校验动作（H03 图形能力） |

### 业务规则断言（`SubRecipeWizardTests`，16 项）

| BR | 断言 |
|---|---|
| `B-25` | `OrderedSteps_AreExactlyTheSevenConfirmedSteps`（T-042 7 步）、`ForwardEntry_BlockedUntilPreviousStepCompleted`、`SkippedMiddleStep_IsRejected`、`FailedStep_BlocksAllLaterSteps_UntilReExecuted` |
| `BR-19` | `CompletedStep_CanBeReEntered_AndReFailureReBlocksForward`（重入 + 再失败重新生效） |
| `BR-24` | `LevelingConfig_MatchScore_MustBeWithin0To100` |
| `BR-25/26` | Mark 三点结构校验（见 `SubRecipeValidator.ValidateMarks`，测试覆盖） |
| `BR-20` | `RecheckConfig_Defaults_FollowBr20`（离线复判 + 复查相机） |
| `T-005` | `SubRecipeValidator_RequiredFields_MatchT005Screenshot`、`MissingMapNotchAngle`、`Br78_TypeMustMatchFileExtension` |
| 持久化 | `StepStates_PersistWithSubRecipe_RoundTripKeepsProgress`、`Duplicate_CopiesAllSevenStepStates`、继承/唯一名/跨 Job 重名 |

## 3. 后续待办与依赖

| # | 待办 | 对应需求 | 依赖 | 阻塞 |
|---|---|---|---|---|
| 1 | ~~子 Recipe 分步向导~~ **已于增量 2 交付**（数据级完成，图形化交互见 §2bis 边界表） | `R-H02-05/06`、`BR-19` | `T-005`/`T-042` 已闭环 | 否 |
| 2 | ~~复判设置写入~~ **已于增量 2 交付**（步骤 7，BR-20 默认） | `R-H02-07`、`BR-20/21` | `BR-21` 光照校准随 H03/H06；`H08` 消费属 P3 | 否 |
| 3 | ~~H03 拉平 / Mark / 参考图像与模板匹配~~ **已于增量 3 交付**（0.9.0，`T-024` 闭环） | `R-H03-01~06` | `R-H03-05` Wafer 扫描对位随 H05 | 否 |
| 4 | ~~H04 ROI 与扫描区域单一数据源~~ **已于增量 4 交付**（1.0.0，`AC-P2-03` / `AC-P2-05` 通过） | `R-H04-01~06`、`F-3.2.2-68~79`、`F-3.8.1-01~12` | 无 | 否 |
| 5 | **H05 MapType 编辑 + 8 类 Bin 快捷键（下一增量）** | `R-H05-02/03/07`、`AC-P2-06` | DEP-08；H04 已就绪，启动无需新的待确认结论 | 否 |
| 6 | ~~`AC-P2-05` 关闭参数页自动保存~~ **已于增量 4 交付**（BR-40，`Closing → SaveOnClose`） | `F-3.8.1-11` | 无 | 否 |
| 7 | Job 持久化（替换内存仓库） | `R-H02-01` 落盘 | `H07` 配置单点（P4）；`T-008` | 否 |

## 4. 待确认项（影响 P2 关闭）

| 编号 | 内容 | 阻塞级 | 影响 |
|---|---|---|---|
| ~~`T-005`~~ | ~~基础信息字段清单~~ **已闭环（2026-11-10，5 分区 33 字段）** | — | 已全量落入向导步骤 1 |
| ~~`T-042`~~ | ~~步骤完整顺序~~ **已闭环（2026-11-10，7 步无更多）** | — | 已固化于 `SubRecipeWizard.OrderedSteps` |
| `T-004` | 「推荐忽略」6 项（定位 Die、方向、偏移、Col/Row、X/Y、Include/Exclude）语义 | 中 | 已按级 C 保留并可持久化（Job 与子配方两处），语义待补 |
| `T-023` | 自定义 LotId/WaferId 与 OCR 自动填充的覆盖优先级 | 中 | 与 H02→H01 数据流衔接 |
| ~~`T-024`~~ | ~~Mark 点重置二次确认~~ **已闭环（0.9.0，`MarkAlignmentService.ResetMarks` 强制）** | — | 界面经确认弹窗后方可清空 |
| `T-014` | ROI 参数设置的具体算法参数项（检测/量测/全局） | 高（原）→ 不阻塞 H04 | **按 E-08 级 C 保留处理**：实现为可扩展键值集合 + 9 个显式标注的占位项（`AlgorithmParameterCatalog`），界面动态渲染且支持增删，禁止固定滑块替代；实机清单确认后直接替换目录，不改结构。仍属 P2 关闭前的文档待确认项 |
| `T-031` | 「推荐忽略」高级参数是否默认可见或仅高级入口 | 低 | 已在 Job 表单「高级参数」展开区与子配方字段中全量保留并可持久化；默认可见性待产品确认，不影响功能完整性 |
| `T-032` | 模板参数、Golden 创建参数、Block 等高级参数具体行为 | 中 | 已在 H03/H04 配置模型中按级 C 保留（可编辑、可持久化），具体算法行为待算法文档 |

> 用户已确认的下一增量顺序为 **H03 → H04 → H05**；H03/H04 已交付，
> **启动 H05 无需新的待确认结论**。

## 5. 验证证据（1.0.0）

- `dotnet build WaferInspect.slnx -c Release`：0 警告 0 错误；
- `dotnet test WaferInspect.slnx -c Release`：**139/139 通过**（新增 `RoiScanAreaTests` 27 项：BR-35~42、AC-P2-03/05、AC-H04-01~04、T-014 扩展集合、B-23 统一模型）；
- `WaferInspect.Smoke`：真实登录后主窗口句柄非零；
- 界面实测（UIAutomation，脚本 `verify-p2-h04.ps1`）：向导步骤 1~7 全流程、H03 三步门禁正反用例、
  H04 步骤 5 门禁、Map 圈选与检测项保存、步骤 6 引用 ROI 生成扫描区域、
  缺陷分析页同步显示、参数验证页 11 项控件与单幅/全部/预扫描、关闭自动保存与重进参数保持，
  `ALL DONE` 收口；运行期 `ui-error.log` 为空；
- 关键断言：向导改 1 个扫描区域 → 参数验证页再加 1 个 → 主页 `H04SyncText` 显示 2 个（AC-P2-03）；
  `Detection.Threshold` 120 → 177 → 关闭后 `v1` 且重进仍为 177（AC-P2-05 / AC-H04-03）。

## 6. 已知限制（显式登记，不静默）

1. **Map 解析器仅 TXT / CSV**：`Xml` / `Sinf` 保留枚举位，导入时明确报「解析器未接入」，不伪造成功；
2. **Job 仓库为内存实现**：重启即回到种子数据，落盘随 H07 配置单点确定；
3. **设备侧能力仍搁置**：H03 视窗图像为确定性模拟源（采集随 H06 / T-029 搁置）；H04 预扫描运动、
   相机采集、光源控制与 AI 推理均未接入，单幅 / 全部图片测试与预扫描结果是确定性离线演算，
   界面统一标注 `[搁置] 未下发（T-029）` 与 `AI 模型未部署（T-035）`，不伪造设备执行成功；
4. **本地图片文件夹未在本次 UIA 自动化中实测**：入口（F-3.8.1-02）与枚举实现已交付，但脚本未驱动
   文件选择对话框；单幅 / 全部测试的作用范围由 `RoiScanAreaTests` 断言，界面在未选图片时明确提示
   「需先在原始图片路径中指定（F-3.8.1-09）」，不静默返回空结果；
5. **算法参数清单待实机确认**（T-014 / T-032）：默认占位项全部标注待确认，按可扩展集合实现；
6. **Job 与运行页尚未联通**：`IJobCatalog` 仍为 `SimulatedJobCatalog`，待向导与 Recipe 完成后切换到 `IJobRepository`（避免半成品两处数据源）。
