# WaferInspect · 晶圆检测上位机

.NET 10 WPF 桌面端上位机原型：设备状态、检测任务、EL/PL/AOI 晶圆图谱、实时良率趋势与告警闭环。
左侧导航按配置分组，每组可独立展开 / 收起。

## 环境

- .NET SDK 10.0.x（含 `Microsoft.WindowsDesktop.App` 运行时）
- Windows（WPF / `net10.0-windows`）

## 构建与运行

```bash
dotnet restore WaferInspect.slnx
dotnet build WaferInspect.slnx -c Release
dotnet run --project src/WaferInspect.App/WaferInspect.App.csproj -c Release
```

直接运行产物：

```bash
src/WaferInspect.App/bin/Release/net10.0-windows/WaferInspectHMI.exe
```

## 测试与冒烟

```bash
dotnet test WaferInspect.slnx -c Release
dotnet run --project tools/WaferInspect.Smoke/WaferInspect.Smoke.csproj -c Release
```

`WaferInspect.Smoke` 会启动 Release 产物，经 UIAutomation 以初始账号（BR-76：admin / 123456）
**真实登录**后断言进程存活且已创建主窗口句柄（登录门禁生效后未认证不出现主窗口），
输出窗口标题；失败时输出顶层窗口枚举与登录错误文本辅助定位。退出码 `0` 为通过。也可显式传入 exe 路径：

```bash
dotnet run --project tools/WaferInspect.Smoke -c Release -- <绝对路径/WaferInspectHMI.exe>
```

## 目录

| 目录 | 用途 |
|---|---|
| `src/WaferInspect.Core` | 纯契约与计算：`Navigation/`、`Data/`、`Devices/`、`Events/`、`Security/`、`Maps/`、`Simulation/`、`DependencyInjection/` |
| `src/WaferInspect.App` | WPF 主程序：`Views/`、`ViewModels/`（含 `Modules/` 的 H01~H10 骨架）、`Models/`、`Services/` 等 |
| `tests/WaferInspect.Core.Tests` | 导航、Core 装配、Map 快照、数据替换、设备/事件/鉴权、角色模型与登录门禁单测 |
| `tools/WaferInspect.Smoke` | 启动冒烟工具 |
| `.github/workflows/build.yml` | CI：restore + Release 构建 + 单元测试 |

组织规范见 [AGENTS.md](AGENTS.md)，代码风格见 `.editorconfig`，
版本与共享元数据在 `Directory.Build.props`，NuGet 包版本在 `Directory.Packages.props`（CPM）。

## 架构（0.5.0）

| 关注点 | 实现 |
|---|---|
| MVVM | `CommunityToolkit.Mvvm 8.4.2`：`ObservableObject` 基类 + `[ObservableProperty]` / `[RelayCommand]` 源生成器；XAML 绑定属性名与命令名保持不变 |
| 组合根 | `Microsoft.Extensions.Hosting 10.0.12` 的 Generic Host：`App.OnStartup` 注册并启动、`App.OnExit` 停止并释放；`StartupUri` 已移除，`MainWindow` 与 `MainViewModel` 均由容器解析 |
| 依赖注入 | `Core` 提供 `AddWaferInspectCore()`：导航、`IDataSource`、Map、事件、鉴权与设备命令层；`App` 追加数据泵、模块 ViewModel 注册表、窗口 |
| 生命周期 | 1 Hz 节拍由 `DispatcherTimerLiveDataPump` 产生，`SimulationHostedService : IHostedService` 随主机启停；日志由通用主机提供（默认 Warning 级别） |
| 可测性 | 确定性良率算法与导航目录在 `Core`（net10.0、无 WPF 类型），测试项目用真实 Host 验证装配与历史数值 |

包版本集中维护在 `Directory.Packages.props`（CPM），各 `csproj` 的 `PackageReference` 不写版本号。

启动链路：`App.OnStartup` → 构建 Host → `host.Start()`（启动数据泵）→ 登录窗口（`LoginWindow`，唯一登录入口）→ 认证成功经 `LoginGate` 校验 → 解析 `MainWindow`（注入 `MainViewModel`）→ `Show()`；取消登录则应用退出。

## Blade 迁移状态（P0 已关闭）

P0 基线：4 组 14 项导航，新增「数据分析」(`analysis`) 与「图谱管理」(`maps`)，
两页均明确标识为尚未迁移的骨架，不展示伪造的历史业务数据。H01~H10 ViewModel 已注册，
实时良率通过 `IDataSource` 读取，Map 布局统计由 H05 `IMapRenderer` 唯一提供。

0.5.0 激活应用级登录门禁：统一角色模型（`Core/Security/RoleModel`，T-017 已确认）、
认证链路与初始账号（T-027 已确认，BR-76 按历史行为保留）。P0 已关闭，下一阶段为
P1（检测任务主干 C-01）。完整验收状态、F↔R↔AC 追溯和待确认项见
[`docs/P0-Migration-Status.md`](docs/P0-Migration-Status.md)，实现硬约束见 `AGENTS.md` §6。

## 左侧导航配置

菜单只在一个地方定义：`src/WaferInspect.Core/Navigation/NavConfiguration.cs`。

```csharp
new NavGroupDefinition("system", "系统", /* DefaultExpanded */ false,
[
    new NavItemDefinition("alarms", "告警中心", "M6 16h12l-2-3V9H8v4l-2 3z M10 19h4", "3"),
    ...
])
```

- `DefaultExpanded` 控制首次加载时该组是展开还是收起（示例里"系统"组默认收起）；
- 折叠组只隐藏组内项，不改变选中项；导航到被折叠组内的视图时会自动展开该组；
- 组标题的展开 / 折叠箭头、项数徽标、蓝色选中指示点，以及菜单项的选中高亮条，
  共同构成状态反馈；样式集中在 `Themes/Theme.xaml` 的 `NavGroupToggle` / `NavRadio`。

## 已知限制

- `Nullable` 暂为 `disable`（ViewModel 仍依赖 WPF 类型，详见 AGENTS.md §4）；
- 数据仍为 1 Hz 模拟数据；良率读取已切换到可替换的 `IDataSource`，但真实设备 / 数据库适配器尚未接入；
- 配套的高保真 HTML 原型不在本仓库内。
