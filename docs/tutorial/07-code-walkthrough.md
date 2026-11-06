# 07 程式內容導讀

本章帶你實際讀一遍程式碼。先看建議的閱讀順序，再從程式進入點出發，沿著「執行一次暫態分析」走過每一層，最後逐一介紹各模組。

## 建議閱讀順序

| 順序 | 檔案 | 為什麼先讀它 |
|---|---|---|
| 1 | `src/CircuitBench.Core/Model/Components.cs`、`src/CircuitBench.Core/Model/Circuit.cs` | 整個系統的共同語言：電路就是一串不可變的 `Component` record |
| 2 | `src/CircuitBench.Core/Mna/MnaLayout.cs` | 未知數怎麼排：先節點電壓、後支路電流。後面所有索引都以它為準 |
| 3 | `src/CircuitBench.Core/Mna/Devices/Device.cs` | 元件的抽象介面與蓋印輔助結構，看懂這裡就看懂 MNA |
| 4 | `src/CircuitBench.Core/Mna/MnaSystem.cs` | 組裝與求解一次線性化系統 |
| 5 | `src/CircuitBench.Core/Analysis/Simulator.cs` → `DcSolver.cs` → `TransientEngine.cs` | 分析的入口與兩個主要求解器 |
| 6 | `src/CircuitBench.Schematic/Model/SchematicDocument.cs`、`src/CircuitBench.Schematic/Connectivity/ConnectivityExtractor.cs` | 圖面怎麼變成電路 |
| 7 | `src/CircuitBench.App.ViewModels/Editor/EditorViewModel.cs`、`src/CircuitBench.App.ViewModels/Analysis/AnalysisViewModel.cs` | UI 邏輯 |
| 8 | `src/CircuitBench.App/App.axaml.cs`、`src/CircuitBench.App/Controls/SchematicCanvas.cs` | 最外層：組裝與繪圖 |

`src/CircuitBench.Core/LinearAlgebra/SparseLu.cs` 雖然重要，但可以當成黑盒子先跳過，08 章會深入拆解。

## 程式啟動

`src/CircuitBench.App/Program.cs` 是標準的 Avalonia 進入點：`AppBuilder.Configure<App>().UsePlatformDetect()`，接著以 classic desktop lifetime 啟動。

真正的組裝在 `App.OnFrameworkInitializationCompleted()`：

```csharp
// src/CircuitBench.App/App.axaml.cs
public MainWindowViewModel CreateMainViewModel(Func<Window?> owner)
{
    var time = TimeProvider.System;                                   // ①
    var paths = new DefaultAppPaths();
    Messages = new AvaloniaMessageService(owner);                     // ②
    var files = new AvaloniaFileDialogService(() => owner(), paths);
    var autoSave = EnableAutoSave ? new AutoSaveService(time, paths) : null;  // ③
    return new MainWindowViewModel(files, Messages, new Simulator(time), autoSave);
}
```

① **整個程式只有這裡取得 `TimeProvider.System`**，之後一路往下傳：`Simulator` → `MnaSystem` → `SparseLu`。所以測試只要換掉這一個物件，就能控制所有計時。

② 對話框服務需要「擁有者視窗」，但 ViewModel 建立時視窗可能還沒準備好，所以傳入 `Func<Window?>`，延後到真正開對話框時才取用。

③ `EnableAutoSave` 是給 Headless 測試關掉自動儲存用的，避免測試寫入使用者的 `%LocalAppData%`。

`MainWindowViewModel` 的建構子再組出子 ViewModel：`EditorViewModel`、`AnalysisViewModel`、`WaveformViewModel` 等，並訂閱事件。例如分析完成時依分析種類切頁：

```csharp
// src/CircuitBench.App.ViewModels/MainWindowViewModel.cs
Analysis.Completed += (_, kind) => CurrentPage = kind switch { /* Transient → Waveform, Ac → Bode ... */ };
Analysis.Failed += (_, message) => _ = _messages.ShowErrorAsync("分析未執行", message);
```

## 走讀：一次暫態分析的生命週期

以「學生按下執行，跑 RC 電路的暫態」為例，依序經過下列五站。

### 第 1 站：ViewModel 先檢查，再交給背景

```csharp
// src/CircuitBench.App.ViewModels/Analysis/AnalysisViewModel.cs
public async Task RunAsync()
{
    if (!_editor.RunCheck())                                   // ①
    {
        Status = $"電路有 {_editor.Issues.Count} 個問題，請先修正：{first}";
        Failed?.Invoke(this, Status);
        return;
    }

    var circuit = _editor.Extraction.Circuit;
    _cts = new CancellationTokenSource();
    var progress = new Progress<AnalysisProgress>(p => Progress = p.Fraction);  // ②
    try
    {
        // ...
        var r = await _simulator.TransientAsync(circuit, new TransientRequest(stop, maxStep), options, progress, _cts.Token);
        Waveform.Load(r.Data);                                 // ③
        Diagnostics.Load("暫態分析", r.Statistics);
    }
    catch (OperationCanceledException) { Status = "已取消（未保留任何結果）"; }
    catch (SimulationException ex) { Status = $"分析失敗：{ex.Message}"; /* ... */ }
    finally { IsRunning = false; /* ... */ }
}
```

① 驗收要求「任何分析前檢查」，所以檢查是寫死在執行流程裡的第一步，不是可選的按鈕。

② `Progress<T>` 會把回呼排回建立它的同步內容（UI 執行緒），所以背景執行緒回報進度時，不需要自己 dispatch。

③ **只有成功才會呼叫 `Load`**。取消與失敗都走 `catch`，結果頁維持原樣。這就是「不留下半套結果」在 ViewModel 層的實作。對應的測試是 `CancellationLeavesPreviousResultsUntouched`。

### 第 2 站：圖面萃取成電路

`_editor.Extraction` 是一個延遲計算的屬性，`=> _extraction ??= ConnectivityExtractor.Extract(Document)`；文件改變時 `OnDocumentChanged()` 會把快取清成 null。萃取的演算法見 08 章「連線萃取」。

### 第 3 站：Simulator 建立 MNA 系統

```csharp
// src/CircuitBench.Core/Analysis/Simulator.cs
public Task<TransientResult> TransientAsync(/* ... */) =>
    Task.Run(() => Transient(circuit, request, options, progress, cancellation), cancellation);

public TransientResult Transient(/* ... */)
{
    var start = _time.GetTimestamp();
    var system = CreateSystem(circuit, options, LoadMode.Dc);
    var engine = new TransientEngine(system, request, options, new MonotonicProgress(progress, "暫態分析"), cancellation);
    var (data, iterations, accepted, rejected, method) = engine.Run();
    return new TransientResult(data, Stats(system, iterations, accepted, rejected, method, start));
}
```

非同步版本只是同步版本外面包一層 `Task.Run`。**同步版本是公開的**，所以 Core 的測試可以直接呼叫 `_sim.Transient(...)`，不需要 `await`，也不會碰到執行緒問題。這是讓數值程式容易測試的小技巧。

`MonotonicProgress` 包住使用者傳入的 `IProgress`，丟掉比上次小的值，並限制每 0.5% 才回報一次，避免 UI 被大量進度事件淹沒。

### 第 4 站：MnaSystem 的建構，結構只登記一次

```csharp
// src/CircuitBench.Core/Mna/MnaSystem.cs
Layout = new MnaLayout(circuit);
Devices = CreateDevices(circuit, Layout);

var builder = new PatternBuilder(Layout.Size);
for (var i = 0; i < Layout.NodeCount; i++)
{
    builder.Add(i, i); // gmin 步進需要每個節點的對角          ①
}
foreach (var d in Devices) d.Register(builder);
Pattern = builder.Build();
foreach (var d in Devices) d.Bind(Pattern);                       // ②
```

① 每個節點的對角都預先登記，即使沒有元件蓋印在那裡。否則 gmin 步進要在對角加值時，結構就會改變，符號分解也就不能重用了。

② `Bind` 讓每個元件查出自己要寫入的 CSC 陣列索引（槽位）並保存起來。之後每次組裝，蓋印都是直接 `m[_pp] += g`，沒有任何查表或雜湊。

### 第 5 站：TransientEngine 的主迴圈

主迴圈每一圈的骨架如下（省略細節，完整演算法見 08 章）：

```csharp
// src/CircuitBench.Core/Analysis/TransientEngine.cs
while (t < stop)
{
    cancellation.ThrowIfCancellationRequested();
    h = Math.Min(h, Math.Min(maxStep, _oscillation.StepCap(t)));    // ① 步長上限
    // 會跨過轉折點時截短，並把 tNew 直接設成轉折點
    ctx.Coefficients = /* BE、BDF2 或 TR 的係數 */;
    ctx.Time = landsOnBreakpoint ? Math.BitDecrement(tNew) : tNew;  // ② 左極限

    var xNew = solver.Newton(x, 0, options.MaxNewtonIterations);
    if (xNew is null) { rejected++; h = hActual / 8; continue; }    // ③
    if (ErrorRatio(...) > 1) { rejected++; /* 縮小 h */ continue; }
    if (InterpolationErrorRatio(...) > 1) { rejected++; /* 縮小 h */ continue; }
    // TR 振盪檢查（必要時改用 BDF2 的結果）

    foreach (var d in system.Devices) d.AcceptTransient(xNew, ctx); // ④
    _times.Add(t); recorder.Record(x);
}
```

① 步長上限取三者最小：使用者的最大步長（已含 SIN 週期的 1/50）、自由振盪的估計週期 / 100、下一個轉折點。

② 落在轉折點上的那一步，以 `Math.BitDecrement(tNew)`（比 tNew 小一個 ULP 的浮點數）求電源值。開關在 t = 1 ms 切換時，t = 1 ms 這一點看到的是切換前的狀態，下一步才是切換後。這讓「轉折點上的值」有明確定義。

③ 三種拒絕（Newton 不收斂、LTE 太大、折線誤差太大）都用 `continue` 回到迴圈開頭，**不會**呼叫 `AcceptTransient`，也不寫入結果。驗收標準 11「被拒絕的步驟不留在結果中」就是靠這個結構保證的。

④ 只有被接受的步驟才更新元件的歷史狀態（電容的前一個電壓與電流）。

## 模組導讀

### Core／Model：不可變的電路模型

- **職責**：描述電路的連接關係與參數，不含任何求解行為。
- **關鍵型別**：`Component` 的各個子 record（`Resistor`、`VoltageSource`、`Diode`、`TimedSwitch`……）、`Circuit.Create()`、`SourceSpec`、`Waveform`。
- **值得學的寫法**：`PulseWaveform.Corner(k, index)` 是唯一計算轉折時刻的地方，`Evaluate()` 與 `Breakpoints()` 都呼叫它：

```csharp
// src/CircuitBench.Core/Model/Waveforms.cs
/// 轉折時刻一律由 Corner 計算，讓 Evaluate 與 Breakpoints 使用完全相同的浮點數值。
public double Corner(long k, int index)
{
    var start = IsPeriodic ? Td + k * Per : Td;
    return index switch { 0 => start, 1 => start + Tr, 2 => start + Tr + Pw, 3 => start + Tr + Pw + Tf, /* ... */ };
}
```

如果兩邊各自計算 `Td + k*Per + Tr`，運算順序不同就可能差一個 ULP。暫態引擎把時間設成轉折點的值時，`Evaluate` 判斷「是否已過轉折」就可能出錯。**同一個浮點數只算一次**，是處理時間邊界的關鍵原則。

### Core／Mna：蓋印與伴隨模型

- **職責**：把每個元件轉成矩陣與右側向量的貢獻。
- **關鍵型別**：
  - `MnaLayout`：未知數編號，`DescribeUnknown()` 產生「節點 N3 的電壓」這種中文描述。
  - `Device`：元件的抽象基底。
  - `TwoTerminalSlots`、`BranchSlots`：蓋印輔助結構。
  - `RealLoadContext`、`ComplexLoadContext`：組裝時傳給元件的資訊。
  - `IntegrationCoefficients`：積分方法的係數。

`TwoTerminalSlots` 把兩端元件最常見的四格蓋印與接地處理封裝起來：

```csharp
// src/CircuitBench.Core/Mna/Devices/Device.cs
public readonly void Stamp<T>(T[] m, T g, double[]? scale = null)
    where T : INumberBase<T>
{
    if (_pp >= 0) m[_pp] += g;      // 接地端的槽位是 -1，自動略過
    if (_qq >= 0) m[_qq] += g;
    if (_pq >= 0) m[_pq] -= g;
    if (_qp >= 0) m[_qp] -= g;
    if (scale is not null) { /* 記錄抵消前的量級，供奇異判定 */ }
}
```

- **泛型數學 `INumberBase<T>`**：同一份蓋印程式碼同時服務實數（直流、暫態）與複數（交流），電阻的 `LoadAc` 只是把 `g` 換成 `new Complex(g, 0)`。
- **`scale` 參數**：審查回饋後加入，見 08 章「奇異偵測」。

電容的伴隨模型完全由係數決定，不知道自己正在用哪種積分方法：

```csharp
// src/CircuitBench.Core/Mna/Devices/LinearDevices.cs（CapacitorDevice）
private (double G, double Ieq) Companion(RealLoadContext ctx)
{
    var k = ctx.Coefficients;   // x'(n+1) ≈ A0·x(n+1) + A1·x(n) + A2·x(n−1) + B·x'(n)
    return (capacitance * k.A0, capacitance * (k.A1 * _vPrev + k.A2 * _vPrev2) + k.B * _iPrev);
}
```

i = C·v′，代入通式後，v(n+1) 的係數 C·A0 就是等效電導，其餘項就是等效電流源。BE、TR、BDF2 只是 `IntegrationCoefficients` 的三個工廠方法，**新增積分方法不需要改動任何元件**。這是策略模式的數值版本。

### Core／Analysis／DcSolver：Newton 與收斂輔助

- **職責**：解非線性直流方程；失敗時依序嘗試 gmin 步進、電源步進。
- **關鍵方法**：
  - `Newton(initial, extraGmin, maxIterations)`：失敗時回傳 null，不丟例外。
  - `SolveOperatingPoint()`：串接各種收斂策略。
  - `Polish()`：收斂後再多做一次迭代。
- **值得學的寫法**：`Newton()` 以回傳 null 表示「這個策略沒成功」，外層用 `is not null` 串接策略。這比用例外控制流程清楚，也比較快（失敗是常態，不是例外）。
- **收斂條件的細節**：除了變化量小於容許值，還要求 `!system.Devices.Any(d => d.WasLimited)`。二極體的 pnjlim 限制了這一步的接面電壓時，看起來變化很小，其實是被截斷的，不能宣告收斂。

### Core／LinearAlgebra：稀疏 LU

- **職責**：分解與求解 Ax = b；結構不變時只重做數值分解。
- **關鍵方法**：
  - `Factor()`：完整分解，含排序、符號分析與選主元。
  - `Refactor()`：沿用主元順序，只重算數值。
  - `Solve()`：前代與回代。
  - `ScaledConditionLowerBound()`：求解後估計條件數下界。
- 詳見 08 章。這裡只提醒一點：`MnaSystem.AssembleAndSolve()` **永遠呼叫 `Refactor`**，第一次呼叫時它發現還沒分解過，就自己轉呼叫 `Factor`。呼叫端不需要記得「第一次要 Factor」。

### Schematic：圖面與連線

- **職責**：可編輯的圖面模型、從圖面到電路的萃取、復原重做、存檔。
- **關鍵型別**：
  - `SchematicDocument`：不可變的圖面文件。
  - `ComponentSymbol`：圖面上的元件。
  - `ComponentCatalog`：各元件的接腳位置、預設參數、名稱前綴。
  - `ConnectivityExtractor`：圖面 → 電路。
  - `UndoHistory<T>`：快照式復原重做。
  - `ProjectSerializer`：JSON 存讀檔。
- **值得學的寫法**：`UndoHistory<T>` 是完全泛型的，只要求 `T` 不可變。它不知道什麼是電路圖，所以能用 `UndoHistory<int>` 測容量與丟棄最舊步驟的邏輯（見 `AtLeastHundredLevelsAndOldestDroppedBeyondCapacity`）。

```csharp
// src/CircuitBench.Schematic/Editing/UndoHistory.cs
public void Apply(T next)
{
    _undo.AddLast(Current);
    if (_undo.Count > Capacity) _undo.RemoveFirst();   // LinkedList：兩端都能 O(1) 移除
    _redo.Clear();
    Current = next;
    Changed?.Invoke(this, EventArgs.Empty);
}
```

復原堆疊用 `LinkedList` 而不是 `Stack`，因為超過容量時要從**底部**丟掉最舊的一筆，`Stack` 做不到。

### App.ViewModels／Editor：指標事件狀態機

- **職責**：工具切換、點選、框選、拖曳、畫導線、放置元件、旋轉、鏡射、複製貼上、檢查標示。
- **關鍵方法**：`PointerPressed/PointerMoved/PointerReleased(WorldPoint, PointerModifiers)`、`RunCheck()`、`RenameComponent()`、`RenameLabel()`。
- **值得學的寫法**：
  - **一個使用者動作 = 一個復原步驟**：拖曳過程中只更新 `DragOffset` 預覽，放開時才 `History.Apply(Document.Move(...))` 一次。畫 L 形導線也是兩段一起 `AddRange` 成一步。
  - **ViewModel 接收世界座標，不是螢幕座標**：座標轉換由 `Viewport` 負責，畫布只呼叫 `Viewport.ToWorld()`。測試直接傳 `new WorldPoint(3, 2)`，不必關心縮放比例。
  - `RenameComponent()` 回傳 `string?`：成功時為 null，失敗時是中文原因（例如名稱重複）。這比丟例外更適合「使用者輸入驗證」的情境。
- **可以改進的地方**：`EditorViewModel.cs` 約 570 行，指標事件的三個方法各自以 `switch (ActiveTool)` 分派。工具再增加時，可以考慮改成每種工具一個類別（State Pattern），每個類別各自處理 Pressed、Moved、Released。

### App.ViewModels／Results：波形

- **職責**：曲線清單、多 Y 軸、縮放平移、游標與量測。
- **值得學的寫法**：`ColumnsInto()` 把抽樣結果寫進每條曲線自己的緩衝區，寬度不變就重用：

```csharp
// src/CircuitBench.App.ViewModels/Results/WaveformViewModel.cs
public ReadOnlySpan<MinMaxColumn> ColumnsInto(Trace trace, int pixelWidth)
{
    if (trace.ColumnBuffer.Length != pixelWidth)
        trace.ColumnBuffer = new MinMaxColumn[pixelWidth];
    trace.Pyramid.Decimate(XMin, XMax, trace.ColumnBuffer);
    return trace.ColumnBuffer;
}
```

這是 Skia 重繪量測後加入的最佳化：原本每次重繪都配置新陣列，在 100 萬點的壓力測試下會觸發 GC，造成最差重繪時間超標（見 11 章）。

### App：自訂繪製控制項

三個畫布都繼承 `Control` 並覆寫 `Render(DrawingContext)`：`SchematicCanvas`、`WaveformCanvas`、`BodeCanvas`。

- **職責**：繪圖，以及把指標與鍵盤事件轉給 ViewModel。
- `SchematicCanvas.OnPointerPressed()` 只處理「中鍵或右鍵 → 平移」這個純檢視操作，其他一律轉呼叫 `Editor.PointerPressed(ToWorld(...))`。
- `WaveformCanvas.Render()` 每個像素欄畫一條 min–max 垂直線，用 `StreamGeometry` 串成一條折線，並以 `PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased })` 關閉反鋸齒：

```csharp
// src/CircuitBench.App/Controls/WaveformCanvas.cs
// 每欄畫一條 min–max 垂直線；先連到離上一點較近的一端，避免邊緣處出現假尖刺
var x = r.Left + i + 0.5;
var yMax = Sy(cols[i].Max);
var yMin = Sy(cols[i].Min);
var (first, second) = Math.Abs(yMax - lastY) <= Math.Abs(yMin - lastY) ? (yMax, yMin) : (yMin, yMax);
g.LineTo(new Point(x, first));
g.LineTo(new Point(x, second));
lastY = second;
```

「先連到較近的一端」是 Headless 截圖檢查時發現的修正。原本固定先畫 max 再畫 min，波形快速上升時，相鄰兩欄之間會多出一條往回拉的線，看起來像假尖刺。

- **可以改進的地方**：`WaveformCanvas` 直接使用 `TimeProvider.System` 量測重繪耗時，沒有像其他地方一樣由外部注入。這不影響功能，但如果要讓重繪耗時的顯示也能測試，就需要改成注入。
