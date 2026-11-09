# AGENTS.md · 仓库组织规范

> 本文件是**项目结构与文件归属的唯一规范**，面向在本仓库工作的开发者与 AI 代理。
> 新增项目、新增文件、调整解决方案分层前先读本文件；与 README（面向使用者）分工不同：
> README 讲"怎么构建与使用"，本文件讲"东西放在哪里、为什么"。
> 组织规范参照 `aoi_twin_demo` 仓库，按本仓库当前规模裁剪。

---

## 1. 顶层布局

```
wafer-inspect-wpf/
├─ WaferInspect.slnx         解决方案（唯一入口）
├─ .editorconfig             代码风格（解决方案级）
├─ Directory.Build.props     全解决方案共享元数据：版本单点、TreatWarningsAsErrors、Deterministic
├─ Directory.Packages.props  中央包管理（CPM）：所有 PackageReference 的版本在此
├─ nuget.config              包源配置
├─ .gitignore
├─ README.md                 使用说明（构建 / 运行 / 测试 / 目录）
├─ AGENTS.md                 本文件：组织规范
├─ src/                      源码项目（可交付产物）
├─ tests/                    测试项目（不发布）
├─ tools/                    工具项目（验收 / 冒烟，不随主程序发布）
├─ assets/                   运行时资产（当前无，预留）
├─ docs/                     验收清单与报告（当前无，预留；报告与证据同批提交）
└─ .github/workflows/        CI（restore + build + test）
```

**Solution Items = 解决方案级文件**，必须留在仓库根目录：MSBuild 的
`Directory.Build.props` / `Directory.Packages.props` 只在目录树向上查找时生效，挪走即失效。
在 Visual Studio 中通过 `Solution Items` 文件夹集中呈现。

---

## 2. 代码目录的用途与归属规则

### 2.1 `src/` — 源码项目

依赖方向严格单向：`WaferInspect.App ──► WaferInspect.Core`，禁止反向引用。

| 项目 | 目标框架 | 职责 | 禁止 |
|---|---|---|---|
| `WaferInspect.Core` | `net10.0` | 纯数据契约与纯计算：左侧导航配置、视图 Key、可注入的导航目录、确定性模拟算法、`AddWaferInspectCore()` DI 注册扩展 | 引用任何 UI / Windows 类型 |
| `WaferInspect.App` | `net10.0-windows` (WinExe) | WPF 主程序：Views / ViewModels / Models / Converters / Navigation / Services / Themes，以及 Generic Host 组合根 | 在 `Core` 里塞 UI 类型；在视图里 `new` ViewModel |

`WaferInspect.App` 内部子文件夹按职责命名，不再引入解决方案级子分组：

```
src/WaferInspect.App/
├─ App.xaml(.cs)              应用入口、资源字典合并、Generic Host 组合根
├─ Views/                     窗口与 XAML 视图（构造函数注入 ViewModel）
├─ ViewModels/                页面状态编排、[ObservableProperty] / [RelayCommand]
├─ Models/                    绑定模型（ObservableBase / NavGroup / NavItem / 任务 / 告警 …）
├─ Navigation/                把 Core 的纯配置转成运行时导航模型（实例服务）
├─ Services/                  1 Hz 数据泵与其 IHostedService 托管
├─ Converters/                XAML 值转换器
└─ Themes/                    语义色板、控件模板、状态样式

src/WaferInspect.Core/
├─ Navigation/                纯导航配置、INavigationCatalog
├─ Simulation/                确定性模拟算法 ISimulationData
└─ DependencyInjection/       AddWaferInspectCore()
```

**归属判定顺序**：
1. 与菜单 / 视图标识相关的纯数据 → `Core/Navigation`；
2. WPF 对象、窗口、样式 → `App/Views` + `App/Themes`；
3. 只服务某个页面的状态编排 → `App/ViewModels`；
4. 运行时绑定模型 → `App/Models`；
5. 只在验收 / 现场运行时使用 → `tools/`。

### 2.2 `tests/` — 测试项目

命名固定 `WaferInspect.<被测层>.Tests`。当前 `WaferInspect.Core.Tests`（net10.0 + xunit）
覆盖导航配置的分组完整性、默认展开状态、菜单 Key 唯一性与默认视图归属，
并用真实 Generic Host 验证 `AddWaferInspectCore()` 的装配与模拟算法的历史数值。

规则：测试工程不得被 `src/` 引用；不写仓库目录，临时数据放 `Path.GetTempPath()`；
涉及 WPF 的用例需独立 STA 宿主，当前仅测纯配置层，不引入 WPF 依赖。

### 2.3 `tools/` — 工具项目

`WaferInspect.Smoke`（net10.0 console）：启动 Release 产物，等待后断言进程存活且
`MainWindowHandle != 0`，输出窗口句柄/标题并结束进程。不随主程序发布，CI 中不强制运行。

---

## 3. Visual Studio 解决方案分层

解决方案与磁盘结构一致，分四组呈现，不允许项目平铺在根：

```
Solution 'WaferInspect'
├─ Solution Items/  (.editorconfig / AGENTS.md / README.md / Directory.*.props / nuget.config / .gitignore)
├─ src/             (WaferInspect.Core, WaferInspect.App)
├─ tests/           (WaferInspect.Core.Tests)
└─ tools/           (WaferInspect.Smoke)
```

`WaferInspect.slnx` 用 `<Folder>` 分组、`<File>` 挂解决方案级文件、`<Project>` 挂项目。
**新增项目必须同时改两处**：磁盘目录 + slnx 对应分组；漏改会导致 CI 不编译。
分组名固定为 `Solution Items` / `src` / `tests` / `tools`。

---

## 4. 与参考规范的差异（渐进项，允许存在但需在此登记）

| 项 | 参考 `aoi_twin_demo` | 本仓库现状 | 原因 / 后续 |
|---|---|---|---|
| 分层数量 | Core / ViewModel / Adapters / Rendering / App | Core / App 两层 | 当前为单窗口上位机，无设备协议与 3D 渲染层 |
| `ViewModel` 层 | `net10.0`，禁止引用 WPF | 仍在 `App/ViewModels` | `MainViewModel` 依赖 `DispatcherTimer`、`Geometry`、`Brush`；下沉需先剥离 WPF 类型 |
| `Nullable` | `enable` | `Core` 已 `enable`；`App` 仍 `disable`（Directory.Build.props 全局注明，Gradual） | 全量启用 App 层会产生 120 条 CS86xx 警告，需整体改造绑定属性契约；随 ViewModel 下沉一并切换 |
| MVVM 框架 | 参考仓库自行实现基类 | `CommunityToolkit.Mvvm 8.4.2`：`ObservableObject` + `[ObservableProperty]` / `[RelayCommand]` 源生成器 | 删除手写 `Observable` / `RelayCommand`；保留 `ObservableBase.Raise()` 兼容跨对象通知 |
| 应用主机 | 参考仓库的通用主机 | `Microsoft.Extensions.Hosting 10.0.12`：DI、日志、`IHostedService` 生命周期 | `App.OnStartup` 建 Host、`App.OnExit` 停止并释放；`StartupUri` 已移除，窗口由容器解析 |
| `assets/` / `docs/` | 有实体目录与验收物证 | `docs/` 已有迁移 PRD 与验收依据 | `assets/` 仍预留 |

---

## 5. 新增文件 / 项目的检查清单

- [ ] 位置符合 §2 归属规则，命名符合 `WaferInspect.<层>`；
- [ ] 目标框架与所在层一致（Core / tests / tools = `net10.0`，App = `net10.0-windows`）；
- [ ] 依赖方向单向，无反向引用；`WaferInspect.slnx` 已加入对应分组；
- [ ] 菜单分组 / 默认展开状态只改 `src/WaferInspect.Core/Navigation/NavConfiguration.cs` 一处；
- [ ] PackageReference 只写 `Include`，版本在 `Directory.Packages.props`（CPM）；Core 服务注册走 `AddWaferInspectCore()`；
- [ ] 窗口 / ViewModel 由 Generic Host 解析，禁止在 View 的 code-behind 里 `new MainViewModel(...)`；新增有生命周期的服务用 `IHostedService`；
- [ ] `dotnet build WaferInspect.slnx -c Release` 0 警告 0 错误（`TreatWarningsAsErrors=true`）；
- [ ] `dotnet test WaferInspect.slnx` 全绿；`tools/WaferInspect.Smoke` 启动冒烟通过；
- [ ] 版本号只改 `Directory.Build.props` 一处；提交信息用中文 Conventional Commits，尾注版本号（如 `feat(nav): … (0.2.0)`）。
