using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CircuitBench.App.ViewModels.Analysis;
using CircuitBench.App.ViewModels.Editor;
using CircuitBench.App.ViewModels.Results;
using CircuitBench.App.ViewModels.Services;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.IO;
using CircuitBench.Schematic.Editing;
using CircuitBench.Schematic.Export;
using CircuitBench.Schematic.Persistence;

namespace CircuitBench.App.ViewModels;

public enum Page
{
    Editor,
    Waveform,
    Bode,
    Diagnostics,
}

/// <summary>主視窗：分頁導覽、檔案操作與各子 ViewModel 的組合。</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogService _files;
    private readonly IMessageService _messages;
    private SchematicDocumentVersion _savedVersion;

    public MainWindowViewModel(IFileDialogService files, IMessageService messages, ISimulator simulator, AutoSaveService? autoSave = null)
    {
        _files = files;
        _messages = messages;
        AutoSave = autoSave;
        Editor = new EditorViewModel();
        Waveform = new WaveformViewModel();
        Bode = new BodeViewModel();
        Diagnostics = new DiagnosticsViewModel();
        Analysis = new AnalysisViewModel(Editor, simulator, Waveform, Bode, Diagnostics);
        AnalysisForm = new AnalysisSettingsViewModel(Analysis);
        Properties = new PropertyPanelViewModel(Editor);
        Analysis.Completed += (_, kind) => CurrentPage = kind switch
        {
            AnalysisKind.Ac => Page.Bode,
            AnalysisKind.OperatingPoint => Page.Diagnostics,
            _ => Page.Waveform,
        };
        Analysis.Failed += (_, message) => _ = _messages.ShowErrorAsync("分析未執行", message);
        Analysis.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AnalysisViewModel.Settings))
            {
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(Title));
            }
        };
        Editor.DocumentChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Title));
        };
        _savedVersion = Version;
    }

    public EditorViewModel Editor { get; }

    public AnalysisViewModel Analysis { get; }

    public AnalysisSettingsViewModel AnalysisForm { get; }

    public PropertyPanelViewModel Properties { get; }

    public WaveformViewModel Waveform { get; }

    public BodeViewModel Bode { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public AutoSaveService? AutoSave { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string? _filePath;

    [ObservableProperty]
    private Page _currentPage = Page.Editor;

    public bool IsDirty => !Version.Equals(_savedVersion);

    public string Title => $"電路實驗台 CircuitBench — {(FilePath is null ? "未命名" : Path.GetFileName(FilePath))}{(IsDirty ? " *" : string.Empty)}";

    private SchematicDocumentVersion Version => new(Editor.Document, Analysis.Settings);

    /// <summary>目前狀態轉成專案檔內容。</summary>
    public ProjectFile Snapshot() => new(Editor.Document, Analysis.Settings,
        new ViewSettings
        {
            Zoom = Editor.Viewport.Zoom,
            PanX = Editor.Viewport.OffsetX,
            PanY = Editor.Viewport.OffsetY,
            Probes = [.. Waveform.Traces.Select(t => t.Id.ToString())],
        },
        FilePath is null ? string.Empty : Path.GetFileNameWithoutExtension(FilePath));

    public void LoadProject(ProjectFile project, string? path)
    {
        Editor.LoadDocument(project.Schematic);
        Analysis.Settings = project.Analysis;
        Editor.Viewport.Set(project.View.Zoom, project.View.PanX, project.View.PanY);
        Waveform.Clear();
        FilePath = path;
        MarkSaved();
    }

    private void MarkSaved()
    {
        _savedVersion = Version;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    private void Navigate(Page page) => CurrentPage = page;

    [RelayCommand]
    private async Task NewAsync()
    {
        if (IsDirty && !await _messages.ConfirmAsync("開新檔案", "目前的電路尚未儲存，確定要捨棄嗎？"))
        {
            return;
        }

        LoadProject(ProjectFile.New(), null);
    }

    [RelayCommand]
    public async Task OpenAsync()
    {
        var path = await _files.OpenFileAsync(FileKind.Project);
        if (path is null)
        {
            return;
        }

        await OpenPathAsync(path);
    }

    public async Task<bool> OpenPathAsync(string path)
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _messages.ShowErrorAsync("無法開啟檔案", ex.Message);
            return false;
        }

        var result = ProjectSerializer.Deserialize(text);
        if (result.Project is null)
        {
            await _messages.ShowErrorAsync("無法開啟專案", result.MessageZh);
            return false;
        }

        LoadProject(result.Project, path);
        return true;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (FilePath is null)
        {
            await SaveAsAsync();
            return;
        }

        await WriteProjectAsync(FilePath);
    }

    [RelayCommand]
    public async Task SaveAsAsync()
    {
        var path = await _files.SaveFileAsync(FileKind.Project, "電路" + ProjectSerializer.FileExtension);
        if (path is not null)
        {
            await WriteProjectAsync(path);
        }
    }

    private async Task WriteProjectAsync(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            FilePath = path;
            await File.WriteAllTextAsync(path, ProjectSerializer.Serialize(Snapshot()), new UTF8Encoding(false));
            MarkSaved();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _messages.ShowErrorAsync("儲存失敗", ex.Message);
        }
    }

    [RelayCommand]
    public async Task ImportNetlistAsync()
    {
        var path = await _files.OpenFileAsync(FileKind.Netlist);
        if (path is null)
        {
            return;
        }

        var result = SpiceNetlistReader.Parse(await File.ReadAllTextAsync(path));
        if (!result.Success)
        {
            await _messages.ShowErrorAsync("網表有語法錯誤", string.Join("\n", result.Errors.Take(20)));
            return;
        }

        var deck = result.Deck!;
        if (deck.Circuit.Nodes.Count > Core.Validation.CircuitValidator.MaxNodes)
        {
            await _messages.ShowErrorAsync("電路太大", $"網表有 {deck.Circuit.Nodes.Count:N0} 個節點，超過編輯器上限 {Core.Validation.CircuitValidator.MaxNodes:N0} 個。");
            return;
        }

        var settings = Analysis.Settings;
        foreach (var a in deck.Analyses)
        {
            settings = a switch
            {
                OpCommand => settings with { Kind = AnalysisKind.OperatingPoint },
                DcCommand dc => settings with { Kind = AnalysisKind.DcSweep, DcSource = dc.SourceName, DcStart = R(dc.Start), DcStop = R(dc.Stop), DcStep = R(dc.Step) },
                TranCommand tr => settings with { Kind = AnalysisKind.Transient, TranStop = R(tr.Stop), TranMaxStep = tr.MaxStep is { } m ? R(m) : string.Empty },
                AcCommand ac => settings with { Kind = AnalysisKind.Ac, AcDecade = ac.Type == AcSweepType.Decade, AcStart = R(ac.Start), AcStop = R(ac.Stop), AcPoints = ac.Points.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                _ => settings,
            };
        }

        LoadProject(new ProjectFile(AutoLayout.FromCircuit(deck.Circuit), settings, new ViewSettings()), null);
        Editor.Viewport.Fit(Editor.Document.Elements.Select(HitTester.Bounds).Aggregate((x, y) => x.Union(y)), 1000, 700);
        CurrentPage = Page.Editor;

        static string R(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    public async Task ExportNetlistAsync()
    {
        var extraction = Editor.Extraction;
        var path = await _files.SaveFileAsync(FileKind.Netlist, "電路.cir");
        if (path is null)
        {
            return;
        }

        var deck = new SpiceDeck(extraction.Circuit, [ToCommand(Analysis.Settings)], Path.GetFileNameWithoutExtension(path));
        await File.WriteAllTextAsync(path, SpiceNetlistWriter.Write(deck), new UTF8Encoding(false));
    }

    public static AnalysisCommand ToCommand(AnalysisSettings s)
    {
        static double P(string t) => Core.Units.EngineeringNotation.TryParse(t, out var v, out _) ? v : 0;
        return s.Kind switch
        {
            AnalysisKind.OperatingPoint => new OpCommand(),
            AnalysisKind.DcSweep => new DcCommand(s.DcSource, P(s.DcStart), P(s.DcStop), P(s.DcStep)),
            AnalysisKind.Ac => new AcCommand(s.AcDecade ? AcSweepType.Decade : AcSweepType.Linear, (int)P(s.AcPoints), P(s.AcStart), P(s.AcStop)),
            _ => new TranCommand(P(s.TranStop) / 1000, P(s.TranStop), 0, string.IsNullOrWhiteSpace(s.TranMaxStep) ? null : P(s.TranMaxStep)),
        };
    }

    [RelayCommand]
    public async Task ExportSchematicSvgAsync()
    {
        var path = await _files.SaveFileAsync(FileKind.Svg, "電路圖.svg");
        if (path is not null)
        {
            await File.WriteAllTextAsync(path, SchematicSvgExporter.Export(Editor.Document, Editor.Extraction), new UTF8Encoding(false));
        }
    }

    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        var path = await _files.SaveFileAsync(FileKind.Csv, "波形.csv");
        if (path is null)
        {
            return;
        }

        await using var stream = File.Create(path);
        if (CurrentPage == Page.Bode && Bode.Data is { } ac)
        {
            CsvExporter.WriteAc(stream, ac, Bode.SelectedSignal is { } s ? [s] : ac.Signals.Keys);
        }
        else if (Waveform.Data is { } data)
        {
            var signals = Waveform.Traces.Count > 0 ? Waveform.Traces.Select(t => t.Id) : data.Signals.Keys;
            CsvExporter.Write(stream, data, signals);
        }
    }

    [RelayCommand]
    public async Task ExportWaveformSvgAsync()
    {
        if (!Waveform.HasData)
        {
            return;
        }

        var path = await _files.SaveFileAsync(FileKind.Svg, "波形.svg");
        if (path is not null)
        {
            await File.WriteAllTextAsync(path, WaveformSvgExporter.Export(Waveform), new UTF8Encoding(false));
        }
    }

    /// <summary>啟動時呼叫：處理當機回復並啟動自動儲存。</summary>
    public async Task StartupAsync()
    {
        if (AutoSave is null)
        {
            return;
        }

        if (AutoSave.CheckForRecovery() is { } recovery)
        {
            if (await _messages.ConfirmAsync("回復未儲存的工作", $"上次程式沒有正常關閉。要回復 {recovery.SavedAt.LocalDateTime:yyyy/MM/dd HH:mm:ss} 自動儲存的電路嗎？"))
            {
                LoadProject(recovery.Project, null);
                _savedVersion = default;
                OnPropertyChanged(nameof(IsDirty));
            }
            else
            {
                AutoSave.DiscardRecovery();
            }
        }

        AutoSave.Start(Snapshot, () => IsDirty);
    }

    /// <summary>視窗要關閉時詢問：有未儲存的變更時需要使用者確認。回傳是否可以關閉。</summary>
    public async Task<bool> ConfirmCloseAsync() =>
        !IsDirty || await _messages.ConfirmAsync("結束", "電路尚未儲存，確定要結束嗎？（未儲存的變更會遺失）");

    /// <summary>關閉前呼叫（正常關閉）。</summary>
    public void Shutdown() => AutoSave?.MarkCleanShutdown();

    private readonly record struct SchematicDocumentVersion(Schematic.Model.SchematicDocument? Document, AnalysisSettings? Settings);
}
