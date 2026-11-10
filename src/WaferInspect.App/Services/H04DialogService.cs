using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using WaferInspect.Core.Inspection;
using WaferInspectHMI.ViewModels.Modules;
using WaferInspectHMI.Views;

namespace WaferInspectHMI.Services;

/// <summary>H04 对话框服务的 WPF 实现：只负责开窗 / 选文件 / 枚举本地图片，不承载业务规则。</summary>
public sealed class H04DialogService : IH04DialogService
{
    private static readonly string[] ImagePatterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff"];

    private readonly Func<string, ParameterValidationDialog> _validationFactory;

    public H04DialogService(Func<string, ParameterValidationDialog> validationFactory)
        => _validationFactory = validationFactory;

    public string PickImageFolder(string currentFolder)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "加载文件夹测试（F-3.8.1-02）：选择该文件夹内任一图片即可定位目录",
            Filter = "图像文件 (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
            dialog.InitialDirectory = currentFolder;
        return dialog.ShowDialog(Owner) == true
            ? Path.GetDirectoryName(dialog.FileName) ?? string.Empty
            : string.Empty;
    }

    public IReadOnlyList<string> EnumerateImages(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];
        return ImagePatterns
            .SelectMany(pattern => Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();
    }

    public VerificationOutcome ShowParameterValidation(string recipeId)
    {
        var dialog = _validationFactory(recipeId);
        dialog.Owner = Owner;
        dialog.ShowDialog();
        return dialog.LastOutcome;
    }

    public void ShowMessage(string title, string message)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string title, string message)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
           == MessageBoxResult.Yes;

    private static Window Owner => Application.Current?.MainWindow;
}
