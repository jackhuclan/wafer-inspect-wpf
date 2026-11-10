# P4 迁移状态与双向追溯基线（Wafer 扫描）

> 基线：`PRD-Blade-Migration.md` P4 / PRD 4.5.2（UI-3.7.3）Wafer 扫描；本文件记录实际完成度，不把接口或页面骨架算作历史业务功能已完成。
> 版本：P4-A = **1.5.0**（本文件记录的状态）；P4-B 待后续增量。

## 1. 本次对象、范围与目标状态

- **迁移对象**：H05「Wafer 扫描」——PRD 4.5.2 的扫描参数与 Map 编辑工具（UI-3.7.3 的 F-3.7.3-09~23）。
- **本轮范围（P4-A / 1.5.0）**：
  - 独立导航项「Wafer 扫描」（Owner H05，位于「设备与配置」分组，与 H08「数据分析」同构）；
  - 扫描参数：晶圆大小（BR-46）、扫描速度（BR-47，100–500 校验）、扩膜（BR-48）、创建方式（F-3.7.3-09）；
  - Map 编辑工具：Die 笔刷（F-3.7.3-19）、整行整列（F-3.7.3-20）、矩形批量（F-3.7.3-22）、外扩内缩（F-3.7.3-21/23，R-H05-05）；
  - 全景图占位（F-3.7.3-10，H06 采集 T-029 诚实搁置）；
  - 复用既有单一数据源与单一控件：`IMapTypeCatalog`（B-11，Bin/颜色/快捷键唯一来源）、`MapGridModel`+`MapGridView`（B-06，唯一 Map 绘制）。
- **目标状态**：扫描参数与 Map 本地编辑真实可用；依赖 H03/H06 的对位与扫描执行、Sinf 导出明确标注未接入，不伪造工业格式/设备成功。
- **不在本轮范围（P4-B）**：12 步 Map 创建流程（R-H05-04 / B-24）、Sinf 导出（BR-50 / B-12）、Ink/ReferenceDie 笔刷、各步诚实搁置标记。
- **诚实搁置边界**：F-3.7.3-02/03 模板类型/匹配分数复用 H03 单一实现对位（B-08）；F-3.7.3-04 拉平 / 05 寻找圆心 / 08 扫描执行 / 10 全景图 / 13 创建模板 / 16~18 匹配搜索圆·锁定·Die 匹配 / 14 填充未匹配 依赖 H03/H06（T-029 / T-035），本页仅提供参数与本地编辑（E-08 / E-10）。

## 2. P4-A 验收状态（1.5.0）

| 验收 | 状态 | 证据 / 缺口 |
|---|---|---|
| `AC-H05-02/03/07`（MapType / Map 渲染，由 `maps` 页交付） | 已通过（P2） | 不变；Wafer 扫描复用同一 `MapGridModel` 与 `MapGridView`（B-06） |
| 扫描参数读写（BR-46/47/48 / F-3.7.3-09） | 通过 | `WaferScanViewModel.ApplyScanParams` 校验 BR-47 100–500；UI 三区布局就位 |
| Die 笔刷（F-3.7.3-19） | 通过 | `MapGridModel.EditCell` + `MapGridView` 双击/拖拽；`MapGridModelEditTests` 覆盖 |
| 整行整列（F-3.7.3-20） | 通过 | `MapGridModel.EditRow`/`EditColumn`；测试覆盖 |
| 矩形批量（F-3.7.3-22） | 通过 | `MapGridModel.EditRect` + `MapGridView.RectSelected` 拖拽框选；测试覆盖归一化坐标 |
| 外扩内缩（F-3.7.3-21/23，R-H05-05） | 通过（本地形态学） | `MapGridModel.ExpandShrink` 膨胀/腐蚀；不依赖 H03/H06，测试覆盖 1 步/2 步/腐蚀留核 |
| 全景图（F-3.7.3-10） | 搁置（诚实） | `PanoramaText = "[搁置] 全景图需 H06 图像采集（T-029），未接入"`；不渲染伪造图像 |
| 直接编辑即时刷新 vs H08 复判 BR-53 | 通过 | 编辑方法 `NeedsRefresh=false`（即时）；`Reassign` `NeedsRefresh=true`（待刷新）；`MapGridModelEditTests.DirectEdit_RefreshesImmediately_UnlikeReassign` 标注 BR-53 |
| B-11 单一颜色源 | 通过 | 所有 Bin/颜色来自 `IMapTypeCatalog`；编辑后 `ColorHex` 取自图谱 Bin，测试断言相等 |
| 导航落点（独立项，非并入 maps 分区） | 通过 | `PageConfiguration` 新增 `waferscan`；`NavConfiguration` 设备组新增「Wafer 扫描」；`MainWindow` 承载 Grid（与 `IsAnalysis` 同构） |
| 构建 / 测试门禁 | 通过 | `dotnet build -c Release` 0 警告 0 错误；`dotnet test` 全绿（含 P4-A 新增用例） |

## 3. F ↔ R ↔ AC 双向追溯（P4-A 已落地部分）

| 需求编号 | 标题 | 状态 | 落点 |
|---|---|---|---|
| F-3.7.3-09 | 创建方式（无图案/网格匹配/模板匹配） | 已交付 | `WaferScanViewModel.CreateMode` + `CreateModeOptions` |
| F-3.7.3-19 | Die 笔刷 | 已交付 | `MapGridModel.EditCell` + `MapGridView` 双击 |
| F-3.7.3-20 | 整行 / 整列编辑 | 已交付 | `MapGridModel.EditRow`/`EditColumn` + 工具栏按钮 |
| F-3.7.3-21 | 外扩 | 已交付 | `MapGridModel.ExpandShrink("expand", …)` |
| F-3.7.3-22 | 矩形批量 | 已交付 | `MapGridModel.EditRect` + `MapGridView` 拖拽框选 |
| F-3.7.3-23 | 内缩 | 已交付 | `MapGridModel.ExpandShrink("shrink", …)` |
| F-3.7.3-10 | 全景图 | 搁置（T-029） | `WaferScanViewModel.PanoramaText` 诚实占位 |
| F-3.7.3-11 | 生成 Map 图 | 已交付（本地） | `WaferScanViewModel.GenerateMap` → `MapGridModel.Generate`（BR-45：需图谱文件路径） |
| BR-46 | 晶圆尺寸 | 已交付 | `WaferSizeOptions` + `WaferSize` |
| BR-47 | 扫描速度 100–500 | 已交付 | `ApplyScanParams` 校验 |
| BR-48 | 扩膜开关 | 已交付 | `ExpandFilm` |
| BR-50 | Sinf 导出 | 未迁移（P4-B） | 见第 4 节 |
| BR-52 | X/Y 跳转整数 | 已交付 | `JumpToDie` 整数校验（BR-52） |
| BR-53 | 复判后不自动刷新 | 已交付（语义边界） | 仅约束 H08 复判；Wafer 扫描直接编辑即时刷新（不触发 BR-53） |
| R-H05-05 | Die 笔刷/批量/外扩内缩 | 已交付（P4-A） | `WaferScanViewModel` + `MapGridModel` 编辑方法 |
| B-11 | Bin/颜色唯一目录 | 已交付 | `IMapTypeCatalog` 全链路取色 |
| B-06 | Map 绘制唯一控件 | 已交付 | `MapGridView` + `MapGridModel` 复用 |
| B-24 | 12 步流程编排 | 未迁移（P4-B） | 见第 4 节 |
| B-12 | Sinf 生成 | 未迁移（P4-B） | 见第 4 节 |

## 4. P4-B 未迁移项（诚实登记，不伪造）

| 需求 | 依赖 | 本页处理 |
|---|---|---|
| R-H05-04 / B-24：12 步 Map 创建流程 | H03/H06 对位与采集 | `DeferredNote` 登记；本页仅参数 + 本地编辑 |
| BR-50 / B-12：Sinf 生成与导出 | T-037 格式规范待确认 | `DeferredNote` 登记；不生成伪造 Sinf 文件 |
| Ink / ReferenceDie 专用笔刷 | — | P4-B 增强；当前笔刷已覆盖全部 8 类 Bin（B-11） |
| F-3.7.3-02/03/04/05/08/13/14/16~18 | H03 对位 / H06 采集 | 诚实标注 `[搁置]`，不调用未接入设备能力（E-08 / E-10） |

## 5. 结论

**P4-A（1.5.0）已关闭**：扫描参数与 Map 编辑工具真实可用，导航独立落点，全程复用既有单一数据源/控件，依赖项诚实搁置。
**P4-B 待后续增量**：12 步流程编排与 Sinf 导出；届时复用本页骨架与 `MapGridModel` 编辑能力，不重复造控件。
