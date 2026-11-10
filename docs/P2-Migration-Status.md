# P2 迁移状态 · 配方、对位与算法

> 对应 `docs/PRD-Blade-Migration.md` §8.2 `P2`。本文件只记录**事实状态**，不虚报完成度（E-10）。
> 阶段结论：**P2 未关闭**。已交付「增量 1：H02 Job 管理与公共参数」（0.7.0）、
> 「增量 2：子配方七步向导」（0.8.0，`T-005`/`T-042` 闭环）与
> 「增量 3：H03 对位建模与模板匹配」（0.9.0，`T-024` 闭环）与
> 「增量 4：H04 ROI / 扫描区域唯一数据源」（1.0.0）与
> 「增量 5：H05 MapType 编辑与 Map 渲染」（1.1.0）。用户确认的 H03 → H04 → H05 顺序已全部交付；
> 剩余为 `AC-H05-04` 的 UI 控件统一（待 P3 复判页接入）与 P3/P4 的 H05 能力，**P2 未关闭**。

## 1. 本轮范围与结论

| 项 | 内容 |
|---|---|
| 交付增件 | 增量 1（0.7.0）+ 增量 2（0.8.0）+ 增量 3：H03（0.9.0）+ 增量 4：H04（1.0.0）+ 增量 5：H05 MapType 与 Map 渲染（1.1.0，2026-11-10） |
| 覆盖 | 增量 1~4 见前版；增量 5：`H05` 可编辑 `IMapTypeCatalog`（BR-43 八类 Bin、BR-44 全字段编辑、F-3.7.2-01~12、T-043 删除保护/引用检查）、`MapLevelPalette`（B-11 三通道 4 级颜色）、`MapGridModel` + `MapGridView`（BR-45/51/52/53 唯一坐标、着色、双击、跳转与显式刷新）、图谱管理页替换 P0 骨架、H02/H01/H03/H04 消费方同步（AC-H05-01）、快捷键解析（AC-H05-02） |
| 不覆盖 | `AC-H05-04` 的 UI 控件层统一（首页圆片概览沿用 P1 视觉控件；复判全景 Map 属 P3 未迁移，届时复用 `MapGridView`）、`R-H05-04/05/06`（Wafer 扫描 12 步、笔刷/批量/外扩内缩、Sinf 生成与导出，分属 P4/P3）、`R-H03-05`（Wafer 扫描对位）、`H08` 数据分析（P3）、H06 真实采集与 AI 推理（T-029 / T-035 搁置） |
| 前置闭环 | `T-001`/`T-002`/`T-021`（增量 1）、`T-005`/`T-042`（增量 2）、`T-024`（增量 3）；增量 4/5 不依赖新的待确认结论（T-014 以可扩展集合、T-043 以删除保护+引用检查按级 C 实现并显式登记，见 §4） |
| 阶段门禁 | `AC-P2-01`~`AC-P2-06` 全部通过（`-06`：8 类 Bin 可编辑且快捷键在解析契约中生效）；`AC-H05-01/02/05` 通过，`AC-H05-03`（12 步流程）属 P4、`AC-H05-04` 规则层通过 / UI 控件层待 P3 收口 → **P2 未关闭** |

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

## 2quinquies. 增量 5 交付明细（H05 MapType 编辑与 Map 渲染，1.1.0）

### 领域与接口（`src/WaferInspect.Core/Maps/`）

| 类型 | 职责与规则口径 |
|---|---|
| `BinKind` / `BinKindCatalog` | **BR-43 固定 8 类枚举**（GoodDie / DefectDie / ReferenceDie / OriginalDie / BlankDie / **ForBiddenDie** / MatchFailDie / ExceptionDie，保留原拼写）；唯一中文命名、默认代码 GD/DD/RD/OD/BD/FD/MF/ED、等级 0~3、默认快捷键与说明。`AvailableKinds` 只允许启用未占用的固定类型，不得新增第 9 种 |
| `BinDefinition` / `MapTypeDefinition` | F-3.7.2-05~10 与 BR-69 七项：Bin 类型、代码、类别名、等级、颜色、快捷键、备注、数量全部可编辑；`MapFilePath` 对应 BR-45「生成 Map 图需先导入图谱文件」 |
| `EditableMapTypeCatalog` | `IMapTypeCatalog` 的可编辑实现：类型 CRUD / 复制 / 重命名 / 刷新、`Changed` 事件、Bin 增删与全字段校验（代码唯一、快捷键唯一、等级 0~3、颜色 #RRGGBB/#AARRGGBB）、`ResolveShortcut`（AC-H05-02）、T-043 删除保护 + Job 引用检查（延迟委托读取 `IJobRepository`，避免 DI 循环）。兼容别名 `SimulatedMapTypeCatalog` 保留既有测试与装配调用 |
| `MapLevelPalette` | 把历史首页 PL/EL/AOI 三通道 4 级颜色收敛到一处；Bin 颜色编辑同步更新所有通道同等级颜色，H01 首页 `BuildDies` 改由 `ColorForLevel` 取色（B-11 / AC-H05-01），默认视觉不变 |
| `MapGridModel` | **B-06 唯一坐标与着色规则**：BR-51 `[列,行]` 0-based 格式与解析、BR-52 X=列 Y=行、`FormatDieCoordinate` 统一 1-based Die 行列→0-based 显示（H03/H04 网格消费）、BR-45 生成门禁、Bin 着色、双击选中、X/Y 跳转、BR-53 `MarkDirty` 不自动变色 + `Refresh` 唯一显式刷新入口、`Changed` 通知共享控件 |

### 界面（`src/WaferInspect.App`）

| 文件 | 内容 |
|---|---|
| `Views/MapGridView.xaml(.cs)` | H05 唯一 Map 网格绘制控件：单张 `WriteableBitmap` 按 `MapGridModel.Cells` 着色，双击选中 Die，选中格蓝框；`Model="{Binding Map}"` 是唯一输入契约 |
| `Views/MapManagementView.xaml(.cs)` + `H05MapViewModel` | 「图谱管理」页替换 P0 骨架：上部 MapType 下拉 + 新增/复制/重命名/删除/保存/刷新 + 名称与图谱文件导入；中部 8 类 Bin 代码列表（全字段可编辑、增加/移除/保存本项）与 Map 预览；下部快捷键验证、消费方同步文本、P3/P4 未迁移登记 |
| `IH05DialogService` / `H05DialogService` | 对话框边界（图谱文件选择、类型名称、确认、提示） |
| `MainViewModel` / `MainWindow.xaml` / `PageConfiguration` | 新增 `IsMaps` 视图承载；`maps` 页面由 `IsMigrationSkeleton=true` 改为真实页面；`BuildDies` 着色改读 `ColorForLevel`，并订阅 `Changed` 即时同步 |
| `H02RecipeViewModel` / `SubRecipeWizardViewModel` | 订阅 `IMapTypeCatalog.Changed` 刷新「图谱选择」下拉；`MapCellRow.Tip` / `MarkCellRow.Tip` 改用 `MapGridModel.FormatDieCoordinate`，消除第二份坐标换算 |

### 业务规则断言（`MapTypeCatalogTests`，17 项）

BR-43（固定 8 类 / 不可新增第 9 种）/ BR-44（全字段可编辑、代码与快捷键唯一、等级与颜色格式）/ BR-45（无图谱文件拒绝生成）/ BR-51（格式与解析、1-based→0-based）/ BR-52（X=列 Y=行、越界拒绝）/ BR-53（复判后不刷新颜色不变、显式刷新后更新）；AC-P2-06 / AC-H05-01（颜色编辑同步 `ColorForLevel` 与 Map 预览）/ AC-H05-02（快捷键改后立即生效、大小写不敏感）/ T-043（多套并存、引用检查拒绝删除、最后一套保护）；默认三通道调色板保持历史视觉。

### 实测证据（UIAutomation，2026-11-10，脚本 `verify-p2-h05.ps1`）

- 图谱管理页 26 个控件全部 OK；新建 `MAP-H05-UIA` → 无图谱文件时「生成 Map 图失败（BR-45）」→ 填入图谱文件路径并保存 → 「已生成 Map 图：20×20 Die」；
- 编辑首个 Bin 颜色 `#FF00AA` + 快捷键 `G` 并保存 → `MapConsumerText` 立即显示「H01 着色 Level0=#FF00AA · H02 图谱选择 3 套 · H08 复判快捷键 Space→未绑定」（**AC-H05-01**）；
- 快捷键验证 `G→Bin GD（良品）`、`Space→未绑定`（**AC-H05-02**）；
- 「模拟复判完成」→ 刷新版本 v2 且提示需点刷新 → 「刷新图谱」→ v3 颜色已同步（**BR-53 / AC-H05-05**）；
- X/Y 跳转 `3,7` → 选中 Die 显示 `[3,7] · 匹配失败（MF）`（**BR-51/52**）；
- 运行期 `ui-error.log` 为空，截图确认 Bin 类型下拉显示中文、Map 预览按 Bin 着色。

### 本轮修复与登记

`ComboBox IsEditable="False"` 绑定 `Text` 会显示空白（1.1.0 实测抓出），改为 `SelectedItem` 并在保存时判空保留原类型；约定已写入 `AGENTS.md`。`AC-H05-04` 的坐标 / 着色规则已统一在 H05 Core，但首页圆片概览仍沿用 P1 历史视觉控件（消费 `WaferMapRenderer` + `MapLevelPalette`），复判全景 Map 属 P3 未迁移 —— **不虚报 P2 关闭**，UI 控件统一待 P3 接入 `MapGridView` 时收口。

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
| 5 | ~~H05 MapType 编辑 + 8 类 Bin 快捷键~~ **已于增量 5 交付**（1.1.0，`AC-P2-06` / `AC-H05-01/02/05` 通过） | `R-H05-02/03/07`、`F-3.7.2-01~12` | 无 | 否 |
| 5a | **P2 收口项：`AC-H05-04` UI 控件层统一** | 首页圆片概览 / 复判全景 Map → `MapGridView` | 复判全景 Map 属 P3，尚未迁移；规则层已统一 | 否（待 P3） |
| 5b | **P3/P4：Wafer 扫描 12 步、笔刷/批量/外扩内缩、Sinf 生成与导出** | `R-H05-04/05/06`、`BR-46~50`、`T-037` | DEP-10 / DEP-11；格式规范待确认 | 否（阶段外） |
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
| `T-043` | 图谱类型是否支持多套并存、删除保护与引用检查 | 低 | **按合理默认 + 级 C 保留实现**：支持多套并存、名称唯一；至少保留 1 套；被任一 Job 引用时明确拒绝删除。实机规则待确认，不静默省略 |
| `T-037` | Map / Sinf / Klarf / 2D/CD 量测报表文件格式规范 | 高（P3/P4） | 属 `R-H05-06` / `H08` 导出（P3/P4），本增量不覆盖；界面显式登记未迁移，禁止用自拟格式冒充 Sinf |

> 用户已确认的增量顺序 **H03 → H04 → H05** 已全部交付；**P2 未关闭**（剩 `AC-H05-04` UI 控件统一，
> 待 P3 复判全景 Map 接入），启动后续阶段无需新的待确认结论；`T-043`/`T-037` 结论待补但不阻塞当前实现。

## 5. 验证证据（1.1.0）

- `dotnet build WaferInspect.slnx -c Release`：0 警告 0 错误；
- `dotnet test WaferInspect.slnx -c Release`：**156/156 通过**（新增 `MapTypeCatalogTests` 17 项：BR-43~45、BR-51~53、AC-P2-06、AC-H05-01/02/05、T-043）；
- `WaferInspect.Smoke`：真实登录后主窗口句柄非零；
- 界面实测（UIAutomation，`verify-p2-h04.ps1`）：向导 7 步、H03 三步门禁、H04 ROI/扫描区域单一数据源、
  参数验证 11 项与关闭自动保存重进保持，`ALL DONE` 收口；运行期 `ui-error.log` 为空；
- 界面实测（UIAutomation，`verify-p2-h05.ps1`）：图谱管理页 26 个控件全 OK、BR-45 门禁正反、
  Bin 全字段编辑、快捷键 `G→GD`、BR-53 刷新版本 v3、X/Y 跳转 `[3,7]`，`ALL DONE` 收口；
- 关键断言：H04 向导改 1 个扫描区域 → 参数验证页再加 1 个 → 主页显示 2 个（AC-P2-03）；
  `Detection.Threshold` 120 → 177 → 关闭后 `v1` 且重进仍为 177（AC-P2-05 / AC-H04-03）；
  H05 Bin 颜色 `#FF00AA` → 主页 `MapConsumerText` 显示 `H01 着色 Level0=#FF00AA`（AC-H05-01 / B-11）。

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
6. **Job 与运行页尚未联通**：`IJobCatalog` 仍为 `SimulatedJobCatalog`，待向导与 Recipe 完成后切换到 `IJobRepository`（避免半成品两处数据源）；
7. **MapType / Job 均为内存实现**：重启回到种子数据，落盘随 H07 配置单点确定；`T-043` 删除保护与引用检查按合理默认实现，实机规则待确认；
8. **`AC-H05-04` UI 控件层未完全统一**：坐标 / 着色 / 统计规则已收敛到 H05 Core（`WaferMapRenderer` + `MapLevelPalette` + `MapGridModel`），但首页圆片概览沿用 P1 历史视觉控件，复判全景 Map 属 P3 尚未迁移；接入时统一为 `MapGridView`，当前不存在第二份绘制规则实现；
9. **H05 P3/P4 能力未迁移**：Wafer 扫描 12 步流程（B-24 / R-H05-04）、Die 笔刷 / 整行整列 / 内缩 / 批量 / 外扩内缩（R-H05-05）、Sinf 生成与导出（R-H05-06、T-037）均在界面显式登记，不伪造入口；`AC-H05-03` 随 P4 验证。
