using CircuitBench.Schematic.Persistence;

namespace CircuitBench.App.ViewModels.Services;

public sealed record RecoveryInfo(string AutoSavePath, DateTimeOffset SavedAt, ProjectFile Project);

/// <summary>
/// 自動儲存與當機回復：
/// <list type="number">
/// <item>啟動時檢查 session.lock：若存在（上次沒有正常關閉）且有自動儲存檔，提供回復。</item>
/// <item>寫入 session.lock，之後以 TimeProvider 定時（預設 60 秒）在有變更時寫入 autosave.cbench
/// （先寫暫存檔再取代，避免寫到一半的檔案）。</item>
/// <item>正常關閉時刪除 lock 與自動儲存檔。</item>
/// </list>
/// </summary>
public sealed class AutoSaveService : IDisposable
{
    public const string LockFileName = "session.lock";
    public const string AutoSaveFileName = "autosave" + ProjectSerializer.FileExtension;

    private readonly TimeProvider _time;
    private readonly IAppPaths _paths;
    private ITimer? _timer;
    private Func<ProjectFile?>? _snapshot;
    private Func<bool>? _isDirty;
    private readonly object _gate = new();

    public AutoSaveService(TimeProvider time, IAppPaths paths, TimeSpan? interval = null)
    {
        _time = time;
        _paths = paths;
        Interval = interval ?? TimeSpan.FromSeconds(60);
    }

    public TimeSpan Interval { get; }

    public string LockPath => Path.Combine(_paths.DataDirectory, LockFileName);

    public string AutoSavePath => Path.Combine(_paths.DataDirectory, AutoSaveFileName);

    public DateTimeOffset? LastSavedAt { get; private set; }

    public int SaveCount { get; private set; }

    /// <summary>上次未正常關閉且有可讀的自動儲存檔時回傳回復資訊。</summary>
    public RecoveryInfo? CheckForRecovery()
    {
        if (!File.Exists(LockPath) || !File.Exists(AutoSavePath))
        {
            return null;
        }

        try
        {
            var result = ProjectSerializer.Deserialize(File.ReadAllText(AutoSavePath));
            return result.Project is { } p ? new RecoveryInfo(AutoSavePath, File.GetLastWriteTimeUtc(AutoSavePath), p) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Start(Func<ProjectFile?> snapshot, Func<bool> isDirty)
    {
        _snapshot = snapshot;
        _isDirty = isDirty;
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(LockPath, _time.GetUtcNow().ToString("O"));
        _timer = _time.CreateTimer(_ => SaveNow(), null, Interval, Interval);
    }

    /// <summary>有變更時立即寫入自動儲存檔。回傳是否寫入。</summary>
    public bool SaveNow()
    {
        lock (_gate)
        {
            if (_snapshot is null || _isDirty is null || !_isDirty())
            {
                return false;
            }

            var project = _snapshot();
            if (project is null)
            {
                return false;
            }

            Directory.CreateDirectory(_paths.DataDirectory);
            var temp = AutoSavePath + ".tmp";
            File.WriteAllText(temp, ProjectSerializer.Serialize(project));
            File.Move(temp, AutoSavePath, overwrite: true);
            LastSavedAt = _time.GetUtcNow();
            SaveCount++;
            return true;
        }
    }

    /// <summary>正常關閉：停止計時器並刪除 lock 與自動儲存檔。</summary>
    public void MarkCleanShutdown()
    {
        _timer?.Dispose();
        _timer = null;
        TryDelete(LockPath);
        TryDelete(AutoSavePath);
    }

    /// <summary>使用者選擇不回復時，捨棄舊的自動儲存。</summary>
    public void DiscardRecovery() => TryDelete(AutoSavePath);

    public void Dispose() => _timer?.Dispose();

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 檔案被占用時保留，下次啟動仍可回復；不影響關閉流程
        }
    }
}
