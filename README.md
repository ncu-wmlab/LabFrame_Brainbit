# LabFrame 2023 - BrainBit Plugin

此套件為 LabFrame 2023 專用的 BrainBit 設備插件，用於連接與管理 BrainBit 腦波儀。

> [!NOTE]
> 請再記得多安裝此套件 https://github.com/BrainbitLLC/unity_em_st_artifacts.git#a04238a934b3da0494dd9120a489005277063a1f
> 開發當下此套件最新版(1.0.3)在android平台會有問題

## 支援功能
1. **設備連線管理：** 自動搜尋並手動觸發連接藍牙 BrainBit 設備。
2. **EEG 腦波數據收集：** 自動收集四個通道 (T3, T4, O1, O2) 的腦波數據。
3. **即時阻抗檢查：** 確認電極與頭皮的接觸阻抗值是否過高 (> 200,000Ω)。
4. **多階段資料分流儲存：** 收集期間可動態切換儲存 Tag（依照遊戲階段無縫寫入不同檔案）。
5. **情緒與光譜分析：** 透過 NeuroSDK `EegEmotionalMath` 即時運算專注 / 放鬆（MindData）與 δ/θ/α/β/γ 五頻段光譜百分比。

---

## 基本使用方式

### 1. 手動觸發設備連線
預設啟動遊戲時**不會**自動連線，需在適當時間點（例如點擊按鈕或進入準備階段時）透過程式碼手動掃描並連線。
```csharp
BrainBitManager.Instance.ManualConnect();
```

---

### 2. 關於 EEG (持續腦波) 的收集方式

#### 👉 開始 / 停止收集
開始收集時，可傳入對應的遊戲階段 Tag：
```csharp
// "Intro_Phase" 將作為存檔檔名的後綴
BrainBitManager.Instance.StartEEGStream(true, "Intro_Phase");

// 停止收集
BrainBitManager.Instance.StopEEGStream();
```

#### 👉 動態無縫切換儲存階段 (Tag)
當遊戲進入下一個階段時，你**不需**停止收集，只需修改 Tag：
```csharp
if (BrainBitManager.Instance.IsStreamingEEG)
{
    // 下一毫秒收集的封包，就會被寫進 "Tutorial_Phase" 的檔案中
    BrainBitManager.Instance.SetEEGTag("Tutorial_Phase");
}
```

#### 👉 取得最新的 EEG 值
若需要在遊戲邏輯中使用即時腦波：
```csharp
var eegData = BrainBitManager.Instance.GetLatestEEGData();
if (eegData != null)
{
    Debug.Log($"T3:{eegData.T3} | T4:{eegData.T4} | O1:{eegData.O1} | O2:{eegData.O2}");
}
```

---

### 3. 關於 Impedance (阻抗) 的檢測方式

> 阻抗 (Impedance) 資料流與 EEG (腦波) 資料流是**獨立**的。通常在實驗或遊戲開始前，先開啟阻抗檢測確認配戴良好，然後停止阻抗檢測，再開始 EEG 腦波收集。

#### 👉 開啟 / 停止阻抗檢測與修改 Tag (與 EEG 邏輯完全相同)
```csharp
// 啟動阻抗數據流並儲存至 "Preparation_Impedance"
BrainBitManager.Instance.StartImpedanceStream(true, "Preparation_Impedance");

// 也可呼叫這個無縫切換 Tag 
BrainBitManager.Instance.SetImpedanceTag("Another_Phase_Impedance");

// 停止阻抗流
BrainBitManager.Instance.StopImpedanceStream();
```

#### 👉 判斷各通道阻抗是否過高與取得數值
可以在 `Update()` 或檢查迴圈中呼叫此段程式碼，一次取得四個通道的數值與警示結果：
```csharp
void CheckImpedanceStatus()
{
    // 確保有資料進來
    var data = BrainBitManager.Instance.GetLatestImpedanceData();
    if (data == null) return;

    // 直接一次性取得所有數值與 Boolean (各通道大於 200,000 即為 true)
    var (t3_val, t3_high, t4_val, t4_high, o1_val, o1_high, o2_val, o2_high) = data.GetImpedanceValues();

    // 判斷是否「整體」阻抗都正常 (< 200,000)
    if (data.IsImpedanceGood)
    {
        Debug.Log("✅ 所有通道阻抗良好！可以開始遊戲/實驗了！");
        // 進入下一階段、開啟 EEG 等等...
    }
    else
    {
        Debug.LogWarning("❌ 有通道阻抗太高！");
        
        // 具體顯示是哪個通道有問題
        if (t3_high) Debug.LogWarning($"- 左側前額 (T3) 接觸不良，目前數值高達: {t3_val}");
        if (t4_high) Debug.LogWarning($"- 右側前額 (T4) 接觸不良，目前數值高達: {t4_val}");
        if (o1_high) Debug.LogWarning($"- 左後腦 (O1) 接觸不良，目前數值高達: {o1_val}");
        if (o2_high) Debug.LogWarning($"- 右後腦 (O2) 接觸不良，目前數值高達: {o2_val}");
    }
}
```

---

### 4. 情緒與光譜分析 (MindData / SpectralData)

整合 NeuroSDK `EegEmotionalMath`，即時取得受測者的**專注度 / 放鬆度**以及**五頻段光譜百分比**。

> 情緒處理需要先完成約 6 秒的**校正** (請受測者安靜配戴)，校正完成後才會開始輸出有效的 MindData / SpectralData。

#### 👉 開始 / 停止情緒處理

```csharp
// 啟動情緒處理（若 EEG 尚未啟動，會自動開啟；停止時會一併停止該次自動啟動的 EEG）
BrainBitManager.Instance.StartEmotionsProcessing(
    autoWriteToLabData: true,
    mindTag: "Gameplay_Mind",
    spectralTag: "Gameplay_Spectral");

// 停止情緒處理
BrainBitManager.Instance.StopEmotionsProcessing();
```

#### 👉 等待校正完成

```csharp
BrainBitManager.Instance.OnCalibrationProgress += pct => Debug.Log($"校正中 {pct}%");
BrainBitManager.Instance.OnCalibrationFinished += () => Debug.Log("校正完成！");

// 或在輪詢中判斷：
if (BrainBitManager.Instance.IsEmotionsCalibrated)
{
    // 此時才能讀到有效的 MindData / SpectralData
}
```

若遊戲中需要重新校正（換受測者、中途摘下又戴回）：
```csharp
BrainBitManager.Instance.RestartCalibration();
```

#### 👉 取得最新的專注 / 放鬆值

```csharp
void Update()
{
    if (!BrainBitManager.Instance.IsEmotionsCalibrated) return;

    var mind = BrainBitManager.Instance.GetLatestMindData();
    if (mind == null) return;

    Debug.Log($"專注: {mind.Attention:F1} / 放鬆: {mind.Relaxation:F1}");
    // mind.InstAttention / mind.InstRelaxation 為瞬時值（抖動大，僅進階使用）
}
```

#### 👉 取得最新的五頻段光譜百分比

```csharp
var spec = BrainBitManager.Instance.GetLatestSpectralData();
if (spec != null)
{
    Debug.Log($"δ:{spec.Delta:F1} θ:{spec.Theta:F1} α:{spec.Alpha:F1} β:{spec.Beta:F1} γ:{spec.Gamma:F1}");
}
```

#### 👉 事件訂閱（event-driven 寫法）

```csharp
BrainBitManager.Instance.OnMindDataReceived     += mind => { /* 更新 UI */ };
BrainBitManager.Instance.OnSpectralDataReceived += spec => { /* 更新 UI */ };
BrainBitManager.Instance.OnEmotionsArtifact     += hasArtifact => { /* 顯示雜訊警告 */ };
```

#### 👉 動態無縫切換儲存階段 (Tag)

```csharp
// 不用停情緒處理，下一筆資料就會被寫進新 tag
BrainBitManager.Instance.SetMindTag("Boss_Phase_Mind");
BrainBitManager.Instance.SetSpectralTag("Boss_Phase_Spectral");
```

#### 👉 可調設定（`BrainBitConfig`）

| 欄位 | 預設 | 說明 |
|---|---|---|
| `EmotionsCalibrationLength` | `6` | 校正時間（秒）。越長越穩，但受測者需要等更久 |
| `EmotionsMentalEstimation` | `false` | 啟用 Mental Estimation（依實驗類型決定） |
| `EmotionsPrioritySide` | `SideType.NONE` | 優先分析腦側：`NONE` / `LEFT` / `RIGHT` |

---

### 5. 設備搜尋與多設備選擇機制

預設情況下，`BrainBitManager` 會自動過濾周遭的藍牙設備，只尋找型號為 `SensorLEBrainBit` 的腦波儀。

如果現場有多台 BrainBit 同時開啟，系統如何決定連哪台？
你可以在 Unity Inspector 或是程式碼中修改 `BrainBitConfig.AutoSelectBestSignal` 的設定：

- **`AutoSelectBestSignal = false`（預設）：** 先搶先贏。系統會直接連線到藍牙掃描名單上的第一台設備。
- **`AutoSelectBestSignal = true`：** 訊號最強優先。系統會比較所有掃描到的 BrainBit 訊號強度 (RSSI)，並自動連線到訊號最強（距離接收器最近）的那台設備。建議在展位或多人環境中開啟此設定。

---

### 6. 實用除錯工具：獲取設備詳細參數

如果需要查看設備底層的詳細資訊（例如：電量、硬體版本、韌體版本、取樣頻率等），可使用內建的解析器 `SensorInfoProvider.cs` 將複雜的底層參數結構化。

```csharp
using NeuroSDK;

void ShowDeviceInfo(BrainBitSensor sensor)
{
    // 將龐雜的系統屬性轉換為易讀的 Dictionary
    Dictionary<string, string> parameters = SensorInfoProvider.GetBrainBitSensorParameters(sensor);

    foreach (var param in parameters)
    {
        Debug.Log($"[{param.Key}]: {param.Value}");
        // 範例輸出： [BattPower]: 85, [State]: StateInRange, [SamplingFrequency]: 250
    }
}
```

---

## 平台需求與建置 (Android / iOS)

本套件已內附相關處理機制：
- **Android：** 必須包含定位 (`ACCESS_FINE_LOCATION`) 與藍牙及掃描 (`BLUETOOTH_CONNECT`, `BLUETOOTH_SCAN` 等) 權限。相關權限已配置在 `Runtime/Plugins/Android/AndroidManifest.xml` 中，Unity 建置時將自動打包。
- **iOS：** 打包後製腳本 `BrainBitPostProcess.cs` 會自動為 `Info.plist` 加入必要藍牙權限 (`NSBluetoothAlwaysUsageDescription`)。
