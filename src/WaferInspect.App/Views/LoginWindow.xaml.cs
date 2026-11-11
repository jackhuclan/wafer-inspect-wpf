using System;
using System.Windows;
using System.Windows.Input;
using WaferInspectHMI.ViewModels;

namespace WaferInspectHMI.Views;

/// <summary>
/// 登录窗口（P0 基础设施）。PasswordBox 出于安全设计不支持普通绑定，
/// 密码在 PasswordChanged 事件中同步进 ViewModel；会话建立后以 DialogResult=true 返回。
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    /// <summary>ViewModel 由通用主机的 DI 容器注入，视图不自行创建依赖。</summary>
    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        _vm.LoginSucceeded += (_, _) => DialogResult = true;
        _vm.CancelRequested += (_, _) => DialogResult = false;
        // 预填默认凭据（admin / 123456）：UserId 由 TwoWay 绑定从 VM 默认值渲染，
        // 密码框因安全设计不走绑定，这里直接赋值并借 PasswordChanged 同步进 VM。
        PasswordBox.Password = _vm.Password;
        Loaded += (_, _) => UserIdBox.Focus();
    }

    private void TitleBar_Down(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.LoginCommand.CanExecute(null))
            _vm.LoginCommand.Execute(null);
    }

    /// <summary>密码不经过 XAML 绑定，直接同步到 ViewModel。</summary>
    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _vm.Password = PasswordBox.Password;
}
