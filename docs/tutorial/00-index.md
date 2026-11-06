# CircuitBench 教學文件導覽

CircuitBench（電路實驗台）是給高職電子科學生使用的離線中文電路模擬桌面工具。學生在格點畫布上畫電路，就能執行直流、暫態、交流分析，並查看波形與波德圖。電路接錯時，程式會用中文指出是哪個元件或節點出錯，並在圖上用紅框標出。

**技術棧**：C# / .NET 9、Avalonia 11.3（MVVM，使用 CommunityToolkit.Mvvm 8.4）、xUnit 2.9。數值核心全部自行實作，沒有使用任何數值、電路模擬或圖表套件。

**文件對應版本**：commit `25ddfd2`（2026-11-06）

---

## 閱讀路線

| 路線 | 章節 | 適合 |
|---|---|---|
| 快速掌握（約 30 分鐘） | 00 → 03 → 07 | 想先知道程式碼怎麼切、從哪裡讀起 |
| 完整學習 | 依序 01 → 12 | 想把整個專案當成教材讀一遍 |
| 想學數值方法 | 08 → 11 | 稀疏 LU、Newton 收斂、暫態積分與步長控制、奇異偵測 |
| 想學 MVVM 與可測試的 UI | 03 → 07「ViewModel 層」→ 09 | 把 UI 邏輯放進 ViewModel、用 Headless 測試 Avalonia |
| 想學如何驗證數值程式 | 09 → 11 | 參考求解器、性質測試、解析解比對 |
| 想知道踩過哪些坑 | 11 | 審查回饋的三個問題、TR 振盪、CI 失敗 |

## 章節清單

| 章節 | 檔案 | 狀態 |
|---|---|---|
| 01 需求分析 | [01-requirements.md](01-requirements.md) | 已產生 |
| 02 系統分析 | [02-system-analysis.md](02-system-analysis.md) | 已產生 |
| 03 架構設計 | [03-architecture.md](03-architecture.md) | 已產生 |
| 04 技術選型 | [04-tech-stack.md](04-tech-stack.md) | 已產生 |
| 05 資料庫設計 | — | **略過**：本專案沒有資料庫。資料只存成 JSON 專案檔與自動儲存檔，格式說明已併入 06 章「專案檔」一節 |
| 06 輸入輸出介面與資料流 | [06-api-dataflow.md](06-api-dataflow.md) | 已產生（桌面程式沒有 HTTP API，改寫檔案格式、分析 API 與資料流） |
| 07 程式內容導讀 | [07-code-walkthrough.md](07-code-walkthrough.md) | 已產生 |
| 08 核心演算法 | [08-algorithms.md](08-algorithms.md) | 已產生 |
| 09 測試 | [09-testing.md](09-testing.md) | 已產生 |
| 10 建置與發佈 | [10-deployment.md](10-deployment.md) | 已產生 |
| 11 設計取捨與踩坑紀錄 | [11-tradeoffs-pitfalls.md](11-tradeoffs-pitfalls.md) | 已產生 |
| 12 延伸練習與自我檢核 | [12-exercises.md](12-exercises.md) | 已產生 |
| 名詞對照表 | [glossary.md](glossary.md) | 已產生 |

## 本文件使用的標記

- 📄 **依據**：有明確來源，例如原始需求規格、README、commit 訊息，或開發對話中使用者的說明。
- 🔍 **推斷**：從程式碼行為推論，沒有文件記載。讀者應視為「可能的理由」，而不是當初的決策紀錄。

需求規格的原文來自開發第一天使用者提供的案件說明（以下稱「需求規格」），共 26 項驗收標準。另一個重要依據是 `docs/TEST-REPORT.md`，裡面有驗收對照與審查回饋的修正紀錄。

## 修訂紀錄

| 日期 | 對應 commit | 更新內容 |
|---|---|---|
| 2026-11-06 | `25ddfd2` | 初版，產生全部章節（05 章略過） |
