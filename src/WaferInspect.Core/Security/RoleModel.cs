namespace WaferInspect.Core.Security;

/// <summary>
/// 统一角色模型（PRD 3.3 目标态）。
/// 【T-017 已确认 2026-11-09】采纳 PRD 3.3 推定映射：主界面 OP→operator、ME→engineer、SE→service；
/// Review 四角色并入同名统一角色；technician 保留，历史来源登记为 Review。
/// 能力继承：admin ⊇ service ⊇ engineer ⊇ technician ⊇ operator。
/// 逐模块操作权限矩阵仍按【待确认 T-018】在 P5 逐格确认，本表只固化角色集与继承序，
/// 不在此预填任何具体操作权限（E-08：不静默省略，也不臆造未确认行为）。
/// </summary>
public static class RoleModel
{
    public const string Operator = "operator";
    public const string Technician = "technician";
    public const string Engineer = "engineer";
    public const string Service = "service";
    public const string Admin = "admin";

    /// <summary>继承序：低 → 高；每个角色的有效角色集为该序中不高于自身的前缀。</summary>
    public static IReadOnlyList<string> HierarchyOrder { get; } =
        [Operator, Technician, Engineer, Service, Admin];

    /// <summary>统一角色的中文名，与 PRD 3.3 目标态一致。</summary>
    public static string DisplayNameOf(string role) => role switch
    {
        Operator => "操作员",
        Technician => "技术员",
        Engineer => "维护工程师",
        Service => "服务工程师",
        Admin => "管理员",
        _ => throw new ArgumentException($"未知的统一角色：{role}", nameof(role))
    };

    /// <summary>
    /// 按能力继承展开有效角色集（含自身）。例如 admin 展开为全部五级，
    /// service 展开为 operator/technician/engineer/service 且不含 admin。
    /// </summary>
    public static IReadOnlyList<string> EffectiveRoles(string role)
    {
        int index = Array.IndexOf([.. HierarchyOrder], role);
        if (index < 0)
            throw new ArgumentException($"未知的统一角色：{role}", nameof(role));
        return HierarchyOrder.Take(index + 1).ToArray();
    }
}
