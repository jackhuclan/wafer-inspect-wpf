# P7 迁移状态（1.9.0）：T-037 可插拔 Sinf 导出骨架

> 范围：BR-50 / B-12（Sinf 生成与导出）。把既有「诚实内部 CSV」导出从 `WaferScanViewModel` 内联逻辑
> 抽象为可插拔 `ISinfExporter`，默认实现保留、工业字节流实现预留（T-037 待确认不伪造）。
> 本增量不实现真实工业字节流——T-037 规范未到位，伪造属 E-08/E-10，是项目既定红线。

## 1. 背景与动机

- P4-B（1.6.0）落地的 Sinf 导出是**自洽内部 CSV**（文件头明确标注「非工业 Sinf 字节流，T-037 待确认」），
  序列化逻辑内联在 `WaferScanViewModel.TryExportSinf` 中。
- T-037（工业 Sinf 字节流格式规范）长期「待确认」。一旦规范到位，需要把导出替换为真实工业字节流。
- 若继续内联，替换需改动 VM 与 UI；可插拔抽象让真实格式成为 **drop-in 实现**，调用方与界面不变。

## 2. 交付内容

### 2.1 Core/App 新增（`src/WaferInspect.App/Services/`）

| 文件 | 角色 |
|---|---|
| `SinfExporterKind.cs` | 枚举：`InternalCsv`（默认可用）/ `Industrial`（预留位，T-037 待确认） |
| `SinfExportContext.cs` | 导出上下文记录：承载 `MapGridModel` + 元数据（TypeId/TypeName/CreateMode/WaferSize/ExpandFilm），导出器不依赖 VM |
| `ISinfExporter.cs` | 导出器接口：`Kind` / `IsIndustrial` / `SuggestedFileName` / `Export` |
| `SinfFormatNotConfirmedException.cs` | 工业格式未确认时的诚实异常（E-08/E-10：绝不伪造工业格式） |
| `InternalCsvSinfExporter.cs` | 默认实现：自洽内部 CSV，含「非工业 Sinf 字节流」「T-037」表头与 Die 矩阵/Bin/坐标/Ink·RD 标记 |
| `IndustrialSinfExporter.cs` | 预留实现位：`Export` 抛 `SinfFormatNotConfirmedException`，绝不伪造字节流 |

### 2.2 ViewModel 接入（`WaferScanViewModel`）

- 新增 `_sinfCsv` / `_sinfIndustrial` 字段与 `SinfExporterOptions`（含两项）/`SelectedSinfExporterKind`（默认 `InternalCsv`）属性。
- `ActiveSinfExporter` 按 `SelectedSinfExporterKind` 解析活动导出器（**骨架核心 seam**）。
- `TryExportSinf` 改为经 `ActiveSinfExporter` 委托序列化：构建 `SinfExportContext` → 对话框取路径 → `Export` → 诚实提示（工业/内部分别措辞）。工业实现抛异常时捕获并提示「T-037」，返回 false。

### 2.3 UI（`WaferScanView.xaml`）

- 导出按钮前新增「Sinf 导出格式（T-037）」选择器：`ComboBox` 绑定 `SinfExporterOptions`/`SelectedSinfExporterKind`；
  `Industrial` 项经 `SinfKindToEnabledConverter` **禁用**（`ToolTip` 说明 T-037 待确认）。
- 新增 `SinfKindToTextConverter` / `SinfKindToEnabledConverter`（与既有 `FlowStepStatusConverters` 同文件）。

## 3. 验收（F↔R↔AC）

| 需求/规则 | 落实 |
|---|---|
| BR-50 / B-12 Sinf 生成与导出 | `ISinfExporter` 抽象 + 默认内部 CSV 真实可用 |
| T-037 待确认 → 不伪造工业格式（E-08/E-10） | `IndustrialSinfExporter.Export` 抛诚实异常；UI 禁用工业选项；VM 选中工业并导出时诚实失败、不写文件 |
| 与 H08 报表内部 CSV 先例一致 | 内部 CSV 沿用相同诚实占位模式 |
| 不改造调用方/UI 即可替换格式 | `ActiveSinfExporter` seam：T-037 到位仅实现 `IndustrialSinfExporter.Export` |

## 4. 测试（`WaferInspect.App.Tests/SinfExporterTests.cs`，+5）

- `InternalCsvExporter_ProducesHonestInternalCsv`：产出含「非工业 Sinf 字节流」「T-037」、表头、数据行的文件。
- `IndustrialExporter_NotIndustrialFalse_AndExportThrowsHonest`：`IsIndustrial=true`；`Export` 抛 `SinfFormatNotConfirmedException`。
- `ViewModel_DefaultsToInternalCsv_AndOptionsContainBoth`：默认 `InternalCsv`，选项含两项。
- `ViewModel_InternalCsvExport_Succeeds`：经 VM 导出内部 CSV 成功（4×4 → 16 数据行）。
- `ViewModel_IndustrialSelected_ExportHonestlyRefuses`：选工业并导出 → 诚实失败、不写文件、StatusText 含「T-037」。

## 5. 诚实边界（本增量不碰）

- **真实工业 Sinf 字节流**：T-037 规范仍待确认，`IndustrialSinfExporter` 仅为预留位，绝不以猜测格式伪造（E-08/E-10）。
- 其余 H03/H06 诚实搁置状态（步骤 3~9 的 `Deferred`/部分 `Planned`）维持不变，本增量不改 12 步流程状态机。

## 6. 验证

- `dotnet build WaferInspect.slnx -c Release` → **0 警告 / 0 错误**
- `dotnet test WaferInspect.slnx -c Release` → **Core 200 + App 30 = 230 通过**（App 由 25 → 30，+5 为 P7）
- `git push origin main`：待执行

## 7. 结论

P7（T-037 可插拔 Sinf 导出骨架）**已交付并关闭**：Sinf 导出已具备可插拔 seam，默认内部 CSV 真实可用，
工业字节流预留位诚实占位。T-037 规范确认后，仅需实现 `IndustrialSinfExporter.Export` 并切换
`SelectedSinfExporterKind = Industrial`，调用方与 UI 无需改动即可输出真实工业格式。
