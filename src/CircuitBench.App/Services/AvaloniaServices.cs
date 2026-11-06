using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CircuitBench.App.ViewModels.Services;

namespace CircuitBench.App.Services;

/// <summary>以 Avalonia StorageProvider 實作的檔案對話框。</summary>
public sealed class AvaloniaFileDialogService(Func<TopLevel?> topLevel, IAppPaths paths) : IFileDialogService
{
    private static FilePickerFileType TypeFor(FileKind kind) => kind switch
    {
        FileKind.Project => new("電路實驗台專案") { Patterns = ["*.cbench"] },
        FileKind.Netlist => new("SPICE 網表") { Patterns = ["*.cir", "*.sp", "*.net", "*.txt"] },
        FileKind.Csv => new("CSV（UTF-8）") { Patterns = ["*.csv"] },
        _ => new("SVG 圖片") { Patterns = ["*.svg"] },
    };

    private async Task<IStorageFolder?> StartFolder(IStorageProvider sp)
    {
        Directory.CreateDirectory(paths.DocumentsDirectory);
        return await sp.TryGetFolderFromPathAsync(paths.DocumentsDirectory);
    }

    public async Task<string?> OpenFileAsync(FileKind kind)
    {
        if (topLevel()?.StorageProvider is not { } sp)
        {
            return null;
        }

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "開啟",
            AllowMultiple = false,
            FileTypeFilter = [TypeFor(kind)],
            SuggestedStartLocation = await StartFolder(sp),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(FileKind kind, string suggestedName)
    {
        if (topLevel()?.StorageProvider is not { } sp)
        {
            return null;
        }

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "儲存",
            SuggestedFileName = suggestedName,
            FileTypeChoices = [TypeFor(kind)],
            SuggestedStartLocation = await StartFolder(sp),
        });
        return file?.TryGetLocalPath();
    }
}

/// <summary>簡單的模態訊息視窗。</summary>
public sealed class AvaloniaMessageService(Func<Window?> owner) : IMessageService
{
    public async Task ShowErrorAsync(string titleZh, string messageZh) => await Show(titleZh, messageZh, withCancel: false);

    public Task<bool> ConfirmAsync(string titleZh, string messageZh) => Show(titleZh, messageZh, withCancel: true);

    private async Task<bool> Show(string title, string message, bool withCancel)
    {
        var result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "確定", MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = "取消", MinWidth = 80, IsCancel = true, IsVisible = withCancel };
        ok.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } },
            },
        };

        if (owner() is { } parent)
        {
            await dialog.ShowDialog(parent);
        }
        else
        {
            dialog.Show();
        }

        return result;
    }
}
