#nullable enable
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaferInspect.Core.Security;

namespace WaferInspectHMI.ViewModels;

/// <summary>
/// 登录窗口状态编排（AC-P0-04 应用级登录门禁的 UI 承载）。
/// 只做凭据校验与会话建立；登录后的权限判定一律走 IAuthorizationService 显式权限，不在界面层放行。
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly IAuthorizationService _authorization;

    public LoginViewModel(IAuthenticationService authentication, IAuthorizationService authorization)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    /// <summary>认证成功且会话已建立后触发；窗口据此返回 true，进入主窗口创建流程。</summary>
    public event EventHandler? LoginSucceeded;

    /// <summary>用户主动取消登录触发；应用据此正常退出，不创建主窗口。</summary>
    public event EventHandler? CancelRequested;

    // 初始账号默认值（BR-76 / T-027 已确认保留 admin / 123456），直接预填登录框，减少手工输入。
    [ObservableProperty] private string _userId = "admin";
    [ObservableProperty] private string _password = "123456";
    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>登录命令；失败原因原样展示，不做静默重试或放行（E-05）。</summary>
    [RelayCommand]
    private void Login()
    {
        AuthenticationResult result = _authentication.Authenticate(UserId, Password);
        if (!result.Success)
        {
            ErrorMessage = result.FailureReason ?? "登录失败。";
            return;
        }

        _authorization.EstablishSession(result.User!);
        ErrorMessage = string.Empty;
        LoginSucceeded?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);
}
