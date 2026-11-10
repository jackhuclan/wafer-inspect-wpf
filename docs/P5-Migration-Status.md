# P5 迁移状态（1.7.0）—— H03 参考图像 / Golden Die 规划与落格（B-09）

> 关联提交：P5 增量（1.7.0）。承接 P4-B（1.6.0，H05 Wafer 扫描 12 步流程 + Ink + Sinf 诚实内部导出）。
> 本增量把 Core 早已落地的 `ReferenceImagePlanner` 确定性规划能力接入 Wafer 扫描页，并将规划出的参考 Die 真实落格为 B-11「参考 Die（RD）」。

## 1. 范围与边界

| 项 | 状态 |
|---|---|
| H03 `ReferenceImagePlanner` 规划（F-3.2.2-60~67） | **已交付**（App 层接入 + 落格） |
| Golden Die 四色分类（黄/蓝/绿/红）、BR-29 耗时指数 | **已交付** |
| BR-30 FOV 变更标记 `NeedsRegeneration`、F-3.2.2-64/66 手动调整 | **已交付** |
| 规划参考 Die 落格为 `ReferenceDie`（B-06/B-11） | **已交付** |
| 12 步流程步骤 5 → `Planned`（本地规划完成、设备执行仍搁置） | **已交付** |
| 真实参考图像采集 / 拟合（H06，T-029） | **诚实搁置**（未伪造，E-08/E-10） |
| 真实对位搜索执行（B-10 / T-035） | **诚实搁置**（未伪造） |

## 2. 交付内容

### 2.1 Core（既有，本增量不改动，仅确认）
- `WaferInspect.Core/Alignment/ReferenceImagePlanner`：`Plan(cols,rows,referenceDieCount,fovZoom)` / `ChangeFovZoom` / `MarkManualAdjusted`。
- `WaferInspect.Core/Alignment/AlignmentModels`：`GoldenDiePlan`、`DieCell`、`DieCellClass`、`MapGridState`。
- `WaferInspect.Core/Maps/MapTypeCatalog`：`BinKind.ReferenceDie`（代码 `RD`、标签「参考」、快捷键 `R`、等级 1）已在 `StandardBins()`。

### 2.2 App（本增量新增/修改）
- `ViewModels/Modules/WaferScanViewModel.cs`
  - 新增 `ReferenceImagePlanner _planner` 字段与状态：`ReferenceDieCount`、`FovZoom`、`GoldenPlan`、`HasGoldenPlan`、`GoldenReferenceDieTexts`。
  - 命令：`PlanGoldenDieCommand`、`ApplyGoldenDieToMapCommand`、`ChangeFovZoomCommand`、`MarkManualAdjustCommand`。
  - `RunStep` 步骤 5：调用 `PlanGoldenDie` 并标 `Planned`（`MarkStepPlanned`）。
  - `DeferredNote` 追加 P5 说明（步骤 5 规划点位现可由 H03 本地生成）。
- `Views/FlowStepStatusConverters.cs`
  - `FlowStepStatus` 新增 `Planned`；`FlowStepStatusToTextConverter` 加「规划完成」、`FlowStepStatusToBrushConverter` 加青色；新增 `BoolToCnConverter`（是/否）。
- `Views/WaferScanView.xaml`
  - `Grid` 新增第 5 行（`Grid.Row="4"`）卡片「H03 参考图像（Golden Die 规划，B-09）」：参考 Die 数 + FOV 输入、四个按钮、四色计数与 BR-29/BR-30/手动调整展示、参考 Die 坐标列表、诚实边界标注。
- `ViewModels/Modules/ScanFlowStep.cs`：`FlowStepStatus` 枚举新增 `Planned`（含 XML 文档说明）。

### 2.3 测试
- `tests/WaferInspect.App.Tests/H03GoldenDieTests.cs`（新增，+9 项）：规划（无 Map 拒绝 / 四色分类正确 / 超量拒绝）、落格（标记 RD / 无规划拒绝）、BR-30（FOV 变更置 `NeedsRegeneration` / 同值不置）、流程（步骤 5 → `Planned`、步骤 5 仍 `Deliverable=false`）。
- `WaferScanFlowTests.RunFlow_LocalStepsDone_DeferredStepsRemainDeferred` 更新：步骤 5 现标 `Planned`，其余非可交付步仍 `Deferred`。

## 3. 验收表

| 验收项 | 来源 | 结果 |
|---|---|---|
| 生成 Map 后规划出 N 颗参考 Die 并四色分类正确 | F-3.2.2-60/67 | ✅ |
| BR-29 耗时指数随参考 Die 数单调 | BR-29 | ✅ |
| BR-30 FOV 变更标记 `NeedsRegeneration` | BR-30 / F-3.2.2-62 | ✅ |
| F-3.2.2-64/66 手动调整标记 | F-3.2.2-64/66 | ✅ |
| 规划参考 Die 落格为 `ReferenceDie`（RD） | B-06 / B-11 | ✅ |
| 步骤 5 重分类 `Planned`，其余依赖步仍 `Deferred` | B-24 | ✅ |
| 真实拟合 / 对位不伪造（E-08/E-10） | E-08/E-10 | ✅（显式标注） |

## 4. F↔R↔AC 追溯

- **F-3.2.2-60~67 / B-09**：Golden Die 规划生成与四色分类 —— `ReferenceImagePlanner`（Core）+ `PlanGoldenDie`/`ApplyGoldenDieToMap`（App）。
- **BR-29**：耗时指数 = 参考 Die 数 —— `GoldenDiePlan.EstimatedCostIndex`。
- **BR-30 / F-3.2.2-62**：FOV 变更重生成 —— `ChangeFovZoom` + `NeedsRegeneration`。
- **F-3.2.2-64/66**：FOV 图谱手动调整 —— `MarkManualAdjusted` + `ManualAdjusted`。
- **B-06 / B-11**：落格复用唯一 `MapGridModel.EditCell` 与 `IMapTypeCatalog` 的 `ReferenceDie` Bin。
- **B-24 / F-3.7.3-25**：12 步流程步骤 5 诚实 `Planned`。

## 5. 诚实边界（剩余项）

- **T-029（H06 图像采集）**：真实参考图像采集与 GoldenDie 拟合仍搁置；本面板仅做本地确定性规划与落格，不调用任何设备 / 拟合算法。
- **B-10 / T-035（对位 / AI）**：步骤 4/6/7/8/9 的设备执行部分仍 `Deferred`，待对位与 AI 能力接入。
- **步骤 5 的 `Planned` 语义**：仅表示「搜索圆规划点位可由本地规划给出」，真实匹配搜索仍依赖 B-10/T-029，未伪造。

## 6. 验证

- `dotnet build WaferInspect.slnx -c Release` → **0 警告 0 错误**。
- `dotnet test WaferInspect.slnx -c Release` → **Core 200 + App 15 = 215 通过**（App 含 P5 新增 9 项 + P4-B 6 项）。
- `Directory.Build.props` → `1.7.0`。

## 7. 结论

P5（H03 参考图像 / Golden Die 规划与落格，B-09）已交付并接入 Wafer 扫描页，诚实区分「本地规划可交付」与「设备执行待接入」。P5 关闭。
