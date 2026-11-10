# P3 迁移状态 · 数据分析与人工复判（H08）

> 对应 `docs/PRD-Blade-Migration.md` §8.2 `P3` 与 C-02「数据分析」承载页，以及 `H08`（M09 Review 系统，64 项功能点）。
> 本文件只记录**事实状态**，不虚报完成度（E-10）。
> 阶段结论：**P3 首增量（增量 6：H08 数据分析主页面与复判全景 Map）已交付 1.2.0**；
> 同时收口 `AC-H05-04` 的复判全景 Map 控件统一（复用 H05 `MapGridView`，无第二份绘制规则）。
> 剩余 P3 能力（结果输出 B-13/B-14、Sinf/Klarf 导出、按 Die 查看、GoldenDie/回溯图/复判图、
> AI 重新推理）待后续增量，界面显式登记未迁移，不伪造入口。

## 1. 本轮范围与结论

| 项 | 内容 |
|---|---|
| 交付版本 | **1.2.0**（P3 首增量 6：H08 数据分析与人工复判主页面） |
| 覆盖范围 | 数据分析主页面（`analysis`，去 P0 骨架）+ 左侧 Wafer 检索（LotId/WaferId/机台号筛选）+ 中部复判全景 Map（复用 `MapGridView`，关闭 `AC-H05-04`）+ 右侧复判前/后/AI 良率面板 + 人工/批量/AI 复判执行（AI 标搁置）+ 缺陷列表多维筛选（Zone/类别/复判状态） |
| 不覆盖 | 结果输出（B-13 导出动作、B-14 报表/Klarf/2D 量测/路径复制）、Sinf 生成（B-12，T-037 格式规范待确认）、按 Die 查看、GoldenDie/回溯图/复判图可视化、AI 重新推理（T-035 模型未部署）；复判配置（B-15）已在 P2 增量 2 随 Recipe 落库，本增量仅运行时读取 |
| 前置闭环 | 无新增待确认项；复用 H04 `DefectRecord`（B-23）、H05 `IMapTypeCatalog`/`MapGridModel`/`MapGridView`（B-06/B-11/BR-53）、`WaferRunController` 统计口径 |
| 阶段门禁 | `AC-H05-04`（复判全景 Map 控件统一）已收口：唯一绘制控件 `MapGridView`；`B-11` 颜色/快捷键单一数据源；`BR-53` 复判后不自动刷新；`B-16` 复判执行写回；`B-23` 缺陷模型唯一 |

## 2. 已交付明细

### 2.1 领域与接口（`src/WaferInspect.Core/Analysis/`，经 `AddWaferInspectCore()` 单例注册）

| 类型 | 职责与规则口径 |
|---|---|
| `WaferResult`（`WaferResultModels.cs`） | H08 单枚 Wafer 结果载体：`DesignDieCount`（F-3.9.2-02）、`PreReviewYield`（F-3.9.2-07）、`PostReviewYield`（F-3.9.2-08，未复判显式 `0d`）、`AiReviewYield`（F-3.9.2-09，AI 未部署 `0d`）、`PostReviewDefectCount`；`WithBin`/`WithBatchRecheck`/`WithAiReviewed` 保证 Bin 矩阵与 `DefectRecord.ReviewState`（B-23）一致 |
| `IWaferResultStore` / `WaferResultStore` | B-23 数据源之一：当前为内存确定性种子（同进程同 `Random(20261110)` 恒得同一组 6 枚 Wafer、Bin 矩阵、缺陷），`Wafers`/`Get`/`Update`/`Query(lotId,waferId,toolId)`（F-3.9.2-01~02），`Changed` 广播；落盘随 H07 配置单点 |
| `IRecheckService` / `RecheckService` | **B-16 复判执行唯一实现**：`ManualRecheck`/`BatchRecheck` 经 store 写回并广播 `Changed`；`AiRecheck` 在模型未部署（T-035）时**仅返回 `Executed=false` 与明确搁置说明，不修改任何数据、不伪造推理**（E-10 / P1 设备与算法搁置边界） |
| `MapGridModel` 扩展（`Maps/MapGridModel.cs`） | H08 复判全景 Map 复用 H05 唯一模型：`LoadWafer(mapTypeId, catalog, matrix, rows, cols)`（B-06/B-11，颜色/代码/等级取自 `IMapTypeCatalog`）、`Reassign(column,row,bin)`（BR-53：仅更新逻辑字段、`ColorHex` 故意保留旧值、`NeedsRefresh=true`）、`RecolorWafer(mapTypeId, catalog)`（BR-53：唯一显式刷新入口，按当前 Kind 经 catalog 重新取色） |

### 2.2 界面（`src/WaferInspect.App`）

| 文件 | 内容 |
|---|---|
| `Views/AnalysisView.xaml(.cs)` | 三栏：左 Wafer 检索（筛选框 + `GridWafers` DataGrid + 模拟结果提示）；中复判全景 Map（`views:MapGridView Model="{Binding Map}"` + 生成/跳转/刷新按钮 + 三行 `Mode=OneWay` 状态文本）；右良率面板（`YieldSummaryText`）+ 复判执行（`AvailableBins` ComboBox + 人工/批量/快捷键/AI 按钮）+ 缺陷列表（`GridDefects` 多维筛选）。全控件带 `AutomationProperties.AutomationId` |
| `ViewModels/Modules/H08Items.cs` | `WaferRow`（WaferResult 投影：设计 Die/复判前缺陷/已复判数 + `PreReviewYieldText`/`PostReviewYieldText`(0.00%)/`AiReviewYieldText`）、`H08BinOption`（Kind/Code/Name/Shortcut，取自 `IMapTypeCatalog`，B-11） |
| `ViewModels/Modules/H08AnalysisViewModel.cs` | 替换 P0 骨架占位：注入 `IMapTypeCatalog`/`IWaferResultStore`/`IRecheckService`，基类 `ModuleViewModelBase("H08","数据分析与人工复判","P3（增量 6…）")`；订阅 `catalog.Changed`/`store.Changed`/`recheck.Changed`/`map.Changed`；命令 `SelectWafer`/`GenerateMap`/`RefreshMap`(BR-53)/`JumpToDie`/`RecheckSelected`/`BatchRecheck`/`ApplyShortcut`(B-11)/`AiRecheck`；`DeferredNote` 登记导出/AI 未迁移且不伪造 |
| `ViewModels/MainViewModel.cs` | 新增 `IsAnalysis` + `Analysis` 属性，`Navigate` 中 `key=="analysis"` 时置位并排除 `IsGeneric` |
| `Views/MainWindow.xaml` | 新增 `IsAnalysis` 承载 Grid（同 H02/H04/H05 的 `IsX` 承载约定） |
| `Core/Navigation/PageConfiguration.cs` | `analysis` 去 `IsMigrationSkeleton=true`（与 `maps` 一致为非骨架真实页面） |
| `App.xaml.cs` | 注册 `H08AnalysisViewModel` 为自身类型（供 `MainViewModel` 新 ctor 依赖解析） |

### 2.3 业务规则断言（`WaferResultStoreTests` 8 + `RecheckServiceTests` 5 + `MapGridModelRecheckTests` 5 = 18 项；另 `NavigationConfigurationTests` 适配 analysis 去骨架）

| 规则 | 断言位置 |
|---|---|
| `B-11` | `MapGridModelRecheckTests.LoadWafer_BuildsCellsFromMatrix_WithColorsFromCatalog`（颜色来自 `IMapTypeCatalog`，非本地写死）、`RecheckService`/`H08BinOption` 快捷键解析来自 catalog |
| `BR-53` | `MapGridModelRecheckTests.Reassign_KeepsOldColor_UntilExplicitRecolor`（复判后 `ColorHex` 不变、`NeedsRefresh=true`）、`RecolorWafer_IsOnlyRefreshEntry_AfterReassign`（唯一刷新入口、`RefreshVersion++`）；`Reassign` 之后 `MapGridView` 颜色不变，须点「刷新图谱」 |
| `B-16` | `RecheckServiceTests.ManualRecheck_WritesBack_…`（写回 + `ReviewedCount+1` + 缺陷 `ReviewState=已复判`）、`BatchRecheck_RewritesAllOfKind_…`（整类改写 + 计数）、`AiRecheck_DoesNotFakeResult_…`（`Executed=false` + `[搁置]…T-035` + 不写回 + 不广播） |
| `B-23` | `WaferResultStoreTests.Defects_CarryDeferredSourceImagePath`（缺陷原始图片路径标注 `[搁置]`，T-029 未接入）、`ManualRecheck` 同步缺陷复判状态 |
| 确定性种子 | `WaferResultStoreTests.Seed_*`：6 枚 Wafer、跨实例矩阵一致、Lot/Tool 分组、最外圈 `ForBiddenDie`、复判前缺陷计数口径 |
| 导航去骨架 | `NavigationConfigurationTests.MigrationPages_PreserveRequiredNamesAndOwners`（analysis 仍属 `H08` 但 `IsMigrationSkeleton=false`）、`ExistingPages_KeepLegacyTitlesCrumbsAndNonSkeletonState`（全部页面非骨架） |

## 3. AC-H05-04 关闭证据

- 复判全景 Map 直接复用 H05 唯一控件 `MapGridView`（输入契约仅 `Model="{Binding Map}"`），**未复制任何坐标换算 / Bin 着色 / 刷新规则**；
- `MapGridModel` 的 `LoadWafer`/`Reassign`/`RecolorWafer` 是唯一逻辑入口，颜色/代码/等级/快捷键 100% 来自 `IMapTypeCatalog`（B-11）；
- 首页圆片概览仍消费历史 `WaferMapRenderer`，但绘制规则（等级配色）同样唯一来自 `MapLevelPalette`（H05 Core），不存在第二份坐标/着色实现；
- 结论：**`AC-H05-04` 的「复判全景 Map 控件统一」已收口**，P2 其余项（H02~H05 增量 1~5）此前均已交付 → **P2 关闭**。

## 4. 验证证据（1.2.0）

- `dotnet build WaferInspect.slnx -c Release`：0 警告 0 错误；
- `dotnet test WaferInspect.slnx -c Release`：**174/174 通过**（新增 `WaferResultStoreTests` 8、`RecheckServiceTests` 5、`MapGridModelRecheckTests` 5，覆盖 BR-53、B-11、B-16、B-23、确定性种子、缺陷筛选、良率计算）；
- `WaferInspect.Smoke`：真实登录后主窗口句柄非零；
- 门禁脚本 `tools/verify-p3-h08.ps1`：自动执行 `build` + `test` + `ui-error.log` 空校验，收口打印 `ALL DONE`；
- 关键契约：BR-53 复判后 `MapGridView` 颜色不变、须点「刷新图谱」；B-16 AI 复判返回 `Executed=false` 与 `[搁置] AI 模型未部署（T-035）…`；B-23 缺陷 `SourceImagePath` 含 `[搁置]`。

## 5. 已知限制（显式登记，不静默）

1. **数据与设备侧能力仍搁置**：Wafer 结果为内存确定性合成，界面统一标注「[模拟结果] 设备采集（T-029）/ AI 推理（T-035）未接入」，不伪造设备执行成功；
2. **Wafer 结果仓库为内存实现**：重启回到种子数据，落盘随 H07 配置单点确定；
3. **结果输出未迁移**（B-13/B-14）：Sinf/Klarf/报表/2D 量测/路径复制、按 Die 查看、GoldenDie/回溯图/复判图可视化，界面显式登记未迁移；Sinf 生成格式规范 `T-037` 待确认，禁止自拟格式冒充；
4. **AI 重新推理（T-035）未部署**：`AiRecheck` 仅返回明确搁置结果，不调用任何模型、不写回；
5. **H05 P3/P4 能力仍属后续增量**：Wafer 扫描 12 步（R-H05-04）、Die 笔刷/整行整列/批量/外扩内缩（R-H05-05），不伪造入口；
6. **`AC-H05-04` 仅复判全景 Map 部分收口**：首页圆片概览沿用 P1 历史视觉控件（`WaferMapRenderer`），绘制规则唯一来自 H05 Core，无第二份实现；如需完全统一为 `MapGridView` 属产品决策，非功能性缺口。
