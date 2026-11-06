# 09 測試

數值程式最難的地方不是寫出來，而是**證明它是對的**。本專案用四種互補的方法驗證：

1. 解析解。
2. 獨立的參考求解器。
3. 性質測試（隨機輸入）。
4. 回歸測試（12 個範例）。

## 測試策略

| 層級 | 專案 | 數量 | 測什麼 |
|---|---|---|---|
| 單元與性質 | `tests/CircuitBench.Core.Tests` | 216 | 工程記號、波形、稀疏 LU、MNA、非線性直流、直流掃描、暫態、交流、取消、檢查、抽樣、量測、波德圖、CSV、SPICE |
| 單元與性質 | `tests/CircuitBench.Schematic.Tests` | 60 | 連線萃取、旋轉鏡射、復原重做、點選、自動排列、SVG、專案檔 |
| ViewModel | `tests/CircuitBench.App.ViewModels.Tests` | 63 | 編輯器互動、分析執行與取消、波形與波德圖、診斷、自動儲存、檔案操作、12 個範例 × 2 |
| UI（Headless） | `tests/CircuitBench.App.Headless.Tests` | 5 | 開啟主視窗、工具列與畫布點擊、鍵盤旋轉、屬性面板、執行分析後切頁繪製波形 |
| 效能 | `tests/CircuitBench.Performance.Tests` 與 Headless 的 `RenderPerformanceTests` | 5 | 驗收標準 22 的四項，加上 Skia 完整重繪 |

數量以 README「測試」一節為準，效能測試以 `[Trait("Category", "Performance")]` 標記，預設不執行。

### 兩個測試輔助專案

| 專案 | 內容 |
|---|---|
| `tests/CircuitBench.ReferenceSolver` | 稠密 MNA 加高斯消去（部分主元）、固定步長 BE／TR、複數 AC。**零專案參考**，有自己的 `RefCircuit` 型別 |
| `tests/CircuitBench.TestKit` | `PropertyTest` harness、`RandomCircuits`、`RandomMatrices`、`CoreToReference` 轉接、`LambertW`、`ManualTimeProvider`、`SchematicBuilder`、`WaveformMath` |

`TestKit` 的 csproj 檔設定 `IsTestProject=false`，否則 CI 的 `dotnet test` 會把它當成測試專案而失敗（見 11 章）。

## 執行指令

```powershell
# 全部功能測試（不含效能）
dotnet test -c Release --filter "Category!=Performance"

# 只跑某個專案或某個測試
dotnet test tests/CircuitBench.Core.Tests -c Release --filter "FullyQualifiedName~TransientTests"

# 效能測試（建議 Release、電源設為高效能）
dotnet test tests/CircuitBench.Performance.Tests -c Release --filter "Category=Performance" --logger "console;verbosity=detailed"
dotnet test tests/CircuitBench.App.Headless.Tests -c Release --filter "Category=Performance" --logger "console;verbosity=detailed"

# 重現性質測試的失敗案例（種子由失敗訊息提供）
$env:CIRCUITBENCH_SEED=20261106; dotnet test tests/CircuitBench.Core.Tests --filter "FullyQualifiedName~Property_"

# 重新產生 README 截圖與範例檔
$env:CIRCUITBENCH_SCREENSHOTS="docs/images"; dotnet test tests/CircuitBench.App.Headless.Tests
$env:CIRCUITBENCH_WRITE_SAMPLES=1; dotnet test tests/CircuitBench.App.ViewModels.Tests --filter "FullyQualifiedName~SampleTests"
```

## 參考求解器：為什麼要「互相獨立」

如果用正式求解器自己算出的結果當預期值，程式錯了，測試也會跟著錯。參考求解器要能當「第二意見」，就必須：

- **不共用任何程式碼**：沒有 ProjectReference，連電路型別都是自己的。
- **用最笨但最直接的方法**：
  - 每次都重組稠密矩陣。
  - 用高斯消去求解。
  - 暫態只用固定步長。
  - 可以很慢，但很難寫錯。
- **本身先被驗證**：`tests/CircuitBench.Core.Tests/Reference/ReferenceSolverSanityTests.cs` 用分壓器、RC 解析解、LC 能量守恆、Lambert W 驗證參考求解器，通過之後才拿它驗證正式程式。

連接兩者的 `CoreToReference` 只做欄位對應。正式電路的 `Resistor(Name, A, B, R)` 轉成 `RefElement` 的對應欄位，不含任何判斷邏輯。

## 性質測試 harness

```csharp
// tests/CircuitBench.TestKit/Property/PropertyTest.cs
public static void ForAll(int iterations, Action<Random, int> property, int baseSeed = DefaultBaseSeed)
{
    var overrideSeed = Environment.GetEnvironmentVariable("CIRCUITBENCH_SEED");
    if (int.TryParse(overrideSeed, out var single)) { Run(single, 0, property); return; }   // ①

    for (var i = 0; i < iterations; i++)
    {
        var seed = unchecked(baseSeed + i * 7919);                                           // ②
        Run(seed, i, property);
    }
}
// Run() 捕捉例外，重新包成帶有 "CIRCUITBENCH_SEED={seed} 可重現" 的訊息               ③
```

① 設定環境變數就只跑那一組，除錯時不用等其他幾百組。

② 每組的種子由固定基底推出，乘上質數 7919 讓相鄰種子差距大一點。每次執行的輸入都相同，所以 CI 的結果可以重現。

③ 需求規格要求「失敗時印出可重現的種子」。這個 harness 約 40 行，比引入 FsCheck 簡單，訊息格式也完全可控。

全專案共有 17 組性質測試（📄 `docs/TEST-REPORT.md` 第 1 節），包括：

- 稀疏 LU 的實數與複數版本對照稠密解。
- `Refactor` 與重新分解的結果相同。
- 300 組隨機線性電路的直流、200 組的交流，與參考求解器比對。
- 暫態步長控制的不變量。
- 抽樣等於暴力計算。
- 非均勻時間點上的積分。
- 任意時間點取消。
- 復原 120 層。
- SPICE、自動排列、專案檔的往返。
- 秩虧矩陣偵測與良態矩陣不誤報。

## 代表性測試走讀

### 走讀一：每一步復原，圖面與網表都回到原狀

```csharp
// tests/CircuitBench.Schematic.Tests/EditingTests.cs
PropertyTest.ForAll(10, (rng, _) =>
{
    var history = new UndoHistory<SchematicDocument>(SchematicDocument.Empty, capacity: 150);
    var snapshots = new List<SchematicDocument> { history.Current };
    var netlists = new List<string> { NetlistOf(history.Current) };        // ①
    for (var step = 0; step < 120; step++)
    {
        history.Apply(RandomEdit(rng, history.Current));                   // ②
        snapshots.Add(history.Current);
        netlists.Add(NetlistOf(history.Current));
    }

    for (var k = snapshots.Count - 2; k >= 0; k--)
    {
        Assert.True(history.Undo());
        Assert.True(history.Current.ContentEquals(snapshots[k]), $"復原到第 {k} 步時圖面不同");
        Assert.Equal(netlists[k], NetlistOf(history.Current));             // ③
    }
    // ...再全部重做回來
});
```

① **每一步都記下網表字串**，不只記圖面。驗收標準 19 要求「圖面與萃取出的網表都回到該步之前的狀態」，兩者都要斷言。

② `RandomEdit` 隨機選擇新增、刪除、移動、旋轉、鏡射等操作。120 步超過驗收要求的 100 層。

③ 網表比對用字串（SPICE 輸出），失敗時 xUnit 會直接印出兩份網表的差異，比比對物件好讀。

### 走讀二：審查者的負電阻網表

```csharp
// tests/CircuitBench.Core.Tests/Analysis/OperatingPointTests.cs
[Fact]
public void NegativeResistanceCancellingInsideOneElementIsReported()
{
    // 審查者回報：G1 以負轉導剛好抵消 R1 的 1/3k 電導，抵消發生在同一個矩陣元素內，
    // 舊版直接回傳 V(n1) ≈ 3e15 V 而沒有任何錯誤。
    var deck = CircuitBench.Core.IO.SpiceNetlistReader.Parse("""
        I1 0 n1 DC 1m
        R1 n1 0 3k
        G1 n1 0 n1 0 -0.000333333333333333
        .op
        """).Deck!;
    var ex = Assert.Throws<SimulationException>(() => _sim.OperatingPoint(deck.Circuit, SimulationOptions.Default));
    Assert.Contains("奇異", ex.Message);
    Assert.Contains("節點 n1", ex.Message);
    Assert.Contains("G1", ex.Message);
    Assert.Contains("負電阻", ex.Message);
}
```

值得學的地方：

- **把回報的重現案例原樣放進測試**：用 SPICE 字串而不是手動建 `Circuit`，與審查者的輸入一字不差。註解記下「為什麼有這個測試」。
- **斷言錯誤訊息的內容**：不只檢查「有丟例外」，還檢查訊息是否指出節點與元件。對這個專案來說，中文錯誤訊息本身就是需求。
- **先寫紅燈**：這個測試先寫、確認在舊版失敗，才修改 `SparseLu`。

同一組測試還有兩個「反向」的測試，避免修過頭：

- `LegitimateNegativeResistanceIsStillSolved`：只抵消一半的負電阻要正常求解。
- `HugeButCorrectVoltageThroughOffSwitchIsNotFlagged`：電流灌進 1 TΩ 節點得到 1e6 V，這個解是正確的，不能被誤判為奇異。

## 測試時間的技巧

需求規格要求所有「現在時間」都透過 `TimeProvider`。測試用自製的 `ManualTimeProvider`（`tests/CircuitBench.TestKit/ManualTimeProvider.cs`），可以：

- 手動推進時間，觸發 `CreateTimer()` 建立的計時器。自動儲存的 60 秒計時器在測試中是瞬間觸發。
- 控制 `GetTimestamp()`，讓分解耗時等統計值可預測。

`AutoSaveTests` 用它模擬：啟動 → 編輯 → 推進 60 秒 → 確認寫檔 → 不呼叫 `MarkCleanShutdown`（模擬當機）→ 重新建立服務 → 確認 `CheckForRecovery()` 回傳回復資訊。

## Headless 測試

`tests/CircuitBench.App.Headless.Tests/HeadlessTests.cs` 以 `[AvaloniaFact]` 在不開視窗的情況下建立真正的 `MainWindow`，並用 Skia 實際繪製：

| 測試 | 驗證 |
|---|---|
| `MainWindowOpensWithChineseTitleAndFourPages` | 主視窗標題與四個分頁 |
| `ToolbarAndCanvasClickPlaceComponentAndRunCheck` | 點工具列按鈕、點畫布放置元件、執行檢查 |
| `RunningTransientSwitchesToWaveformPageAndRendersPlot` | 執行暫態後切到波形頁並畫出曲線 |
| `KeyboardRotateOnCanvasRotatesSelection` | 在畫布按 R 旋轉 |
| `PropertyPanelEditsSelectedComponent` | 屬性面板修改參數 |

Headless 測試使用 `NullDialogs` 取代對話框，並關閉自動儲存（`App.EnableAutoSave = false`）。

## 覆蓋範圍與缺口

**有測到的**：26 項驗收標準都有對應的自動化測試，對照表見 `docs/TEST-REPORT.md` 第 4 節。

**沒有測到的**（照實列出）：

- **實機 GUI 的人工操作**：連續拖曳的手感、檔案對話框、多螢幕 DPI。目前只有發佈檔的啟動冒煙測試（📄 `docs/TEST-REPORT.md` 5.5）。
- **實機 GPU 的重繪時間**：Headless 是 CPU 光柵化，數據與實機不同。
- **自然需要 gmin／電源步進的電路**：目前以壓低迭代上限的方式強迫走到步進路徑。
- **多台機器的效能比較**：只在一台 i7-13700 上量測。

## 建議補強的測試

以下為**建議**，目前不存在：

1. **開檔前的未儲存確認**：`MainWindowViewModel.OpenAsync()` 目前沒有確認（見 06 章），補上功能時應先寫 ViewModel 測試。
2. **自然困難的收斂案例**：例如多個二極體串並聯、初值離解很遠的電路，在預設迭代上限下觀察是否需要步進。
3. **效能回歸的趨勢紀錄**：CI 的效能步驟目前只供參考。可以把每次的數據存成 artifact，觀察趨勢，而不是用固定門檻判斷。
4. **萃取的規模測試**：連線萃取是 O(端子數 × 導線數)，可以加一個上千條導線的測試，確認不會成為瓶頸。
