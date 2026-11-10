# P2 迁移状态 · 配方、对位与算法

> 对应 `docs/PRD-Blade-Migration.md` §8.2 `P2`。本文件只记录**事实状态**，不虚报完成度（E-10）。
> 阶段结论：**P2 未关闭**，本轮交付「增量 1：H02 Job 管理与公共参数」。

## 1. 本轮范围与结论

| 项 | 内容 |
|---|---|
| 交付增件 | 增量 1（0.7.0，2026-11-10） |
| 覆盖 | `H02` Job CRUD + 公共参数（`F-3.2.1-01~29`）；`H05` MapType 只读基线（8 类 Bin） |
| 不覆盖 | 子 Recipe 分步向导（`F-3.2.2-01~06/80~82`）、`H03` 对位建模、`H04` ROI/算法、`H05` MapType 编辑 |
| 前置闭环 | `T-001`、`T-002`、`T-021` 由用户于 2026-11-10 确认，已写入 PRD 第 9 章台账 |
| 阶段门禁 | `AC-P2-01`~`AC-P2-04` 通过；`AC-P2-05`、`AC-P2-06` 未实现 → P2 未关闭 |

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

## 3. 后续待办与依赖

| # | 待办 | 对应需求 | 依赖 | 阻塞 |
|---|---|---|---|---|
| 1 | 子 Recipe 分步向导（7 步可重入 + 门禁） | `R-H02-05/06`、`BR-19` | `T-042`（步骤全集）、`T-005`（基础信息字段，**高**） | 是 |
| 2 | 复判设置（离线复判/在线验证、复查相机）写入 Recipe | `R-H02-07`、`BR-20/21` | 与 `H08` 共用契约（P3） | 否 |
| 3 | H03 拉平 / Mark / 参考图像与模板匹配（全系统唯一实现） | `R-H03-01~06`、`BR-22/23/24` | DEP-06；`H05` Wafer 扫描复用 | 否 |
| 4 | H04 ROI 与扫描区域单一数据源 | `R-H04-01~06`、`AC-P2-03` | DEP-07 | 否 |
| 5 | H05 MapType 编辑 + 8 类 Bin 快捷键 | `R-H05-02/03/07`、`AC-P2-06` | DEP-08 | 否 |
| 6 | `AC-P2-05` 关闭参数页自动保存 | — | 向导完成后才有「关闭参数页」语义 | 否 |
| 7 | Job 持久化（替换内存仓库） | `R-H02-01` 落盘 | `H07` 配置单点（P4）；`T-008` | 否 |

## 4. 待确认项（影响 P2 关闭）

| 编号 | 内容 | 阻塞级 | 影响 |
|---|---|---|---|
| `T-005` | 子 Recipe「基础信息」页完整字段清单（手册仅截图） | **高** | 向导第 1 步无法定稿；当前仅保留扩展点 |
| `T-042` | 子配方步骤的完整顺序（手册 7 个命名步骤 + 基础信息，是否还有更多） | 中 | 向导步数/顺序 |
| `T-004` | 「推荐忽略」6 项（定位 Die、方向、偏移、Col/Row、X/Y、Include/Exclude）语义 | 中 | 已按级 C 保留并可持久化，语义待补 |
| `T-023` | 自定义 LotId/WaferId 与 OCR 自动填充的覆盖优先级 | 中 | 与 H02→H01 数据流衔接 |

## 5. 验证证据（0.7.0）

- `dotnet build WaferInspect.slnx -c Release`：0 警告 0 错误；
- `dotnet test WaferInspect.slnx -c Release`：**70/70 通过**（新增 19 项：CRUD / 校验 / 导入 / BR-15/16/17/78 / 保留项往返）；
- `WaferInspect.Smoke`：真实登录后主窗口句柄非零；
- 界面实测：UIAutomation 进入「配方管理」，Job 列表 3 条种子数据、复制/重命名/保存/删除与校验提示逐项走通（详见提交说明）。

## 6. 已知限制（显式登记，不静默）

1. **Map 解析器仅 TXT / CSV**：`Xml` / `Sinf` 保留枚举位，导入时明确报「解析器未接入」，不伪造成功；
2. **Job 仓库为内存实现**：重启即回到种子数据，落盘随 H07 配置单点确定；
3. **向导未实现**：页内明确标注依赖 `T-005` / `T-042`，未放置假按钮；
4. **自动保存未实现**（`AC-P2-05`）：需先有「关闭参数页」语义；
5. **Job 与运行页尚未联通**：`IJobCatalog` 仍为 `SimulatedJobCatalog`，待向导与 Recipe 完成后切换到 `IJobRepository`（避免半成品两处数据源）。
