# P0 迁移状态与双向追溯基线

> 基线：`PRD-Blade-Migration.md` P0 / E-01~E-10；本文件记录实际完成度，不把接口或页面骨架算作历史业务功能已完成。

## 1. 本次对象、范围与目标状态

- **迁移对象**：Blade 历史系统迁移所需的全新系统基线，不是对历史业务逻辑的全量交付。
- **本轮范围**：14 项导航、`analysis` / `maps` 页面承载、H01~H10 ViewModel 骨架、实时良率 `IDataSource` 边界、`IDeviceGateway` / H06 设备命令层、H09 `IEventSource`、H10 `IAuthorizationService`、H05 Map 唯一布局与统计实现、`BR-` 测试标记框架。
- **目标状态**：后续 P1~P5 只能在既定 Owner 与接口边界上实现历史功能；现有 12 项导航、5 类视图、分组折叠、三通道 Map 数值及 1 Hz 模拟节拍不回归。
- **不在本轮范围**：历史页面业务迁移、真实登录/自检、真实设备协议、持久化、文件导入导出、存量数据搬迁。
- **0.5.0 收口轮（2026-11-09）**：激活应用级登录门禁——`LoginWindow` + `LoginViewModel` + `LoginGate`；`RoleModel` 统一五级角色与继承序（T-017 结论）；`IAuthenticationService` / `IAccountStore` 认证链路与初始账号（T-027 结论，BR-76）；顶栏身份接会话；Smoke 升级为 UIAutomation 真实登录；docs 挂入 `WaferInspect.slnx` Solution Items。

## 2. P0 验收状态

| 验收 | 状态 | 证据 / 缺口 |
|---|---|---|
| `AC-P0-01` | 通过 | 4 组 14 项；导航 Key 与 `PageConfiguration` 一一对应；`NavigationConfigurationTests` 覆盖唯一性、名称、Owner 与原页面元数据 |
| `AC-P0-02` | 通过（本轮边界） | Map 布局/等级/统计仅 `WaferMapRenderer` 一份；设备命令仅 `DeviceCommandLayer` 一份；鉴权角色集仅 `RoleModel` 一份。其余历史共享控件属后续阶段，不在 P0 宣称全量 |
| `AC-P0-03` | 通过（结构） | `MainViewModel` 的良率读写依赖 `IDataSource`；替换实现单测通过。真实适配器联调受 `T-029`/`T-030` 阻塞，属 P4 边界，不影响 P0 门禁 |
| `AC-P0-04` | 通过（0.5.0） | 登录门禁已激活：`LoginWindow` 先行，`IAuthorizationService.IsAuthenticated` 为真并经 `LoginGate.EnsureAuthenticated` 校验后才创建主窗口；`RoleModel` 固化五级角色与继承序；`AuthenticationTests` 覆盖角色展开、认证成败、门禁拦截；Smoke 以 UIAutomation 真实登录（admin/123456，BR-76）后断言主窗口句柄非零 |
| `AC-P0-05` | 通过（结构） | 未新增 Review 程序、独立入口或第二套密码；`analysis` 为同一主窗口菜单页；登录入口唯一（C-03 起效） |

**结论：P0 已于 0.5.0（2026-11-09）关闭，可进入 P1（检测任务主干 C-01）。**
`T-017` 已确认：采纳 PRD 3.3 推定映射（OP→operator、ME→engineer、SE→service，Review 四角色并入同名角色，technician 保留），角色集固化于 `Core/Security/RoleModel`。
`T-027` 已确认：初始账号 admin / 123456（BR-76）按历史行为原样保留，登记于 `InMemoryAccountStore` 并有 BR-76 断言测试；哈希存储与强制改密可作为后续安全变更替换 `IAuthenticationService` 实现。

## 3. F ↔ R ↔ AC 双向追溯基线

> `F-` 的逐编号唯一 Owner 以 PRD 2.0.4 为准；下表把每个模块的完整 F 集合关联到迁移需求与验收组。状态列只记录本轮实际进展。

| Owner | F- 范围 / 数量 | R- 需求 | AC- 验收 | 本轮状态 |
|---|---|---|---|---|
| `H01` | PRD 2.0.4，79 | `R-H01-01~10` | `AC-H01-01~07` | 仅 ViewModel 骨架；P1 未开始 |
| `H02` | PRD 2.0.4，38 | `R-H02-01~08` | `AC-H02-01~07` | 仅 ViewModel 骨架；P2 未开始 |
| `H03` | PRD 2.0.4，43 | `R-H03-01~06` | `AC-H03-01~04` | 仅 ViewModel 骨架；P2/P4 未开始 |
| `H04` | PRD 2.0.4，24 | `R-H04-01~06` | `AC-H04-01~04` | 仅 ViewModel 骨架；P2 未开始 |
| `H05` | PRD 2.0.4，26 | `R-H05-01~07` | `AC-H05-01~05` | `R-H05-01` 菜单/承载已完成；Map 渲染仅完成现有演示算法抽取，`R-H05-03` 的 Bin 着色、交互、刷新全量契约未完成 |
| `H06` | PRD 2.0.4，52 | `R-H06-01~06` | `AC-H06-01~05` | `R-H06-06` 仅完成命令层抽象与结果透传；控件、配置读取、真实设备行为未完成 |
| `H07` | PRD 2.0.4，21 | `R-H07-01~06` | `AC-H07-01~04` | 仅 ViewModel 骨架；P4 未开始 |
| `H08` | PRD 2.0.4，64 | `R-H08-01~09` | `AC-H08-01~08` | `R-H08-01` 仅完成菜单入口；三子页与 Review 业务均未迁移 |
| `H09` | PRD 2.0.4，18 | `R-H09-01~06` | `AC-H09-01~05` | `R-H09-03` 仅完成统一事件源骨架；日志格式、报警处理与联动未迁移 |
| `H10` | PRD 2.0.4，36 | `R-H10-01~09` | `AC-H10-01~06` | 仅完成统一会话/显式权限骨架；登录、自检、角色合并未完成 |
| `X01` | `F-A-01`、`F-B-01~04`，5 | `R-X01-01~04` | `AC-X01-01~03` | 仅建立 `BR-` 测试标记与断言辅助；50 术语、4 类单位、78 条规则的全量断言未完成 |

反向检查：本轮新增 `analysis` / `maps` 均有 PRD 5.2 来源；接口和测试工具均标记为 P0 基础设施，不冒充新增业务功能。未删除任何历史 `F-` / `R-` / `AC-` 定义。

## 4. 兼容性处理

- 原有 12 个菜单 Key、标题、面包屑、默认展开状态保持不变；新增项只插入 PRD 指定分组。
- 原 `overview` / `tasks` / `alarms` / `live` / 通用数据表五类承载保持，新增骨架单独使用第六类承载。
- Map 算法原样迁移，三通道快照锁定：PL `217/52/36/12`、EL `204/66/30/17`、AOI `221/49/34/13`（正常/轻微/警告/缺陷），总数均为 317。
- `ISimulationData` 继承 `IDataSource`，既有模拟算法及调用方保持兼容；真实数据替换不影响 XAML 绑定。
- 设备命令参数和适配器结果不做静默改写；未提供生产网关，不伪造设备成功。
- 未登录与未显式授权均拒绝；未内置 `admin` / `123456`，未把 OP 解释为全权限。

## 5. 阻塞与需补充资料

| 编号 | 需要补充 | 影响 |
|---|---|---|
| ~~`T-017`~~ | **已确认（2026-11-09）**：采纳 PRD 3.3 推定映射，technician 保留 | 已闭环，不再阻塞 |
| ~~`T-027`~~ | **已确认（2026-11-09）**：初始账号按历史行为原样保留，兼容行为已记录 | 已闭环，不再阻塞 |
| `T-029` | EFEM/ACS/相机/光源/PLC/IO/SECS-GEM/OCR/FTP 的协议、地址、命令、返回码与异常契约 | 阻塞真实 `IDeviceGateway` 与联调（P4 边界） |
| `T-030` | Job/Recipe/MapType/结果/用户等存量数据格式与搬迁规则 | 阻塞真实数据源、真实账号存储和兼容性验证（P3/P4 边界） |

## 6. 验证记录（0.5.0）

- `dotnet build WaferInspect.slnx -c Release`：0 警告 0 错误（`TreatWarningsAsErrors` 生效）。
- `dotnet test WaferInspect.slnx -c Release`：34/34 通过（新增 12 项认证/角色/门禁断言，含 `BR-76` 标记测试）。
- `WaferInspect.Smoke`（UIAutomation 真实登录）：登录窗口出现 → 注入 admin/123456 → 点击登录 → 主窗口出现，`hwnd` 非零，标题 `WaferInspect · 晶圆检测上位机`。
- 门禁回归：未认证时主窗口不创建（`LoginGate` 单测 + Smoke 失败诊断路径双覆盖）。
- GitHub Actions 结论以推送后的实际 run 为准；未执行或未完成的业务验收不写成通过。

## 7. 残留与后续边界（进入 P1 前登记）

- 主界面顶栏身份已接登录会话（`OperatorName`/`OperatorRole` 来自 `IAuthorizationService`）；「退出登录」按钮仍是演示占位（仅提示文案），退出→重登录回路属 H10 完整迁移（P1 最小闭环 / P5 完整）。
- 登录窗口为 P0 基础设施，样式为统一主题的最小实现；历史登录界面的完整交互（自检提示、身份切换）在 P1 `R-H10-*` 迁移。
- 初始账号为明文内存存储（与历史行为一致）；生产化哈希与改密流程待后续安全变更。
