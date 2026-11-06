namespace CircuitBench.Schematic.Editing;

/// <summary>
/// 以快照實作的復原/重做。狀態必須是不可變的（例如 <see cref="Model.SchematicDocument"/>）。
/// 超過容量時丟棄最舊的步驟。
/// </summary>
public sealed class UndoHistory<T>
{
    private readonly LinkedList<T> _undo = new();
    private readonly Stack<T> _redo = new();

    public UndoHistory(T initial, int capacity = 200)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Current = initial;
        Capacity = capacity;
    }

    public T Current { get; private set; }

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public event EventHandler? Changed;

    /// <summary>套用新狀態（成為可復原的一步），並清除重做堆疊。</summary>
    public void Apply(T next)
    {
        _undo.AddLast(Current);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (_undo.Last is not { } last)
        {
            return false;
        }

        _undo.RemoveLast();
        _redo.Push(Current);
        Current = last.Value;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.AddLast(Current);
        Current = _redo.Pop();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>重設（例如開啟新檔），清除所有歷史。</summary>
    public void Reset(T state)
    {
        _undo.Clear();
        _redo.Clear();
        Current = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
