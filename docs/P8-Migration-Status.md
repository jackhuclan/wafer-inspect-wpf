# P8 迁移状态（1.10.0）：H06 可插拔 IScanExecutor 骨架（真实设备替换预留）

> 范围：H06 扫描执行 / 对位（T-029 相机采集 / T-035 AI 推理）。把既有 `OfflineScanExecutor` 离线沙盒
> 抽象为可插拔 `IScanExecutor`，默认实现保留、真实设备实现预留（T-029/T-035 待确认不伪造）。
> 本增量不实现真实设备扫描——T-029/T-035 规范未到位，注册生产 `IDeviceGateway` 或伪造设备结果属 E-08/E-10，是项目既定红线。

## 1. 背景与动机

- P6（1.8.0）落地的 `OfflineScanExecutor` 是**离线确定性演算沙盒**（合成 FOV + B-08 引擎 + H03 对位），
  以具体类直接 `new` 在 `WaferScanViewModel` 中，产出 `OfflineScanReport{IsOffline=true}`。
- T-029（相机采集）/ T-035（AI 推理）长期「待确认」。一旦规范到位，需要把扫描来源替换为真实设备 FOV/AI。
- 若继续以具体类硬编码，替换需改动 VM 与 UI；可插拔抽象让真实设备成为 **drop-in 实现**，调用方与界面不变。

## 2. 交付内容

### 2.1 Core 新增（`src/WaferInspect.Core/Alignment/`）

| 文件 | 角色 |
|---|---|
| `ScanExecutorKind.cs` | 枚举：`Offline`（默认可用）/ `Device`（预留位，T-029/T-035 待确认） |
| `ScanReport.cs` | 扫描演算结果记录（由 `OfflineScanReport` 重命名，保留 `IsOffline` 标志区分来源） |
| `IScanExecutor.cs` | 扫描执行抽象：`Kind` / `IsOffline` / `Execute` / `RetryWithLowerThreshold` |
| `DeviceScanNotConfirmedException.cs` | 真实设备规范未确认时的诚实异常（E-08/E-10：绝不注册生产 `IDeviceGateway`、绝不伪造设备结果） |
| `OfflineScanExecutor.cs` | 重构为 `IScanExecutor` 离线分支：`Kind=Offline`、`IsOffline=true`，内部逻辑不变 |
| `DeviceScanExecutor.cs` | 预留实现位：`Execute` 抛 `DeviceScanNotConfirmedException`，绝不伪造 |

### 2.2 ViewModel 接入（`WaferScanViewModel`）

- 原 `_executor` 字段改为 `IScanExecutor _scanExecutor`（默认 `OfflineScanExecutor`）并新增 `_deviceExecutor` 字段。
- 新增 `SelectedScanExecutorKind`（默认 `Offline`）/ `ScanExecutorOptions`（含两项）属性。
- `ActiveScanExecutor` 按 `SelectedScanExecutorKind` 解析活动执行器（**骨架核心 seam**）。
- `RunOfflineScanCore` 等经 `ActiveScanExecutor` 委托；命令名保持（默认离线），`Device` 经 UI 禁用不可达。
- `LastScanReport` 类型由 `OfflineScanReport` 改为 `ScanReport`。

### 2.3 UI / Converters（`WaferScanView.xaml` + `Views/FlowStepStatusConverters.cs`）

- H06 卡片 `WrapPanel` 新增「扫描执行源（T-029/T-035）」选择器：`ComboBox` 绑定 `ScanExecutorOptions`/`SelectedScanExecutorKind`；
  `Device` 项经 `ScanKindToEnabledConverter` **禁用**（`ToolTip` 说明 T-029/T-035 待确认）。
- 新增 `ScanKindToTextConverter` / `ScanKindToEnabledConverter`（与既有 Sinf 转换器同文件）；XAML 增加 `alignment` 命名空间与两个资源。

## 3. 验收（F↔R↔AC）

| 需求/规则 | 落实 |
|---|---|
| H06 扫描执行 / 对位（B-08/B-10） | `IScanExecutor` 抽象 + 默认离线实现真实可用（B-08 引擎 + H03 对位） |
| T-029/T-035 待确认 → 不伪造设备结果（E-08/E-10） | `DeviceScanExecutor.Execute` 抛诚实异常；UI 禁用设备选项；VM 选中设备并运行时诚实失败、不写伪造结果 |
| 与 P7 `ISinfExporter` 同构 | 可插拔 seam + 预留位 + UI 禁用，规范到位即 drop-in 替换 |
| 不改造调用方/UI 即可替换设备来源 | `ActiveScanExecutor` seam：T-029/T-035 到位仅实现 `DeviceScanExecutor.Execute` |

## 4. 测试（`WaferInspect.App.Tests/ScanExecutorTests.cs`，+6；`H06ScanExecutionTests.cs` 平滑迁移到 `IScanExecutor`/`ScanReport`）

- `OfflineExecutor_ImplementsInterface_AndOfflineContract`：`Kind=Offline`、`IsOffline=true`、`Execute` 返回 `ScanReport` 且分数合法。
- `OfflineExecutor_HighThresholdFails_ThenLowerThresholdPasses`：高阈值不通过、降分重试通过（同视窗分数不变）。
- `DeviceExecutor_IsDevice_AndExecuteThrowsHonest`：`Kind=Device`、`IsOffline=false`；`Execute`/`Retry` 抛 `DeviceScanNotConfirmedException`。
- `ViewModel_DefaultsToOffline_AndOptionsContainBoth`：默认 `Offline`，选项含两项。
- `Converter_DeviceDisabled_OfflineEnabled`：`ScanKindToEnabledConverter` 对 `Device` 返回 false、`Offline` 返回 true；`ScanKindToTextConverter` 文本含「T-029」/「离线」。
- `ViewModel_DeviceSelected_ScanHonestlyRefuses`：选设备并运行 → 诚实失败、不写结果、`StatusText` 含「T-029」「T-035」。

## 5. 诚实边界（本增量不碰）

- **真实设备 FOV/AI 扫描**：T-029/T-035 规范仍待确认，`DeviceScanExecutor` 仅为预留位，绝不以猜测采集/推理伪造（E-08/E-10），绝不注册生产 `IDeviceGateway`（AGENTS.md §P2 红线）。
- 其余 H03/H06 诚实搁置状态（步骤 3~9 的 `Deferred`/部分 `Planned`）维持不变，本增量不改 12 步流程状态机。
- 闭环后替换路径（仅实现 `DeviceScanExecutor`，UI 启用 + DI 注册即完成）：经已闭环 `IDeviceGateway` 采集真实 FOV、接入真实 AI 推理，跑同一套 B-08 引擎 + H03 对位，令 `IsOffline=false`；调用方与 UI 零改动。

## 6. 验证

- `dotnet build WaferInspect.slnx -c Release` → **0 警告 / 0 错误**
- `dotnet test WaferInspect.slnx -c Release` → **Core 200 + App 36 = 236 通过**（App 由 30 → 36，+6 为 P8）
- `git push origin main`：待执行

## 7. 结论

P8（H06 可插拔 `IScanExecutor` 骨架）**已交付并关闭**：H06 扫描执行已具备可插拔 seam，默认离线演算真实可用，
真实设备预留位诚实占位。T-029/T-035 规范确认后，仅需实现 `DeviceScanExecutor.Execute`（真实 FOV 走已闭环的
`IDeviceGateway` + 真实 AI 推理）并切换 `SelectedScanExecutorKind = Device`、UI 启用对应选项，调用方与 UI 无需改动即可完成真实设备替换。
