using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CircuitBench.App.Services;
using CircuitBench.App.ViewModels;
using CircuitBench.App.ViewModels.Services;
using CircuitBench.App.Views;
using CircuitBench.Core.Analysis;

namespace CircuitBench.App;

/// <summary>
/// Composition Root：在這裡手動組裝所有服務與 ViewModel（不使用 DI 容器）。
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;

    public IMessageService? Messages { get; private set; }

    /// <summary>Headless 測試可關閉自動儲存，避免寫入使用者資料夾。</summary>
    public bool EnableAutoSave { get; set; } = true;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _window = new MainWindow();
            _window.DataContext = CreateMainViewModel(() => _window);
            desktop.MainWindow = _window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public MainWindowViewModel CreateMainViewModel(Func<Window?> owner)
    {
        var time = TimeProvider.System;
        var paths = new DefaultAppPaths();
        Messages = new AvaloniaMessageService(owner);
        var files = new AvaloniaFileDialogService(() => owner(), paths);
        var autoSave = EnableAutoSave ? new AutoSaveService(time, paths) : null;
        return new MainWindowViewModel(files, Messages, new Simulator(time), autoSave);
    }
}
