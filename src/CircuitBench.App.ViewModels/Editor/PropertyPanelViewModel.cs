using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CircuitBench.Schematic.Model;

namespace CircuitBench.App.ViewModels.Editor;

/// <summary>屬性面板中的一個欄位。設定 Value 會立即寫回文件（成為一個復原步驟）。</summary>
public sealed partial class ParameterField(string key, string label, string hint, string value, Func<string, string?> commit) : ObservableObject
{
    private string _value = value;

    public string Key { get; } = key;

    public string Label { get; } = label;

    public string Hint { get; } = hint;

    [ObservableProperty]
    private string? _error;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            var error = commit(value);
            Error = error;
            if (error is null)
            {
                _value = value;
            }

            OnPropertyChanged();
        }
    }
}

/// <summary>選取單一元件或標籤時，顯示可編輯的名稱與參數。</summary>
public sealed partial class PropertyPanelViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;

    public PropertyPanelViewModel(EditorViewModel editor)
    {
        _editor = editor;
        _editor.PropertyChanged += OnEditorChanged;
        Rebuild();
    }

    public ObservableCollection<ParameterField> Fields { get; } = [];

    [ObservableProperty]
    private string _header = "（未選取）";

    public bool HasSelection => Fields.Count > 0;

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.SelectedComponent) or nameof(EditorViewModel.SelectedLabel))
        {
            Rebuild();
        }
    }

    private object? _shownTarget;

    private void Rebuild()
    {
        object? target = (object?)_editor.SelectedComponent ?? _editor.SelectedLabel;

        // 選取的元素與欄位內容都沒變時不重建，避免正在編輯的 TextBox 失去焦點
        if (Equals(target, _shownTarget))
        {
            return;
        }

        _shownTarget = target;
        Fields.Clear();
        switch (target)
        {
            case ComponentSymbol c:
            {
                var def = ComponentCatalog.Get(c.Kind);
                Header = $"{def.DisplayNameZh} {c.Name}";
                var id = c.Id;
                Fields.Add(new ParameterField("name", "名稱", $"以 {def.Prefix} 開頭", c.Name, v => _editor.RenameComponent(id, v)));
                foreach (var p in def.ParameterDefinitions)
                {
                    var key = p.Key;
                    Fields.Add(new ParameterField(key, p.LabelZh, p.Hint, c.Parameter(key), v =>
                    {
                        _editor.SetParameter(id, key, v);
                        return null;
                    }));
                }

                break;
            }

            case NetLabel l:
            {
                Header = "網路標籤";
                var id = l.Id;
                Fields.Add(new ParameterField("net", "網路名稱", "同名標籤視為相連；0 代表接地", l.NetName, v => _editor.RenameLabel(id, v)));
                break;
            }

            default:
                Header = "（未選取）";
                break;
        }

        OnPropertyChanged(nameof(HasSelection));
    }
}
