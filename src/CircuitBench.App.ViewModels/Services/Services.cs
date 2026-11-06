namespace CircuitBench.App.ViewModels.Services;

public enum FileKind
{
    Project,
    Netlist,
    Csv,
    Svg,
}

/// <summary>檔案對話框（由 App 以 Avalonia StorageProvider 實作）。</summary>
public interface IFileDialogService
{
    Task<string?> OpenFileAsync(FileKind kind);

    Task<string?> SaveFileAsync(FileKind kind, string suggestedName);
}

/// <summary>訊息對話框。</summary>
public interface IMessageService
{
    Task ShowErrorAsync(string titleZh, string messageZh);

    Task<bool> ConfirmAsync(string titleZh, string messageZh);
}

/// <summary>應用程式使用的資料夾。</summary>
public interface IAppPaths
{
    /// <summary>專案檔預設位置：%UserProfile%\Documents\CircuitBench。</summary>
    string DocumentsDirectory { get; }

    /// <summary>設定與自動儲存：%LocalAppData%\CircuitBench。</summary>
    string DataDirectory { get; }
}

public sealed class DefaultAppPaths : IAppPaths
{
    public string DocumentsDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CircuitBench");

    public string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CircuitBench");
}

/// <summary>測試或無 UI 時使用：所有對話框都不做事。</summary>
public sealed class NullDialogs : IFileDialogService, IMessageService
{
    public List<string> Errors { get; } = [];

    public bool ConfirmResult { get; set; } = true;

    public Queue<string?> NextPaths { get; } = new();

    public Task<string?> OpenFileAsync(FileKind kind) => Task.FromResult(NextPaths.Count > 0 ? NextPaths.Dequeue() : null);

    public Task<string?> SaveFileAsync(FileKind kind, string suggestedName) => Task.FromResult(NextPaths.Count > 0 ? NextPaths.Dequeue() : null);

    public Task ShowErrorAsync(string titleZh, string messageZh)
    {
        Errors.Add($"{titleZh}：{messageZh}");
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string titleZh, string messageZh) => Task.FromResult(ConfirmResult);
}
