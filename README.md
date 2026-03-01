# LabFrame 2023 - BrainBit Plugin

此套件為 LabFrame 2023 專用的 BrainBit 設備插件，用於連接與管理 BrainBit 腦波儀。

## 支援功能
1. **設備連線管理：** 自動搜尋並手動觸發連接藍牙 BrainBit 設備。
2. **EEG 腦波數據收集：** 自動收集四個通道 (T3, T4, O1, O2) 的腦波數據。
3. **即時阻抗檢查：** 確認電極與頭皮的接觸阻抗值是否過高 (> 200,000Ω)。
4. **多階段資料分流儲存：** 收集期間可動態切換儲存 Tag（依照遊戲階段無縫寫入不同檔案）。

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

#### 👉 判斷各通道阻抗是否過高
在檢測狀態下，呼叫以下的 API 一次取得四個通道的數值與警示結果：
```csharp
var data = BrainBitManager.Instance.GetLatestImpedanceData();
if (data != null)
{
    // 直接一次性取得所有數值與 Boolean (檢查是否大於 200,000)
    var (t3_val, t3_high, t4_val, t4_high, o1_val, o1_high, o2_val, o2_high) = data.GetImpedanceValues();

    if (t3_high)
        Debug.LogWarning($"T3 沒接好，當前阻抗值: {t3_val}");
    if (t4_high)
        Debug.LogWarning($"T4 沒接好，當前阻抗值: {t4_val}");
}
```

---

## 平台需求與建置 (Android / iOS)

本套件已內附相關處理機制：
- **Android：** 必須包含定位 (`ACCESS_FINE_LOCATION`) 與藍牙及掃描 (`BLUETOOTH_CONNECT`, `BLUETOOTH_SCAN` 等) 權限。相關權限已配置在 `Runtime/Plugins/Android/AndroidManifest.xml` 中，Unity 建置時將自動打包。
- **iOS：** 打包後製腳本 `BrainBitPostProcess.cs` 會自動為 `Info.plist` 加入必要藍牙權限 (`NSBluetoothAlwaysUsageDescription`)。
