# P4 迁移状态与双向追溯基线（Wafer 扫描）

> 基线：`PRD-Blade-Migration.md` P4 / PRD 4.5.2（UI-3.7.3）Wafer 扫描；本文件记录实际完成度，不把接口或页面骨架算作历史业务功能已完成。
> 版本：P4-A = **1.5.0**（扫描参数 + Map 编辑工具）；P4-B = **1.6.0**（12 步流程编排 + Ink/ReferenceDie 笔刷 + Sinf 诚实内部导出）。

## 1. 本次对象、范围与目标状态

- **迁移对象**：H05「Wafer 扫描」——PRD 4.5.2 的扫描参数与 Map 编辑工具（UI-3.7.3 的 F-3.7.3-09~23）+ 12 步 Map 创建流程（F-3.7.3-25 / B-24）。
- **P4-A（1.5.0，已交付）**：独立导航项、扫描参数（BR-46/47/48 / F-3.7.3-09）、Map 编辑工具（Die 笔刷 / 整行整列 / 矩形批量 / 外扩内缩，R-H05-05）、全景图诚实搁置（T-029）。
- **P4-B（1.6.0，已交付）**：
  - 12 步流程编排（B-24 / F-3.7.3-25）：编排层 + 自由跳转（BR-19），本地步（1/2/10/11/12）真实完成，依赖 H03/H06 步（3~9）诚实标记 `[搁置]`（E-08 / E-10）；
  - Ink 墨点笔刷（BR-49）：新增 Die 物理墨点属性 `Inked` + `MapGridView` 墨点渲染 + Ink 笔刷；ReferenceDie 已是 B-11 笔刷；
  - Sinf 导出（BR-50 / B-12）：**诚实内部导出**（自洽 Map 数据 CSV，文件头明确标注「非工业 Sinf 字节流，T-037 待确认」），不伪造工业格式。
- **目标状态**：12 步流程可走通、本地编辑与墨点真实可用、Sinf 为诚实内部表示；依赖 H03/H06 的对位与扫描执行明确标注未接入，不伪造工业格式 / 设备成功。
- **诚实搁置边界（仍有效）**：步骤 3~9（选模板 / 矩形 ROI / 匹配搜索圆 / 锁定 / 灰度+Die 匹配 / 降分重试 / 填充未匹配）依赖 H03 对位 / H06 扫描执行（T-029 / T-035），仅登记不伪造；Sinf 真工业字节流待 T-037 格式规范确认。

## 2. P4-B 验收状态（1.6.0）

| 验收 | 状态 | 证据 / 缺口 |
|---|---|---|
| 12 步流程编排（B-24 / F-3.7.3-25） | 已交付 | `WaferScanViewModel.FlowSteps`（12 步）+ `ScanFlowStep` 模型；自由跳转（BR-19）；`RunFlow` 逐条执行；`WaferScanFlowTests` 覆盖 |
| 本地步（1 创建方式 / 2 选图谱 / 10 Die 笔刷 / 11 Ink·ReferenceDie / 12 Sinf） | 已交付 | 分别复用 `ApplyScanParams` / `GenerateMap` / `EditCell` / Ink 笔刷 + ReferenceDie 笔刷 / `TryExportSinf` |
| 依赖步（3~9）诚实 `[搁置]` | 已交付（诚实） | `RunStep` 对 `!Deliverable` 步标记 `Deferred` 并提示依赖（T-029/T-035），不调用未接入设备；测试断言不标 `Done` |
| Ink 墨点笔刷（BR-49） | 已交付 | `MapGridModel.EditInk`/`ClearAllInk` + `MapGridCell.Inked` + `MapGridView.InkMode`/墨点渲染；`Inked` 跨刷新/重着色保留 |
| ReferenceDie 笔刷 | 已交付 | `ReferenceDie` 已是 `BinKind`，B-11 笔刷调色板直接可选（无需新增） |
| Sinf 导出（BR-50 / B-12） | 已交付（诚实内部导出） | `TryExportSinf` 写自洽 CSV，文件头标注「非工业 Sinf 字节流，T-037 待确认」；不伪造工业字节流（E-08/E-10） |
| 直接编辑即时刷新 vs H08 复判 BR-53 | 已交付（语义一致） | P4-A 已覆盖；P4-B 的 Ink 编辑同样 `NeedsRefresh=false` 即时刷新 |
| B-11 单一颜色源 | 已交付 | 沿用 `IMapTypeCatalog`；Sinf 导出 Die 行含 `BinCode/等级/颜色` |
| 构建 / 测试门禁 | 通过 | `dotnet build -c Release` 0 警告 0 错误；`dotnet test` 206 项全绿（新增 App.Tests 6 项） |

## 3. F ↔ R ↔ AC 双向追溯（P4-B 新增部分）

| 需求编号 | 标题 | 状态 | 落点 |
|---|---|---|---|
| F-3.7.3-25 | 12 步 Map 创建完整流程 | 已交付（编排 + 诚实搁置） | `ScanFlowStep` 12 步 + `WaferScanViewModel` 编排（`RunFlow`/`RunStep`） |
| B-24 | 12 步流程编排 | 已交付 | 同上；H05 唯一 Owner，调用 H03/H06/H08 属消费 |
| BR-49 | Ink 笔刷 / ReferenceDie | 已交付 | `MapGridCell.Inked` + `MapGridView.InkMode` + `MapGridModel.EditInk`/`ClearAllInk`；ReferenceDie 复用 B-11 笔刷 |
| BR-50 | Sinf 导出 | 已交付（诚实内部导出） | `WaferScanViewModel.TryExportSinf` + `IH05DialogService.PickExportPath` |
| B-12 | Sinf 文件生成（唯一） | 已交付（内部表示，真格式待 T-037） | `TryExportSinf` 单一实现，导出动作由本页触发（B-13 由 H08 统一） |
| F-3.7.3-19/20/21/22/23 | Die 笔刷/整行整列/矩形/外扩内缩 | 已交付（P4-A） | `MapGridModel.EditCell/Row/Column/Rect/ExpandShrink` |
| B-11 | Bin/颜色唯一目录 | 已交付 | `IMapTypeCatalog` 全链路取色（含 Sinf 导出行） |
| B-06 | Map 绘制唯一控件 | 已交付 | `MapGridView` + `MapGridModel` 复用，Ink 墨点为控件内叠加渲染 |

## 4. 诚实边界（P4-B 后剩余）

| 需求 | 依赖 | 本页处理 |
|---|---|---|
| F-3.7.3-03/04/05/08/13/16~18（步骤 3~9） | H03 对位 / H06 扫描执行（T-029/T-035） | 流程中诚实 `[搁置]`，不伪造设备/算法结果（E-08 / E-10） |
| Sinf 工业字节流（BR-50 真格式） | T-037 格式规范待确认 | 当前为自洽内部导出并明确标注；T-037 确认后替换为真格式生成，不破坏出口语义 |

## 5. 结论

**P4（A + B）已关闭**：扫描参数、Map 编辑工具、12 步流程编排、Ink/ReferenceDie 笔刷、Sinf 诚实内部导出全部真实可用；依赖 H03/H06 的对位与扫描执行、Sinf 工业格式明确诚实搁置。全程复用既有单一数据源（`IMapTypeCatalog` B-11）与单一控件（`MapGridView`+`MapGridModel` B-06），未另起第二份绘制 / 颜色规则。
