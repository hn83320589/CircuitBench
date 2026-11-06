# CircuitBench 測試報告與驗收對照

| 項目 | 內容 |
|---|---|
| 報告日期 | 2026-11-06 |
| 受測版本 | commit `780a46e`（main） |
| 本機環境 | Intel Core i7-13700、32 GB RAM、Windows 11、.NET SDK 9.0.315、Release 組態 |
| CI | GitHub Actions run [42166516117](https://github.com/hn83320589/CircuitBench/actions/runs/42166516117)，`windows-latest`：全部步驟成功 |

## 1. 結論

**26 項驗收標準全部有自動化測試或文件佐證，且全部通過。**

| 驗證項目 | 結果 |
|---|---|
| 建置 | `dotnet build -c Release`：0 個警告、0 個錯誤（`TreatWarningsAsErrors` 已開啟） |
| 功能測試 | 333 個，全數通過、0 略過 |
| 性質測試 | 15 組 |
| 效能測試 | 4 項，皆在門檻內 |

有 3 點需要特別說明，詳見第 5 節：

1. 驗收標準 8 的欠阻尼 RLC：最大步長要設為振盪週期的 1/100 才能達到 0.2%，預設步長不夠。
2. 驗收標準 22 的「重繪 ≤ 16 ms」：自動化量測的是抽樣與刻度計算，沒有包含 Skia 的實際繪製時間。
3. CI 上的效能測試只供參考，不影響 CI 是否通過。

## 2. 測試執行摘要

執行指令：`dotnet test -c Release --filter "Category!=Performance"`（TRX 結果 333/333 Passed）。

| 測試專案 | 案例數 | 通過 | 失敗 | 主要測試類別 |
|---|---|---|---|---|
| CircuitBench.Core.Tests | 205 | 205 | 0 | EngineeringNotation 65、SparseLu 10、OperatingPoint 8、NonlinearDc 13、DcSweep 5、Transient 12、Ac 13、Cancellation 5、CircuitValidator 16、MinMaxPyramid 5、Measurement 6、Bode 5、Csv 2、SpiceNetlist 18、Waveform 12、Circuit 3、ReferenceSolverSanity 7 |
| CircuitBench.Schematic.Tests | 60 | 60 | 0 | Connectivity 15、Orientation 10、UndoHistory 4、DocumentEditing 3、HitTester 4、AutoLayout 2、SvgExport 2、Persistence 20 |
| CircuitBench.App.ViewModels.Tests | 63 | 63 | 0 | EditorViewModel 15、AnalysisViewModel 7、WaveformViewModel 7、AutoSave 4、MainWindowFile 5、Sample 25 |
| CircuitBench.App.Headless.Tests | 5 | 5 | 0 | Avalonia Headless（Skia 實際繪製） |
| **合計** | **333** | **333** | **0** | |

**性質測試（15 組）**：

- 都使用固定的基底種子；失敗時會印出可重現的種子，以 `CIRCUITBENCH_SEED` 只重跑那一組。
- 括號內是每組產生的隨機案例數。

| # | 測試 | 驗證內容 |
|---|---|---|
| 1 | `Property_RealRandomSparseMatchesDense`（200） | 實數稀疏 LU 與稠密解一致 |
| 2 | `Property_ComplexRandomSparseMatchesDense`（200） | 複數稀疏 LU 與稠密解一致 |
| 3 | `Property_RefactorWithSameStructureEqualsFreshFactor`（100） | 重用符號分解的結果與重新分解相同 |
| 4 | `Property_RandomLinearCircuitsMatchReferenceSolver`（300） | 隨機線性電路的直流工作點與參考求解器一致 |
| 5 | `Property_RandomLinearCircuitsMatchReferenceComplexSolution`（200） | 隨機線性電路的交流解與參考求解器一致 |
| 6 | `Property_StepControlInvariants`（40） | 暫態步長控制的不變量 |
| 7 | `Property_DecimationEqualsBruteForceForAnyRangeAndWidth`（300） | 最小最大抽樣與暴力計算一致 |
| 8 | `Property_RangeMinMaxMatchesLinq`（200） | 區間最小/最大值正確 |
| 9 | `Property_AverageAndRmsOfPiecewiseLinearAreExactOnNonUniformGrid`（200） | 非均勻時間點上的平均值與 RMS |
| 10 | `Property_FrequencyFromInterpolatedZeroCrossings`（50） | 以內插零交越求頻率 |
| 11 | `Property_CancelAtRandomProgressNeverReturnsResult`（12） | 任意進度取消都不回傳結果 |
| 12 | `Property_EveryUndoRestoresDocumentAndNetlist`（10 × 120 步） | 每一步復原後，圖面與網表都回到前一狀態 |
| 13 | `Property_RandomCircuitsRoundTrip`（150） | SPICE 網表往返 |
| 14 | `Property_RandomCircuitsSurviveAutoLayout`（50） | 匯入網表的自動排列不改變電路 |
| 15 | `Property_RandomDocumentsRoundTrip`（30） | 專案檔往返 |

## 3. 效能測試

執行指令：`dotnet test tests/CircuitBench.Performance.Tests -c Release --filter "Category=Performance"`。每項先暖機一次，取第二次的數據。下表列出本機兩次執行的結果。

| 項目 | 門檻 | 第 1 次 | 第 2 次 | 判定 |
|---|---|---|---|---|
| 2,000 節點 RC 梯形網路，暫態 10,000 步 | ≤ 10 s | 3.04 s | 1.67 s | ✅ |
| 100 × 100 電阻網格（1 萬個未知數），直流工作點 | ≤ 1 s | 0.31 s | 0.15 s | ✅ |
| 2,000 節點，500 個頻率點的交流分析 | ≤ 3 s | 0.22 s | 0.22 s | ✅ |
| 100 萬點 × 2 條曲線（寬 1,920 px），縮放與平移重繪 200 次的最差耗時 | ≤ 16 ms | 5.5 ms | 2.8 ms | ✅ |

補充數據：
- 暫態：10,069 個接受步、2 個拒絕步；只做數值分解（重用符號分解）10,071 次。
- 網格：非零元素 49,597 個，LU 填入後 393,318 個。
- CI runner 上的效能步驟同樣成功，但結果只供參考。

## 4. 驗收標準對照表

| # | 驗收標準（摘要） | 佐證（測試／文件） | 結果 |
|---|---|---|---|
| 1 | 工程記號：各字尾、大小寫、m／meg、尾端單位、無效輸入訊息 | `EngineeringNotationTests`（65 案例，含指數溢位的防護） | ✅ |
| 2 | 連線萃取：交叉不相連、T 字接點、接腳與端點相連、同名標籤、接地為 0、未連接接腳；旋轉鏡射後的接腳位置 | `ConnectivityTests`（15）、`OrientationTests`（10） | ✅ |
| 3 | 電路檢查五類問題，且指出元件或節點 | `CircuitValidatorTests`（16）：無接地、只經電容或電流源的浮接、V/E/L 迴路、未連接接腳、參數無效，每類都驗證 `ComponentNames`／`Nodes` | ✅ |
| 4 | 稀疏 LU：相對誤差 ≤ 1e-10、偵測奇異、重用結果相同 | 性質測試 #1–3；`StructurallySingular…`、`NumericallySingular…`、`ComplexSingular…`、`RefactorFallsBackWhenPivotBecomesTiny` | ✅ |
| 5 | 300 組隨機線性電路的直流工作點，相對誤差 ≤ 1e-9 | 性質測試 #4（含 R、V、I、E、G） | ✅ |
| 6 | 非線性直流：Lambert W、橋式整流、反向偏壓；步進收斂並在診斷頁顯示方法 | `NonlinearDcTests`（13）；`DiagnosticsShowsSteppingMethod`（ViewModel） | ✅ |
| 7 | 直流掃描與逐點工作點一致 | `DcSweepTests.SweepMatchesPointwiseOperatingPoints` | ✅ |
| 8 | RC 步階誤差 ≤ 1e-3·max + 1e-6 V；RLC 頻率與衰減率誤差 ≤ 0.2% | `RcStepResponseMatchesAnalyticSolution`（預設設定）；`UnderdampedRlcFrequencyAndDecayMatchAnalytic`（最大步長 = 週期/100，見 5.1） | ✅（附條件） |
| 9 | LC 100 週期能量漂移 ≤ 1%；開關切斷電感電流無逐步交替 | `LosslessLcOscillatesHundredPeriods…`；`SwitchInterruptingInductorCurrent…`（有、無並聯電阻兩種）；`StiffnessAppearingWithoutBreakpointDoesNotRing` | ✅ |
| 10 | 轉折點為精確時間點（≤ 1e-15 s），轉折前後不跨越 | `PulsePwlAndSwitchBreakpointsAreExactTimePoints`、`PulseValuesAtCornersAreHitExactly` | ✅ |
| 11 | 拒絕的步驟不留在結果中、時間嚴格遞增、遵守最大步長 | 性質測試 #6；`SharpPulseCausesRejectedSteps…` | ✅ |
| 12 | 半波與全波整流，與參考求解器（固定極小步長）誤差 ≤ 0.5% 峰值 | `HalfWaveRectifierMatchesFineStepReference`、`FullWaveBridgeRectifierWithFilterMatchesFineStepReference`（參考求解器用 TR、步長 2 µs） | ✅ |
| 13 | RC −3 dB 頻率 ≤ 0.1%、相位 −45° ±0.05°；RLC 的 f₀ 與 Q；隨機電路複數解 ≤ 1e-9 | `RcLowPassMinus3dBFrequencyAndPhase`、`SeriesRlcResonanceFrequencyAndQ`、性質測試 #5 | ✅ |
| 14 | 二極體在不同偏壓下的小訊號阻抗 | `DiodeSmallSignalImpedanceFollowsBias`（4 種偏壓，含反向） | ✅ |
| 15 | 任何時間點取消都不留下半套結果；進度單調遞增 | 性質測試 #11；`ProgressIsMonotonicAndEndsAtOne`（tran／dc／ac）；`CancellationLeavesPreviousResultsUntouched`（ViewModel） | ✅ |
| 16 | 最小最大抽樣等於暴力計算，尖峰不遺漏 | 性質測試 #7、#8；`SingleSampleSpikeIsNeverLostAtAnyZoom`（100 萬點） | ✅ |
| 17 | 非均勻時間點的平均值與 RMS、內插零交越求頻率、游標讀值內插 | 性質測試 #9、#10；`SineMeasurementsMatchAnalyticValues`、`CursorReadoutIsInterpolated`、`MeasurementWindowUsesInterpolatedEndpoints` | ✅ |
| 18 | dB 與相位計算、相位展開、對數頻率軸刻度 | `BodeTests`（5） | ✅ |
| 19 | 復原與重做至少 100 層，每步都回到前一狀態 | 性質測試 #12；`AtLeastHundredLevelsAndOldestDroppedBeyondCapacity` | ✅ |
| 20 | 專案檔往返無損、版本過新或損毀有明確訊息、自動儲存與當機回復 | `PersistenceTests`（20，含 15 種損毀輸入）、性質測試 #15、`AutoSaveTests`（4，含模擬當機後回復） | ✅ |
| 21 | SPICE 網表匯出再匯入相同、語法錯誤指出行號 | 性質測試 #13；`ExportThenImportGivesSameCircuit…`；`SyntaxErrorsReportLineNumberAndReason`（12 種） | ✅ |
| 22 | 效能四項，README 載明量測環境 | 第 3 節；README「效能」一節 | ✅（重繪見 5.2） |
| 23 | ViewModel 測試，以及至少 3 個 Avalonia Headless 測試 | `EditorViewModelTests`（15：工具切換、選取、拖曳、旋轉、標籤命名、檢查標示）；`HeadlessTests`（5） | ✅ |
| 24 | 至少 250 個案例（含 8 組性質測試），全數通過 | 333 個案例、15 組性質測試 | ✅ |
| 25 | README 含規定的各節內容 | 建置、截圖（4 張，由 Headless 測試產生）、架構、MNA、稀疏 LU／符號分解重用、暫態與步長控制、收斂策略、效能數據、設計取捨、已知限制 | ✅ |
| 26 | Windows CI 綠燈、徽章、上傳 win-x64 artifact | run 42166516117 成功；README 有徽章；artifact `CircuitBench-win-x64`（約 42 MB）與 `test-results` | ✅ |

### 技術限制與交付物

| 項目 | 驗證方式 | 結果 |
|---|---|---|
| 專案切分：Core、Schematic、ViewModels 不參考 UI | 這三個專案的程式碼都沒有 Avalonia 參考（只有一行註解提到它） | ✅ |
| 沒有使用禁用套件 | `Directory.Packages.props` 只有 Avalonia、CommunityToolkit.Mvvm、xUnit、Test SDK；沒有 Math.NET、SpiceSharp、OxyPlot、ScottPlot、LiveCharts，也沒有 WPF 或 WinForms | ✅ |
| 「現在時間」都透過 TimeProvider | 原始碼中沒有 `DateTime.Now`、`DateTimeOffset.Now` 或 `Stopwatch`（搜尋結果只出現在 Avalonia 的 DLL 裡） | ✅ |
| 參考求解器與正式程式碼互相獨立 | `CircuitBench.ReferenceSolver.csproj` 沒有任何 ProjectReference；README 有說明 | ✅ |
| 範例電路至少 10 個，並附預期結果 | `samples/` 有 12 組（專案檔與網表各一）；`samples/EXPECTED.md`；`SampleTests` 對 24 個檔案自動驗證預期值 | ✅ |
| 沒有未附 issue 編號的 TODO | 搜尋 `TODO`：0 筆 | ✅ |

## 5. 需要說明的事項

### 5.1 驗收標準 8：欠阻尼 RLC 需要設定最大步長

梯形法的頻率誤差約為 (ωh)²/12，衰減率誤差約為 (ωh)²/4。LTE 步長控制管的是每一步的局部誤差，不是長時間累積的相位誤差，所以在預設最大步長（停止時間的 1/50）下：

- 測試電路的頻率誤差約 1.7%，超過門檻。
- 把最大步長設為**振盪週期的 1/100** 後，頻率誤差約 0.03%、衰減率誤差約 0.1%，符合 ≤ 0.2%。

標準要求的是準確度，沒有規定分析設定，而最大步長本來就是使用者可以設定的參數，所以判定為通過。README 的「暫態積分」與「已知限制」都寫了這個建議。如果驗收方要求預設設定也要達標，就得調降預設的 LTE 容許值或預設最大步長，代價是所有暫態分析的點數都會增加。

### 5.2 驗收標準 22：「重繪 ≤ 16 ms」的量測範圍

自動化測試量的是每次重繪時，ViewModel 依目前範圍把兩條 100 萬點曲線抽樣成每像素欄的最小/最大值，再加上刻度計算的時間。繪圖控制項只畫這些抽樣結果（每條曲線約 2 × 1,920 個頂點）。

Skia 的實際繪製時間沒有放進自動化測試，因為 Headless 的計時與實機 GPU 繪製不同。這段時間會即時顯示在波形頁右上角（「重繪 x ms」），截圖中約 1.3 ms。

### 5.3 CI 上的效能測試只供參考

GitHub runner 的效能浮動很大，所以 CI 會執行效能測試，但設為 `continue-on-error`，不影響是否通過；正式數據以第 3 節的本機量測為準。這次 CI 的效能步驟同樣成功。

### 5.4 測試過程中發現並修正的問題

| 發現途徑 | 問題 | 修正 |
|---|---|---|
| 範例 08 的回歸測試 | 二極體在非轉折點截止時，高阻抗節點上出現 TR 的逐步正負交替 | TR 的某個未知數反向時以 BDF2 重算比對，判定是振盪就改用 BDF2（commit `881c8f0`），並新增 Core 層的回歸測試 |
| Headless 截圖檢查 | 波形在邊緣處的最小/最大連線順序造成假尖刺 | 改成先連到離上一點較近的一端 |
| 自動安全審查 | 工程記號的指數溢位會丟出例外 | 改用 `long.TryParse` 並限制指數範圍，網表讀取也把格式例外轉成行錯誤 |
| 自動安全審查 | 專案檔可以載入未定義的元件類型，之後在繪製或萃取時崩潰 | 載入時拒絕未定義的列舉值與 null 參數 |
| CI 第一次執行失敗 | TestKit 被誤當成測試專案 | 在 csproj 設定 `IsTestProject=false` |

### 5.5 沒有自動化覆蓋的部分

- **實機 GUI 的人工操作流程**：例如連續拖曳的手感、檔案對話框、多螢幕 DPI。目前只有發佈檔的啟動冒煙測試：程式能正常開啟，視窗標題正確。
- **多台機器的效能比較**：只量測了一台機器。

## 6. 重現方式

```powershell
dotnet build -c Release
dotnet test -c Release --filter "Category!=Performance" --logger trx
dotnet test tests/CircuitBench.Performance.Tests -c Release --filter "Category=Performance" --logger "console;verbosity=detailed"
# 重新產生範例檔／截圖
$env:CIRCUITBENCH_WRITE_SAMPLES=1; dotnet test tests/CircuitBench.App.ViewModels.Tests --filter "FullyQualifiedName~SampleTests"
$env:CIRCUITBENCH_SCREENSHOTS="docs/images"; dotnet test tests/CircuitBench.App.Headless.Tests
```
