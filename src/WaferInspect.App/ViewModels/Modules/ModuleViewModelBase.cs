using WaferInspect.Core.Navigation;
using WaferInspectHMI.Models;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// P0 模块 ViewModel 骨架。只承载模块身份与页面归属；历史业务行为必须在对应阶段实现，
/// 不得在此用静态数据或空命令冒充已迁移功能。
/// </summary>
public abstract class ModuleViewModelBase : ObservableBase
{
    protected ModuleViewModelBase(string moduleId, string displayName, string migrationStages)
    {
        ModuleId = moduleId;
        DisplayName = displayName;
        MigrationStages = migrationStages;
    }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string MigrationStages { get; }
}

public interface IModuleViewModelRegistry
{
    IReadOnlyList<ModuleViewModelBase> Modules { get; }

    ModuleViewModelBase ForView(string viewKey);
}

public sealed class ModuleViewModelRegistry : IModuleViewModelRegistry
{
    private readonly IReadOnlyDictionary<string, ModuleViewModelBase> _byModule;

    public ModuleViewModelRegistry(IEnumerable<ModuleViewModelBase> modules)
    {
        _byModule = modules.ToDictionary(module => module.ModuleId, StringComparer.Ordinal);
        Modules = _byModule.Values.OrderBy(module => module.ModuleId, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<ModuleViewModelBase> Modules { get; }

    public ModuleViewModelBase ForView(string viewKey)
    {
        var page = PageConfiguration.PageOf(viewKey);
        return page?.OwnerModule is string owner && _byModule.TryGetValue(owner, out var module)
            ? module
            : null;
    }
}
