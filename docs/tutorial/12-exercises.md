# 12 延伸練習與自我檢核

## 理解檢核

1. 為什麼 `MnaSystem` 建構時，要替**每個節點**的對角都登記非零位置，即使該位置沒有元件蓋印？
   <details><summary>答案</summary>
   gmin 步進要在每個節點的對角加上電導。如果這些位置沒有預先登記，加值時結構就會改變，稀疏 LU 的符號分解也就不能重用。見 07 章第 4 站與 `src/CircuitBench.Core/Mna/MnaSystem.cs`。
   </details>

2. 電容的伴隨模型為什麼不需要知道目前用的是 BE、TR 還是 BDF2？
   <details><summary>答案</summary>
   積分方法統一寫成 x′(n+1) ≈ A0·x(n+1) + A1·x(n) + A2·x(n−1) + B·x′(n)，由 `IntegrationCoefficients` 提供係數。電容只依係數算出等效電導 C·A0 與等效電流源。見 07 章「Core／Mna」。
   </details>

3. 暫態分析落在轉折點時，為什麼用 `tNew = bp`，而不是 `t + (bp − t)`？又為什麼這一步要用 `Math.BitDecrement(tNew)` 求電源值？
   <details><summary>答案</summary>
   浮點數運算 `t + (bp − t)` 不保證等於 `bp`，直接賦值才能讓時間誤差為 0。`BitDecrement` 取比 tNew 小一個 ULP 的時間，代表「切換前」的左極限，讓轉折點上的值有明確定義。見 08 章 7.5。
   </details>

4. 審查者的負電阻網表（I1、R1 3k、G1 −1/3k）為什麼能通過舊版的奇異判定？只靠求解後的條件數檢查夠不夠？
   <details><summary>答案</summary>
   抵消發生在同一個矩陣元素內，舊判定以抵消後的殘值當尺度，判定永遠不成立。條件數檢查是第二道防線，用來抓累積捨入讓主元剛好落在門檻上方的情況。兩道都需要：第一道在分解時就擋下明確的抵消，也比較早給出精確的未知數位置。見 08 章第 5 節。
   </details>

5. `UndoHistory<T>` 的復原堆疊為什麼用 `LinkedList` 而不是 `Stack`？
   <details><summary>答案</summary>
   超過容量時要從底部丟掉最舊的一步，`Stack` 只能從頂端操作。見 07 章「Schematic」。
   </details>

6. 為什麼 `DcSolver.Newton()` 判定收斂時，還要檢查 `WasLimited`？
   <details><summary>答案</summary>
   二極體被 pnjlim 限制時，這一步的變化量是被截斷過的，看起來很小，其實離解還很遠。見 07 章「Core／Analysis／DcSolver」與 08 章第 6 節。
   </details>

7. 參考求解器為什麼不能參考 Core？只要演算法不同，共用 `Circuit` 型別有什麼風險？
   <details><summary>答案</summary>
   共用型別時，型別本身的錯誤（例如電流方向定義、節點正規化）會讓兩邊一起錯，測試就看不出問題。參考求解器有自己的 `RefCircuit`，轉接層 `CoreToReference` 只做欄位對應。見 09 章。
   </details>

8. 純電阻的半波整流電路，為什麼 LTE 控制完全不起作用？專案用什麼機制補上？
   <details><summary>答案</summary>
   LTE 以狀態量（電容電壓、電感電流）估計，純電阻電路沒有狀態量，LTE 恆為 0。專案用「SIN 每週期至少 50 點」與「折線內插誤差控制」補上。見 08 章 7.4。
   </details>

## 動手練習

### 練習 1：開檔前確認未儲存的變更

`MainWindowViewModel.NewAsync()` 會詢問是否捨棄未儲存的變更，但 `OpenAsync()` 不會。

- **任務**：補上確認，並先寫 ViewModel 測試（紅燈）。
- **提示**：
  - 參考 `NewAsync()` 的寫法。
  - 測試可以用 `NullDialogs` 的 `ConfirmResult` 控制使用者的回答，用 `NextPaths` 指定開啟的路徑。
  - 想一想：確認應該放在選擇檔案**之前**還是**之後**？兩種順序對使用者有什麼不同？

### 練習 2：新增一個「電流控制電流源」（F 元件）

- **任務**：讓 SPICE 網表支援 `F1 out+ out- Vsense gain`。
- **提示**：需要改動的地方大致依資料流排列：
  1. `src/CircuitBench.Core/Model/Components.cs`：新增 record。
  2. `src/CircuitBench.Core/Mna/Devices/LinearDevices.cs`：新增 Device。
     - 控制量是 `Vsense` 的支路電流，所以蓋印在「輸出節點列 × Vsense 的支路欄」。
     - 記得在 `Register` 登記這兩個位置。
  3. `src/CircuitBench.Core/Mna/MnaSystem.cs` 的 `CreateDevices()`。
  4. `src/CircuitBench.Core/IO/SpiceNetlistReader.cs` 與 `src/CircuitBench.Core/IO/SpiceNetlistWriter.cs`。
  5. `src/CircuitBench.Core/Validation/CircuitValidator.cs`：F 的輸出端算不算直流路徑？
  6. 測試：參考求解器也要支援 F，然後性質測試 `Property_RandomLinearCircuitsMatchReferenceSolver` 才能涵蓋它。
- 想一想：如果 `Vsense` 不存在，應該在哪一層報錯？

### 練習 3：讓連線萃取支援上千條導線

- **任務**：`ConnectivityExtractor` 的「端子落在導線中段」檢查是 O(端子數 × 導線數)。先寫一個 2,000 條導線的效能測試量出時間，再改善。
- **提示**：
  - 導線只有水平與垂直兩種。
  - 可以把水平導線依 y 座標分組、垂直導線依 x 座標分組，查詢時只看同一列或同一行的導線。
  - 改完後，`ConnectivityTests` 的 15 個案例都必須通過。

### 練習 4：在診斷頁顯示 Refactor 退回完整分解的次數

- **任務**：`LuStatistics.RefactorFallbacks` 已經有統計值，但 `SolverStatistics` 沒有帶出來。
- **提示**：
  - `src/CircuitBench.Core/Analysis/AnalysisTypes.cs` 的 `SolverStatistics` 是 positional record，新增欄位會影響所有建構它的地方。
  - `src/CircuitBench.App.ViewModels/Results/DiagnosticsViewModel.cs` 負責顯示。
  - 想一想：Newton 迭代時二極體電導劇烈變化，退回次數會不會很高？這個數字對學生有沒有意義，還是只對開發者有意義？

## 延伸學習

| 主題 | 本專案的位置 | 搜尋關鍵字 |
|---|---|---|
| 修正節點分析 | 08 章第 3 節 | `modified nodal analysis stamps`、`SPICE companion model` |
| 稀疏 LU 與排序 | 08 章第 4 節 | `Gilbert Peierls left-looking LU`、`approximate minimum degree ordering`、`KLU circuit simulation` |
| 剛性方程與 L-stable | 08 章第 7 節 | `trapezoidal rule ringing stiff`、`BDF2 variable step`、`TR-BDF2` |
| 局部截斷誤差步長控制 | 08 章 7.3 | `local truncation error step size control divided differences` |
| Newton 收斂輔助 | 08 章第 6 節 | `pnjlim SPICE`、`gmin stepping source stepping homotopy` |
| 條件數估計 | 08 章第 5 節 | `condition number estimation LU`、`Hager Higham estimator` |
| 波形抽樣 | 08 章第 8 節 | `min max decimation waveform rendering`、`LTTB downsampling` |
| 性質測試 | 09 章 | `property-based testing shrinking`、`FsCheck` |
| 不可變資料與復原 | 03 章 | `persistent data structures undo`、`ImmutableList structural sharing` |
| Avalonia Headless | 09 章 | `Avalonia Headless XUnit`、`RenderTargetBitmap` |
