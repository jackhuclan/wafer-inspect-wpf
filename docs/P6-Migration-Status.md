# P6 迁移状态（H06 扫描执行/对位，离线演算沙盒，1.8.0）

> 关联：PRD/H03 对位域、B-08 模板匹配、B-09 参考图像、T-029（图像采集）、T-035（AI 推理）
> 上游：P5（1.7.0，H03 参考图像 Golden Die 规划与落格）已交付
> 下游建议：T-029/T-035 闭环后由真实采集 FOV 替换 `SyntheticFovImage`、由真实设备网关替换离线沙盒

## 1. 范围与定位

P6 在 **T-029（图像采集）/T-035（AI 推理）未闭环** 的硬约束下，把 H06 中**可本地确定性验证**的部分真实落地：

- **真实计算**：复用 B-08 唯一匹配引擎（`TemplateMatchingEngine`）与 H03 对位服务（`MarkAlignmentService`），在合成 FOV 上真实跑匹配与对位数学；
- **诚实边界**：FOV 由 `SyntheticFovImage` 合成、不接入真实相机/AI；`OfflineScanExecutor` **不注册** `IDeviceGateway` 生产适配器（守 AGENTS.md 第 198 行红线），所有输出 `IsOffline` 恒 true 并附离线说明，绝不伪造设备结果（E-08/E-10）。

## 2. 交付内容

### 2.1 Core：`OfflineScanExecutor`（新建 `Alignment/OfflineScanExecutor.cs`）
- 构造注入 `TemplateMatchingEngine`（B-08 单源）+ `MarkAlignmentService`（H03 单源）——禁止另写匹配/对位逻辑。
- `Execute(columns, rows, matchThreshold, seed?, dieXmm?, dieYmm?)`：
  - 合成 FOV（`SyntheticFovImage.Generate`，固定种子可复现）；
  - 取中心 Die 的 20×20 对比模板区（die 基底 150 + 部分特征块 220，方差 > 30，避免 B-08「灰度无对比」返回 0 分）；
  - 真实调用 `TemplateMatchingEngine.Match(...)` 算匹配分；
  - 真实调用 `MarkAlignmentService.DieToPlatform(...)` 解参考 Die 平台坐标；
  - 返回 `OfflineScanReport`（`IsOffline=true`、`MatchScore`∈[0,100]、`MatchPassed` 反映 BR-24 严格大于阈值、`PlatformX/Y`、`Note` 注明非真实设备）。
- `RetryWithLowerThreshold(prev, lower)`：步骤 8 用——同一合成 FOV（同种子）上用更低阈值真实重跑，分数确定性不变、仅 Pass 随阈值变化。
- 诚实护栏：`columns/rows<=0`、`matchThreshold` 越界均抛异常（非静默降级）。

### 2.2 App·ViewModel（`WaferScanViewModel`，P5 同文件）
- 新增字段 `_executor = new(new TemplateMatchingEngine(), new MarkAlignmentService())`、状态 `MatchThreshold`(默认 75)/`LastScanReport`/`HasScanReport`/`ScanReportText`；
- 命令 `RunOfflineScan`（步骤 7 入口）、`RetryLowerScore`（步骤 8）、`RunOfflineAlign`；
- `RunStep`：步骤 7/8 调离线执行器后标记 `Planned`（文本注明「离线演算」）；步骤 3/4/6/9 维持 `Deferred`；
- `DeferredNote` 追加 P6 已交付说明。

### 2.3 App·UI（`WaferScanView.xaml`）
- `Grid.Row="5"` 新增卡片「H06 扫描执行/对位（离线演算，T-029/T-035 诚实搁置）」：匹配阈值输入、三按钮（运行离线扫描/降低分数重试/离线对位）、分数与平台坐标展示、`Surface3`+`Warn` 红色诚实横幅（合成 FOV + B-08 引擎 + H03 对位，全部确定性离线演算，未接入真实相机/AI）。

### 2.4 测试（`H06ScanExecutionTests.cs`，App.Tests 新增 +10；全量 Core 200 + App 25 = 225）
- 离线执行 `IsOffline=true`、分数∈[0,100]、平台坐标由 H03 解出；
- 降分重试确定性重算（同 FOV 同分、Pass 随阈值单调）；
- `columns<=0` 抛异常；
- 无 Map → 命令优雅拒绝；
- 步骤 7/8 → `Planned`、步骤 3/4/6/9 → `Deferred`；
- 既有 `WaferScanFlowTests.RunFlow_LocalStepsDone_DeferredStepsRemainDeferred` 更新为承认 7/8 现 `Planned`。

## 3. 12 步流程诚实状态（P6 后）

| 步骤 | 标题 | 归属 | 状态 | 说明 |
|---|---|---|---|---|
| 1 | 选创建方式 | H05 | Done | 本地 |
| 2 | 选图谱类型并生成 Map | H05 | Done | 本地 |
| 3 | 选 GoodDie Bin 并创建模板 | H03 | **Deferred** | 真实采集提模板依赖 T-029，维持搁置 |
| 4 | 调整矩形 ROI | H03 | **Deferred** | 矩形 ROI 锁定依赖 T-029，维持搁置 |
| 5 | 显示并调整匹配搜索圆 | H03 | Planned | P5 本地规划完成，设备搜索仍搁置（B-10/T-029） |
| 6 | 锁定 | H03 | **Deferred** | 锁定依赖 T-029，维持搁置 |
| 7 | 灰度模板 + Die 匹配 | H03/H06 | **Planned** | P6 离线沙盒真实跑通 B-08 引擎（合成 FOV），真实设备搜索仍搁置 |
| 8 | 降低匹配分数重试 | H03/H06 | **Planned** | P6 离线沙盒真实重跑（合成 FOV），真实 AI 推理仍搁置（T-035） |
| 9 | 填充未匹配 Die / 多建模板 | H03/H06 | **Deferred** | 填充未匹配依赖 T-029/T-035，维持搁置 |
| 10 | Die 笔刷手动改 Bin | H05 | Done | 本地 |
| 11 | Ink 笔刷改未匹配 Die / 设 ReferenceDie | H05 | Done | 本地 |
| 12 | 导出 Sinf | H05/H08 | Done | 诚实内部导出（非工业字节流，T-037 待确认） |

## 4. F↔R↔AC 追溯（新增）

| 需求/规则 | 实现位置 | 状态 |
|---|---|---|
| H06 扫描执行/对位（离线可验证部分） | `OfflineScanExecutor` + Wafer 扫描页面板 | 已交付（离线演算） |
| B-08 唯一匹配引擎复用 | `TemplateMatchingEngine.Match` 经 `OfflineScanExecutor` 调用 | 已交付 |
| H03 对位数学复用 | `MarkAlignmentService.DieToPlatform` 经 `OfflineScanExecutor` 调用 | 已交付 |
| BR-24 匹配阈值严格大于判通过 | `OfflineScanReport.MatchPassed` | 已交付 |
| T-029 未闭环前禁止生产网关 | `OfflineScanExecutor` 不引用 `IDeviceGateway` | 已遵守 |
| E-08/E-10 不伪造设备结果 | 报告 `IsOffline`+`Note` 诚实标注 | 已遵守 |

## 5. 诚实边界剩余项（非 P6 阻塞，属后续依赖）

- **T-029** 真实相机采集 / 视窗 FOV：当前由 `SyntheticFovImage` 合成替代；闭环后用真实 `IDeviceGateway` 采集 FOV 替换 `Execute` 的 FOV 来源即可，匹配/对位逻辑不变。
- **T-035** 真实 AI 推理：当前无 AI 调用；离线沙盒仅跑 B-08 几何匹配，未涉及推理。
- 步骤 3/4/6/9 的**设备采集/ROI 锁定/平台锁定/未匹配填充**仍需真实设备能力，维持 `Deferred` 直至对应 T- 项闭环。

## 6. 结论

P6 已关闭：H06 中可在离线确定性前提下真实验证的「扫描执行/对位」部分（匹配计算、对位换算、降分重试）已落地并诚实标注，12 步流程步骤 7/8 由 `Deferred` 升级为 `Planned`；真实设备采集与 AI 推理仍按 T-029/T-035 诚实搁置，未伪造任何结果。
