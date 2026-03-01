using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Android;
using System;
using LabFrame2023;
using TMPro;

/// <summary>
/// BrainBit 阻抗檢查控制器
/// 專門用於檢測和顯示 BrainBit 設備的阻抗狀態
/// 通過 BrainBitManager 進行設備管理和數據採集
/// </summary>
public class BrainBitCheckController : MonoBehaviour
{
    [Header("阻抗檢測 UI")]
    public Image[] impedanceIcon = new Image[4]; // T3, T4, O1, O2 狀態指示器
    public TextMeshProUGUI[] impedanceValues = new TextMeshProUGUI[4]; // 阻抗數值顯示
    public TextMeshProUGUI[] channelLabels = new TextMeshProUGUI[4]; // 通道標籤 (T3, T4, O1, O2)

    [Header("連接狀態 UI")]
    public Image ConnectStatus; // 連接狀態指示器
    public TextMeshProUGUI ConnectText; // 連接狀態文字
    public TextMeshProUGUI DeviceInfoText; // 設備信息顯示

    [Header("控制按鈕")]
    public Button MainMenuBtn; // 返回主選單
    public Button ConnectBtn; // 連接設備
    public Button DisconnectBtn; // 斷開連接
    public Button StartImpedanceBtn; // 開始阻抗檢測
    public Button StopImpedanceBtn; // 停止阻抗檢測
    public Button RefreshBtn; // 重新掃描設備

    [Header("阻抗檢測設定")]
    public Color excellentImpedanceColor = Color.green; // 優秀阻抗顏色
    public Color badImpedanceColor = Color.red; // 不良阻抗顏色

    [Header("阻抗閾值設定")]
    [Tooltip("阻抗標準，小於等於此值為正常(綠色)，大於為不良(紅色)")]
    public double threshold = 200000.0;

    [Header("檢測設定")]
    public float impedanceUpdateInterval = 0.5f; // 阻抗更新間隔
    public bool autoStartImpedanceOnConnect = true; // 連接後自動開始阻抗檢測
    public bool showImpedanceWarnings = true; // 顯示阻抗警告

    private BrainBitManager brainBitManager;
    private BrainBitConfig config;
    private Coroutine impedanceCheckCoroutine;
    private bool isImpedanceCheckActive = false;
    private float lastImpedanceUpdateTime = 0f;

    // 阻抗檢測狀態
    private readonly string[] channelNames = { "T3", "T4", "O1", "O2" };
    private bool[] channelWarningShown = new bool[4];
    private double[] lastImpedanceValues = new double[4];

    // UI 更新狀態
    private bool needsUIUpdate = true;

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
        }
        if (!Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN"))
        {
            Permission.RequestUserPermission("android.permission.BLUETOOTH_SCAN");
        }
        if (!Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT"))
        {
            Permission.RequestUserPermission("android.permission.BLUETOOTH_CONNECT");
        }
        Debug.Log("FineLocation: " + Permission.HasUserAuthorizedPermission(Permission.FineLocation));
        Debug.Log("BT_SCAN: " + Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN"));
        Debug.Log("BT_CONNECT: " + Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT"));
#endif
        InitializeImpedanceChecker();
    }

    void Update()
    {
        UpdateConnectionStatus();
        UpdateImpedanceDisplay();

        // 定期更新 UI（避免每幀都更新）
        if (needsUIUpdate || Time.time - lastImpedanceUpdateTime > impedanceUpdateInterval)
        {
            UpdateButtonStates();
            needsUIUpdate = false;
        }
    }

    #region 初始化方法

    private void InitializeImpedanceChecker()
    {
        Debug.Log("[BrainBitChecker] 初始化阻抗檢測器...");

        // 獲取 BrainBitManager 實例
        brainBitManager = BrainBitManager.Instance;

        // 載入配置
        config = LabTools.GetConfig<BrainBitConfig>();

        // 初始化 UI
        InitializeUI();

        // 設置按鈕事件
        SetupButtons();

        // 訂閱事件
        SubscribeToEvents();

        Debug.Log("[BrainBitChecker] 阻抗檢測器初始化完成");
    }

    private void InitializeUI()
    {
        // 設置通道標籤
        for (int i = 0; i < channelLabels.Length && i < channelNames.Length; i++)
        {
            if (channelLabels[i] != null)
                channelLabels[i].text = channelNames[i];
        }

        // 初始化阻抗顯示
        ResetImpedanceDisplay();

        // 初始化連接狀態
        UpdateConnectionStatusDisplay(false, "No Connection");

        // 設置初始按鈕狀態
        UpdateButtonStates();

        Debug.Log("[BrainBitChecker] UI 初始化完成");
    }

    #endregion

    #region UI 更新方法

    private void UpdateConnectionStatus()
    {
        if (brainBitManager == null) return;

        bool isConnected = brainBitManager.IsConnected;
        string statusText = GetConnectionStatusText();

        UpdateConnectionStatusDisplay(isConnected, statusText);
        UpdateDeviceInfo();
    }

    private string GetConnectionStatusText()
    {
        if (brainBitManager.IsConnected)
        {
            if (brainBitManager.IsStreamingImpedance)
                return "Connected - Detecting";
            else
                return "Connected";
        }
        else if (brainBitManager.IsScanning)
        {
            return "Scanning...";
        }
        else
        {
            return "No Connection";
        }
    }

    private void UpdateConnectionStatusDisplay(bool isConnected, string statusText)
    {
        if (ConnectStatus != null)
        {
            ConnectStatus.color = isConnected ? excellentImpedanceColor : badImpedanceColor;
        }

        if (ConnectText != null)
        {
            ConnectText.text = statusText;
        }
    }

    private void UpdateDeviceInfo()
    {
        if (DeviceInfoText == null) return;

        if (brainBitManager.IsConnected)
        {
            DeviceInfoText.text = $"設備: {brainBitManager.ConnectedDeviceName}\n" +
                                 $"地址: {brainBitManager.ConnectedDeviceAddress}";
        }
        else
        {
            DeviceInfoText.text = "設備: No Connection";
        }
    }

    private void UpdateImpedanceDisplay()
    {
        if (brainBitManager == null || !brainBitManager.IsConnected) return;

        var impedanceData = brainBitManager.GetLatestImpedanceData();
        if (impedanceData == null) return;

        var values = impedanceData.ImpedanceValues;
        lastImpedanceUpdateTime = Time.time;

        // 更新各通道的阻抗顯示
        for (int i = 0; i < impedanceIcon.Length && i < values.Count; i++)
        {
            UpdateChannelImpedance(i, values[i]);
        }

        // 檢查是否需要顯示警告
        CheckImpedanceWarnings(impedanceData);
    }

    private void UpdateChannelImpedance(int channelIndex, double impedanceValue)
    {
        // 更新數值顯示
        if (impedanceValues[channelIndex] != null)
        {
            impedanceValues[channelIndex].text = FormatImpedanceValue(impedanceValue);
        }

        // 更新狀態指示器顏色
        if (impedanceIcon[channelIndex] != null)
        {
            impedanceIcon[channelIndex].color = GetImpedanceColor(impedanceValue);
        }

        // 記錄數值
        lastImpedanceValues[channelIndex] = impedanceValue;
    }

    private string FormatImpedanceValue(double value)
    {
        if (value >= 1000000)
            return $"{value / 1000000:F1}MΩ";
        else if (value >= 1000)
            return $"{value / 1000:F0}kΩ";
        else
            return $"{value:F0}Ω";
    }

    private Color GetImpedanceColor(double impedanceValue)
    {
        if (impedanceValue <= threshold)
            return excellentImpedanceColor;
        else
            return badImpedanceColor;
    }

    private void CheckImpedanceWarnings(BrainBit_ImpedanceData impedanceData)
    {
        if (!showImpedanceWarnings) return;

        var values = impedanceData.ImpedanceValues;

        for (int i = 0; i < values.Count && i < channelWarningShown.Length; i++)
        {
            // 如果阻抗值超過閾值(不良)且還沒顯示過警告
            if (values[i] > threshold && !channelWarningShown[i])
            {
                ShowImpedanceWarning(i, values[i]);
                channelWarningShown[i] = true;
            }
            // 如果阻抗值恢復正常，重置警告標記
            else if (values[i] <= threshold && channelWarningShown[i])
            {
                channelWarningShown[i] = false;
                Debug.Log($"[BrainBitChecker] {channelNames[i]} 通道阻抗已恢復正常: {FormatImpedanceValue(values[i])}");
            }
        }
    }

    private void ShowImpedanceWarning(int channelIndex, double impedanceValue)
    {
        string channelName = channelIndex < channelNames.Length ? channelNames[channelIndex] : $"通道{channelIndex}";
        string message = $"{channelName} 通道阻抗過高！\n" +
                        $"當前值: {FormatImpedanceValue(impedanceValue)}\n" +
                        $"建議值: < {FormatImpedanceValue(threshold)}\n\n" +
                        "請檢查電極接觸是否良好";

        Debug.LogWarning($"[BrainBitChecker] {message}");

        // 可以選擇顯示提示框
        // LabPromptBox.Show(message);
    }

    private void ResetImpedanceDisplay()
    {
        // 重置阻抗值顯示
        for (int i = 0; i < impedanceValues.Length; i++)
        {
            if (impedanceValues[i] != null)
                impedanceValues[i].text = "---";

            if (impedanceIcon[i] != null)
                impedanceIcon[i].color = Color.gray;
        }

        // 重置警告標記
        for (int i = 0; i < channelWarningShown.Length; i++)
        {
            channelWarningShown[i] = false;
        }

        Debug.Log("[BrainBitChecker] 阻抗顯示已重置");
    }

    #endregion

    #region 按鈕設置和狀態更新

    private void SetupButtons()
    {
        ConnectBtn?.onClick.AddListener(StartConnection);
        DisconnectBtn?.onClick.AddListener(StopConnection);
        StartImpedanceBtn?.onClick.AddListener(StartImpedanceCheck);
        StopImpedanceBtn?.onClick.AddListener(StopImpedanceCheck);
        RefreshBtn?.onClick.AddListener(RefreshConnection);

        Debug.Log("[BrainBitChecker] 按鈕事件設置完成");
    }

    private void UpdateButtonStates()
    {
        if (brainBitManager == null) return;

        bool isConnected = brainBitManager.IsConnected;
        bool isScanning = brainBitManager.IsScanning;
        bool isStreamingImpedance = brainBitManager.IsStreamingImpedance;

        // 連接相關按鈕
        if (ConnectBtn != null)
            ConnectBtn.interactable = !isConnected && !isScanning;

        if (DisconnectBtn != null)
            DisconnectBtn.interactable = isConnected;

        if (RefreshBtn != null)
            RefreshBtn.interactable = !isConnected && !isScanning;

        // 阻抗檢測按鈕
        if (StartImpedanceBtn != null)
            StartImpedanceBtn.interactable = isConnected && !isStreamingImpedance;

        if (StopImpedanceBtn != null)
            StopImpedanceBtn.interactable = isStreamingImpedance;

        // 主選單按鈕始終可用
        if (MainMenuBtn != null)
            MainMenuBtn.interactable = true;
    }

    #endregion

    #region 事件訂閱和處理

    private void SubscribeToEvents()
    {
        if (brainBitManager != null)
        {
            brainBitManager.OnConnectionStatusChanged += OnConnectionStatusChanged;
            brainBitManager.OnImpedanceDataReceived += OnImpedanceDataReceived;
            brainBitManager.OnDeviceFound += OnDeviceFound;
            brainBitManager.OnError += OnError;

            Debug.Log("[BrainBitChecker] 事件訂閱完成");
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (brainBitManager != null)
        {
            brainBitManager.OnConnectionStatusChanged -= OnConnectionStatusChanged;
            brainBitManager.OnImpedanceDataReceived -= OnImpedanceDataReceived;
            brainBitManager.OnDeviceFound -= OnDeviceFound;
            brainBitManager.OnError -= OnError;

            Debug.Log("[BrainBitChecker] 事件取消訂閱完成");
        }
    }

    #region 事件處理器

    private void OnConnectionStatusChanged(bool isConnected)
    {
        needsUIUpdate = true;

        if (isConnected)
        {
            Debug.Log("[BrainBitChecker] 設備連接成功");

            // 自動開始阻抗檢測
            if (autoStartImpedanceOnConnect)
            {
                StartCoroutine(DelayedStartImpedance());
            }
        }
        else
        {
            Debug.Log("[BrainBitChecker] 設備已斷開連接");

            // 停止阻抗檢測
            StopImpedanceCheck();

            // 重置顯示
            ResetImpedanceDisplay();
        }
    }

    private void OnImpedanceDataReceived(BrainBit_ImpedanceData impedanceData)
    {
        // 數據接收處理在 UpdateImpedanceDisplay 中進行
        // 這裡可以添加額外的處理邏輯

        if (Time.time - lastImpedanceUpdateTime > 5f) // 每5秒打印一次日誌
        {
            Debug.Log($"[BrainBitChecker] 阻抗數據更新: {impedanceData}");
        }
    }

    private void OnDeviceFound(List<NeuroSDK.SensorInfo> devices)
    {
        Debug.Log($"[BrainBitChecker] 發現 {devices.Count} 個設備");

        foreach (var device in devices)
        {
            Debug.Log($"[BrainBitChecker] 設備: {device.Name} ({device.Address})");
        }

        if (devices.Count == 0)
        {
            LabPromptBox.Show("未發現 BrainBit 設備\n請確認設備已開啟且在範圍內");
        }
    }

    private void OnError(string errorMessage)
    {
        Debug.LogError($"[BrainBitChecker] BrainBit 錯誤: {errorMessage}");

        // 顯示錯誤提示
        string userMessage = $"BrainBit 設備錯誤:\n{errorMessage}";
        LabPromptBox.Show(userMessage);

        // 如果是連接錯誤，重置狀態
        if (errorMessage.Contains("connection") || errorMessage.Contains("連接"))
        {
            ResetImpedanceDisplay();
            needsUIUpdate = true;
        }
    }

    #endregion

    #region 按鈕事件處理器

    private void StartConnection()
    {
        Debug.Log("[BrainBitChecker] 開始連接設備...");
        brainBitManager?.ManualConnect();
        needsUIUpdate = true;
    }

    private void StopConnection()
    {
        Debug.Log("[BrainBitChecker] 停止連接...");

        // 先停止阻抗檢測
        StopImpedanceCheck();

        // 然後斷開連接
        brainBitManager?.ManualDisconnect();
        needsUIUpdate = true;
    }

    private void RefreshConnection()
    {
        Debug.Log("[BrainBitChecker] 重新掃描設備...");

        // 如果已連接，先斷開
        if (brainBitManager?.IsConnected == true)
        {
            StopConnection();
        }

        // 開始新的掃描
        StartCoroutine(DelayedStartConnection());
    }

    private void StartImpedanceCheck()
    {
        if (brainBitManager?.IsConnected != true)
        {
            Debug.LogWarning("[BrainBitChecker] 無法開始阻抗檢測 - 設備未連接");
            LabPromptBox.Show("請先連接 BrainBit 設備");
            return;
        }

        Debug.Log("[BrainBitChecker] 開始阻抗檢測...");

        // 重置警告標記
        for (int i = 0; i < channelWarningShown.Length; i++)
        {
            channelWarningShown[i] = false;
        }

        // 開始阻抗數據流（不自動保存到 LabData，因為這只是檢測）
        brainBitManager.StartImpedanceStream(false);

        isImpedanceCheckActive = true;
        needsUIUpdate = true;
    }

    private void StopImpedanceCheck()
    {
        if (brainBitManager?.IsStreamingImpedance == true)
        {
            Debug.Log("[BrainBitChecker] 停止阻抗檢測...");
            brainBitManager.StopImpedanceStream();
        }

        isImpedanceCheckActive = false;
        needsUIUpdate = true;
    }

    private void BacktoMainMenu()
    {
        Debug.Log("[BrainBitChecker] 返回主選單...");

        // 停止所有數據採集
        StopImpedanceCheck();

        // 載入主選單場景
        SceneManager.LoadScene("MainUI");
    }

    #endregion

    #region 協程方法

    private IEnumerator DelayedStartImpedance()
    {
        // 等待連接穩定
        yield return new WaitForSeconds(1.0f);

        if (brainBitManager?.IsConnected == true)
        {
            StartImpedanceCheck();
        }
    }

    private IEnumerator DelayedStartConnection()
    {
        // 等待一下再開始連接
        yield return new WaitForSeconds(0.5f);
        StartConnection();
    }

    #endregion

    #region 公共 API（向後相容性）

    /// <summary>
    /// 獲取指定通道的阻抗數據（向後相容性）
    /// </summary>
    /// <param name="channel">通道索引 (0=T3, 1=T4, 2=O1, 3=O2)</param>
    /// <returns>阻抗值（歐姆）</returns>
    public double GetImpedanceData(int channel)
    {
        if (brainBitManager == null)
        {
            Debug.LogWarning("[BrainBitChecker] BrainBitManager 未初始化");
            return 300000; // 預設高阻抗值
        }

        var impedanceData = brainBitManager.GetLatestImpedanceData();
        if (impedanceData == null || channel < 0 || channel >= impedanceData.ImpedanceValues.Count)
        {
            Debug.LogWarning($"[BrainBitChecker] 無效的通道索引或無阻抗數據: {channel}");
            return 300000;
        }

        return impedanceData.ImpedanceValues[channel];
    }

    /// <summary>
    /// 獲取指定通道的 EEG 數據（向後相容性）
    /// </summary>
    /// <param name="channel">通道索引 (0=T3, 1=T4, 2=O1, 3=O2)</param>
    /// <returns>EEG 值</returns>
    public double GetEegData(int channel)
    {
        if (brainBitManager == null) return 0;

        var eegData = brainBitManager.GetLatestEEGData();
        if (eegData == null || channel < 0 || channel >= eegData.EEGValues.Count)
            return 0;

        return eegData.EEGValues[channel];
    }

    /// <summary>
    /// 連接狀態屬性（向後相容性）
    /// </summary>
    public bool connectionStatus => brainBitManager?.IsConnected ?? false;

    /// <summary>
    /// 開始阻抗測量（向後相容性）
    /// </summary>
    public void StartResistance()
    {
        StartImpedanceCheck();
    }

    /// <summary>
    /// 停止阻抗測量（向後相容性）
    /// </summary>
    public void StopResistance()
    {
        StopImpedanceCheck();
    }

    /// <summary>
    /// 掃描設備（向後相容性）
    /// </summary>
    public void Scanning()
    {
        StartConnection();
    }

    /// <summary>
    /// 獲取阻抗檢測狀態
    /// </summary>
    public bool IsImpedanceCheckActive => isImpedanceCheckActive;

    /// <summary>
    /// 獲取所有通道的阻抗狀態摘要
    /// </summary>
    public string GetImpedanceStatusSummary()
    {
        if (brainBitManager?.IsConnected != true)
            return "設備未連接";

        var impedanceData = brainBitManager.GetLatestImpedanceData();
        if (impedanceData == null)
            return "無阻抗數據";

        return impedanceData.GetImpedanceStatus();
    }

    #endregion 

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }
}
#endregion