# 04 技術選型

這個專案的選型有一個特點：**大部分選擇是被需求規格指定或禁止的**。真正由開發端做的選擇，主要是「不引入什麼」。

## 技術與套件

版本以 `Directory.Packages.props` 與 `Directory.Build.props` 為準。

| 技術／套件 | 版本 | 用途 | 選用理由 | 可能的替代方案 |
|---|---|---|---|---|
| .NET | 9（`net9.0`），SDK 9.0.315 | 執行環境 | 📄 需求規格指定 | — |
| C# | `LangVersion=latest` | 語言 | 📄 需求規格指定。程式用到 primary constructor、collection expression（`[]`）、`INumberBase<T>` 泛型數學 | — |
| Avalonia、Avalonia.Desktop、Avalonia.Themes.Fluent | 11.3.22 | UI 框架 | 📄 需求規格指定「Avalonia 11」，並禁用 WPF、WinForms、WinUI、MAUI、Uno | — |
| Avalonia.Skia | 11.3.22 | 繪圖後端（Headless 測試需要明確參考） | 🔍 推斷：Headless 測試要以 Skia 實際光柵化來產生截圖與量測重繪時間，所以 Headless 測試專案直接參考 | — |
| CommunityToolkit.Mvvm | 8.4.2 | `ObservableObject`、`[ObservableProperty]`、`[RelayCommand]` source generator | 📄 需求規格「可用 CommunityToolkit.Mvvm」 | ReactiveUI（Avalonia 範本的預設），學習曲線較陡 |
| xUnit、xunit.runner.visualstudio | 2.9.3、3.1.5 | 測試框架 | 📄 需求規格指定 xUnit | — |
| Avalonia.Headless.XUnit | 11.3.22 | 不開視窗的 Avalonia UI 測試（`[AvaloniaFact]`） | 📄 驗收標準 23「Avalonia Headless 至少 3 個」 | — |
| Microsoft.NET.Test.Sdk | 18.11.0 | 測試主機 | xUnit 必需 | — |
| System.Text.Json | BCL 內建 | 專案檔序列化 | 📄 規劃文件：「JSON 一律用 BCL 內建的 System.Text.Json」 | Newtonsoft.Json，但會多一個相依 |
| System.Numerics.Complex | BCL 內建 | 交流分析的複數 | 📄 需求規格唯一明確允許的數值型別 | — |
| System.Collections.Immutable | BCL 內建 | 不可變圖面文件與電路模型 | 📄 README「設計取捨」：讓復原與重做只要保存快照 | 可變模型 + 命令模式，見 03 章 |

## 刻意不引入的東西

| 沒用的東西 | 原因 |
|---|---|
| Math.NET、SpiceSharp、OxyPlot、ScottPlot、LiveCharts | 📄 需求規格明確禁止「現成的數值線性代數、電路模擬或繪圖圖表套件」。所以稀疏 LU、求解器與波形繪製都是自己寫的。這是本專案最有學習價值的部分 |
| DI 容器（Microsoft.Extensions.DependencyInjection 等） | 📄 規劃文件決定：Composition Root 只有十幾行，手寫比較清楚 |
| FsCheck 等性質測試框架 | 📄 README「設計取捨」：自己寫 harness，比較容易做到「固定種子、失敗時印出種子」，也避免多一個套件。實作只有約 40 行，見 `tests/CircuitBench.TestKit/Property/PropertyTest.cs` |
| `Microsoft.Extensions.TimeProvider.Testing` 的 `FakeTimeProvider` | 📄 規劃文件 T21：「這個測試用的類別是自己寫，不另外引入套件」。實作在 `tests/CircuitBench.TestKit/ManualTimeProvider.cs` |
| 日誌框架 | 🔍 推斷：桌面單機程式沒有集中收集日誌的需求，診斷資訊改以 `SolverStatistics` 顯示在診斷頁 |

## 建置設定

`Directory.Build.props` 對所有專案生效：

| 設定 | 值 | 意義 |
|---|---|---|
| `TreatWarningsAsErrors` | true | 任何編譯警告都會讓建置失敗，強迫在 commit 前處理 |
| `Nullable` | enable | 開啟可為 null 的參考型別分析 |
| `AnalysisLevel` | latest | 使用最新的 .NET 分析器規則 |
| `Deterministic` | true | 相同輸入產生相同的組件，方便比對 |
| `ManagePackageVersionsCentrally` | true | 套件版本只寫在 `Directory.Packages.props`，各 csproj 檔只寫套件名稱 |

`Directory.Packages.props` 用 MSBuild 屬性 `$(AvaloniaVersion)` 統一 Avalonia 家族的版本，避免各套件版本不一致。

## 版本注意事項

- **.NET 9 是 STS（Standard Term Support）版本**，支援期比 LTS 短。需求規格指定 .NET 9，之後若要升級到 .NET 10 LTS，`TimeProvider`、泛型數學等 API 都相容，預期改動很小（🔍 推斷）。
- **Avalonia 11.3**：`RenderOptions` 與 `DrawingContext.PushRenderOptions()` 是 11.x 的 API，`WaveformCanvas` 用它關閉曲線的反鋸齒。升級到 Avalonia 12 時要確認這組 API 是否改名（🔍 推斷）。
- **Headless 測試用的是 CPU 光柵化**：效能量測結果和實機 GPU 繪製不同，見 09 章。
