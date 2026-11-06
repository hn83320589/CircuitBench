# 名詞對照表

## 電路領域術語

| 名詞 | 說明 | 出現位置 |
|---|---|---|
| 節點（Node） | 電路中電位相同的一組連接點；接地固定為節點 `0` | `src/CircuitBench.Core/Model/NodeName.cs` |
| 網路標籤（Net Label） | 圖面上的文字標籤，同名即視為同一節點 | `src/CircuitBench.Schematic/Model/Elements.cs` |
| 接點（Junction） | 三條以上連線交會、或端點落在導線中段時畫出的圓點 | `src/CircuitBench.Schematic/Connectivity/ConnectivityExtractor.cs` |
| 浮接節點（Floating Node） | 沒有經由直流路徑通到節點 0 的節點 | `src/CircuitBench.Core/Validation/CircuitValidator.cs` |
| 直流路徑 | R、L、V、E 的輸出端、二極體、開關；電容與電流源不算 | 同上 |
| E 元件（VCVS） | 電壓控制電壓源，增益無單位 | `src/CircuitBench.Core/Model/Components.cs` |
| G 元件（VCCS） | 電壓控制電流源，轉導單位 S | 同上 |
| 直流工作點（Operating Point, OP） | 所有電容開路、電感短路時的穩態解 | `src/CircuitBench.Core/Analysis/DcSolver.cs` |
| 暫態分析（Transient） | 隨時間變化的響應 | `src/CircuitBench.Core/Analysis/TransientEngine.cs` |
| 交流分析（AC） | 在工作點線性化後的頻率響應 | `src/CircuitBench.Core/Analysis/Simulator.cs` |
| 波德圖（Bode Plot） | 增益（dB）與相位對對數頻率的圖 | `src/CircuitBench.Core/Bode/BodeMath.cs` |
| 轉折點（Breakpoint） | 電源波形斜率不連續或開關切換的時刻，必須剛好落在時間點上 | `src/CircuitBench.Core/Model/Waveforms.cs` |
| 小訊號電導 gd | 二極體在工作點的 dI/dV | `src/CircuitBench.Core/Mna/Devices/DiodeDevice.cs` |
| Ron／Roff | 理想開關導通與截止的電阻，預設 1 mΩ 與 1 TΩ | `src/CircuitBench.Core/Model/Components.cs` |
| 工程記號 | `4.7k`、`10u`、`1meg` 這類字尾寫法；`m` 為毫 | `src/CircuitBench.Core/Units/EngineeringNotation.cs` |
| SPICE 子集網表 | 本專案支援的 SPICE 文字格式（`.cir`） | `src/CircuitBench.Core/IO/` |

## 專案內的自訂名稱

| 名稱 | 說明 | 位置 |
|---|---|---|
| `SchematicDocument` | 不可變的圖面文件 | `src/CircuitBench.Schematic/Model/SchematicDocument.cs` |
| `ComponentSymbol` | 圖面上的元件（有座標、方向、字串參數） | `src/CircuitBench.Schematic/Model/Elements.cs` |
| `ComponentCatalog` | 每種元件的接腳位置、預設參數、名稱前綴 | `src/CircuitBench.Schematic/Model/ComponentCatalog.cs` |
| `ExtractionResult` | 連線萃取的結果：電路、接腳網路、接點、問題 | `src/CircuitBench.Schematic/Connectivity/ConnectivityExtractor.cs` |
| `CircuitIssue` | 電路檢查發現的問題，帶有元件與節點 | `src/CircuitBench.Core/Validation/CircuitValidator.cs` |
| `IssueMarker` | 問題在圖上的紅框位置 | `src/CircuitBench.App.ViewModels/Editor/EditorViewModel.cs` |
| `MnaLayout` | MNA 未知數的排列與中文描述 | `src/CircuitBench.Core/Mna/MnaLayout.cs` |
| `MnaSystem` | 一個電路的矩陣、元件與分解器 | `src/CircuitBench.Core/Mna/MnaSystem.cs` |
| `Device` | 元件的模擬行為（蓋印、暫態歷史） | `src/CircuitBench.Core/Mna/Devices/Device.cs` |
| `TwoTerminalSlots`／`BranchSlots` | 兩端元件與支路電流元件的蓋印輔助結構 | 同上 |
| `StampScale` | 記錄每個矩陣元素抵消前的量級 | 同上 |
| `IntegrationCoefficients` | 積分方法的係數 A0、A1、A2、B | `src/CircuitBench.Core/Mna/LoadContext.cs` |
| `OscillationTracker` | 由相鄰極值估計振盪週期，限制步長 | `src/CircuitBench.Core/Analysis/TransientEngine.cs` |
| `MonotonicProgress` | 保證進度單調遞增並限制回報頻率 | `src/CircuitBench.Core/Analysis/MonotonicProgress.cs` |
| `SolverStatistics` | 診斷頁顯示的統計資料 | `src/CircuitBench.Core/Analysis/AnalysisTypes.cs` |
| `MinMaxPyramid` | 最小最大值金字塔，抽樣用 | `src/CircuitBench.Core/Sampling/MinMaxPyramid.cs` |
| `ProjectLoadResult` | 載入專案檔的結果：Ok、TooNew、Corrupted | `src/CircuitBench.Schematic/Persistence/ProjectFile.cs` |
| `RefSolver` | 獨立的稠密參考求解器 | `tests/CircuitBench.ReferenceSolver/RefSolver.cs` |
| `PropertyTest` | 固定種子的性質測試 harness | `tests/CircuitBench.TestKit/Property/PropertyTest.cs` |
| `ManualTimeProvider` | 測試用、可手動推進的 `TimeProvider` | `tests/CircuitBench.TestKit/ManualTimeProvider.cs` |
| `NullDialogs` | 測試用的對話框服務 | `src/CircuitBench.App.ViewModels/Services/Services.cs` |

## 技術縮寫

| 縮寫 | 全名 | 說明 |
|---|---|---|
| MNA | Modified Nodal Analysis | 修正節點分析：節點電壓加上部分支路電流為未知數 |
| LU | LU Decomposition | 把矩陣分解成下三角 L 與上三角 U |
| CSC | Compressed Sparse Column | 以欄壓縮的稀疏矩陣格式，`SparsityPattern` 使用 |
| LTE | Local Truncation Error | 局部截斷誤差，每一步積分的誤差 |
| BE | Backward Euler | 後向尤拉法，一階、L-stable |
| TR | Trapezoidal Rule | 梯形法，二階、A-stable 但不是 L-stable |
| BDF2 | Second-order Backward Differentiation Formula | 二階後向差分公式，L-stable |
| L-stable | — | 剛性模態會被衰減到 0，不會正負交替 |
| ULP | Unit in the Last Place | 浮點數最小的可表示間隔 |
| pnjlim | PN Junction Limiting | SPICE 的接面電壓限制演算法 |
| gmin | Minimum Conductance | 並聯在節點或二極體上的極小電導，幫助收斂 |
| reltol／vntol／abstol | Relative／Voltage／Absolute Tolerance | 相對容許值、電壓絕對容許值、電流絕對容許值 |
| trtol | Transient Tolerance Factor | 暫態 LTE 容許值的放大係數，本專案預設 1 |
| RMS | Root Mean Square | 方均根值 |
| MVVM | Model-View-ViewModel | UI 架構模式 |
| CPM | Central Package Management | NuGet 集中套件版本管理（`Directory.Packages.props`） |
