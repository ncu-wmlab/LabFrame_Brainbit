using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using LabFrame2023;
using UnityEngine.Events;
using NeuroSDK;
using SignalMath;
using System;

/// <summary>
/// BrainBit 設備管理器
/// 負責管理 BrainBit 設備的連接、數據採集和記錄
/// </summary>
public class BrainBitManager : LabSingleton<BrainBitManager>, IManager
{
    #region Properties
    /// <summary>
    /// 目前是否有與 BrainBit 設備連線
    /// </summary>
    public bool IsConnected { get; private set; } = false;

    /// <summary>
    /// 正在記錄 EEG 數據？
    /// </summary>
    public bool IsStreamingEEG { get; private set; } = false;

    /// <summary>
    /// 正在記錄阻抗數據？
    /// </summary>
    public bool IsStreamingImpedance { get; private set; } = false;

    /// <summary>
    /// 目前連接的設備名稱
    /// </summary>
    public string ConnectedDeviceName { get; private set; } = "";

    /// <summary>
    /// 目前連接的設備地址
    /// </summary>
    public string ConnectedDeviceAddress { get; private set; } = "";

    /// <summary>
    /// 是否正在掃描設備
    /// </summary>
    public bool IsScanning { get; private set; } = false;

    /// <summary>
    /// 正在跑情緒處理？
    /// </summary>
    public bool IsProcessingEmotions { get; private set; } = false;

    /// <summary>
    /// 情緒校正是否已完成？校正完成前 Mind / Spectral 回傳 null
    /// </summary>
    public bool IsEmotionsCalibrated { get; private set; } = false;

    /// <summary>
    /// 情緒校正進度 0-100
    /// </summary>
    public int CalibrationProgress { get; private set; } = 0;
    #endregion

    #region Events
    /// <summary>
    /// EEG 數據更新事件
    /// </summary>
    public event UnityAction<BrainBit_EEGData> OnEEGDataReceived;

    /// <summary>
    /// 阻抗數據更新事件
    /// </summary>
    public event UnityAction<BrainBit_ImpedanceData> OnImpedanceDataReceived;

    /// <summary>
    /// 連接狀態變化事件
    /// </summary>
    public event UnityAction<bool> OnConnectionStatusChanged;

    /// <summary>
    /// 設備掃描完成事件
    /// </summary>
    public event UnityAction<List<SensorInfo>> OnDeviceFound;

    /// <summary>
    /// 錯誤事件
    /// </summary>
    public event UnityAction<string> OnError;

    /// <summary>
    /// 情緒/心智資料更新事件
    /// </summary>
    public event UnityAction<BrainBit_MindData> OnMindDataReceived;

    /// <summary>
    /// 五頻段光譜資料更新事件
    /// </summary>
    public event UnityAction<BrainBit_SpectralData> OnSpectralDataReceived;

    /// <summary>
    /// 情緒校正進度 0-100
    /// </summary>
    public event UnityAction<int> OnCalibrationProgress;

    /// <summary>
    /// 情緒校正完成
    /// </summary>
    public event UnityAction OnCalibrationFinished;

    /// <summary>
    /// 偵測到情緒處理期間的雜訊（sequence 或雙側）
    /// </summary>
    public event UnityAction<bool> OnEmotionsArtifact;
    #endregion

    #region Private Fields
    private BrainBitConfig _config;
    private Scanner _scanner;
    private BrainBitSensor _currentSensor;

    private BrainBit_EEGData _lastEEGData;
    private BrainBit_ImpedanceData _lastImpedanceData;

    private bool _autoWriteEEGData = false;
    private bool _autoWriteImpedanceData = false;

    private Coroutine _connectionMonitorCoroutine;
    private Coroutine _scanTimeoutCoroutine;

    private int _reconnectAttempts = 0;

    // 用於記錄目前 EEG 寫入資料的標籤 (對應不同的儲存檔案)
    private string _currentEEGTag = "eeg";
    // 用於記錄目前阻抗寫入資料的標籤
    private string _currentImpedanceTag = "impedance";

    // 主執行緒派發佇列，用於將背景執行緒的回呼安全地轉移到主執行緒執行
    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

    // === Emotions ===
    private EmotionsController _emotionsController;
    private bool   _autoWriteEmotionData = false;
    private string _currentMindTag       = "mind";
    private string _currentSpectralTag   = "spectral";
    private BrainBit_MindData     _lastMindData;
    private BrainBit_SpectralData _lastSpectralData;
    // 若為 true，代表 EEG 串流是被情緒處理自動啟動的 — StopEmotionsProcessing 時要一起停
    private bool _emotionsStartedEEG = false;
    #endregion

    #region IManager Implementation
    public void ManagerInit()
    {
        LabTools.Log("[BrainBit] Initializing BrainBit Manager...");

        // 載入配置
        _config = LabTools.GetConfig<BrainBitConfig>(true);

        // 初始化數據對象
        _lastEEGData = new BrainBit_EEGData();
        _lastImpedanceData = new BrainBit_ImpedanceData();

        // 開始連接監控
        _connectionMonitorCoroutine = StartCoroutine(MonitorConnection());

        // 自動連接
        if (_config.AutoConnectOnInit)
        {
            StartCoroutine(DelayedAutoConnect());
        }

        LabTools.Log("[BrainBit] Manager initialized successfully");
        LabTools.Log($"[BrainBit] Config - AutoConnect: {_config.AutoConnectOnInit}, ScanTimeout: {_config.ScanTimeoutSeconds}s");
        LabTools.Log($"[BrainBit] Config - AutoSelectBestSignal: {_config.AutoSelectBestSignal}, ConnectDelay: {_config.ConnectDelaySeconds}s");
    }

    /// <summary>
    /// 每幀執行主執行緒佇列中的 Action（處理從背景執行緒派發過來的回呼）
    /// </summary>
    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                LabTools.LogError($"[BrainBit] Error executing main thread action: {e.Message}");
            }
        }
    }

    public IEnumerator ManagerDispose()
    {
        LabTools.Log("[BrainBit] Disposing BrainBit Manager...");

        // 停止所有數據流
        StopEmotionsProcessing();
        StopEEGStream();
        StopImpedanceStream();

        // 停止監控協程
        if (_connectionMonitorCoroutine != null)
        {
            StopCoroutine(_connectionMonitorCoroutine);
            _connectionMonitorCoroutine = null;
        }

        if (_scanTimeoutCoroutine != null)
        {
            StopCoroutine(_scanTimeoutCoroutine);
            _scanTimeoutCoroutine = null;
        }

        // 斷開連接
        try
        {
            Disconnect();
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Error during disconnect: {e.Message}");
        }

        // 清理資源
        CleanupResources();

        LabTools.Log("[BrainBit] Manager disposed successfully");
        yield return null;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// 開始掃描 BrainBit 設備
    /// </summary>
    public void StartScan()
    {
        if (IsScanning)
        {
            LabTools.LogError("[BrainBit] Already scanning for devices");
            return;
        }

        try
        {
            LabTools.Log("[BrainBit] Starting device scan...");

            // 重用 Scanner，避免重複建立造成 native 資源洩漏
            if (_scanner == null)
            {
                _scanner = new Scanner(SensorFamily.SensorLEBrainBit);
            }
            _scanner.EventSensorsChanged += OnSensorsFound;

            // 開始掃描
            _scanner.Start();

            IsScanning = true;
            LabTools.Log("[BrainBit] Scanner started successfully");

            // 設置掃描超時
            _scanTimeoutCoroutine = StartCoroutine(ScanTimeout());

            // 增加調試信息
            StartCoroutine(DebugScanStatus());
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Failed to start scan: {e.Message}");
            OnError?.Invoke($"Scan failed: {e.Message}");
            IsScanning = false;
        }
    }

    /// <summary>
    /// 調試掃描狀態
    /// </summary>
    private IEnumerator DebugScanStatus()
    {
        int attempts = 0;
        while (IsScanning && attempts < 10)
        {
            yield return new WaitForSeconds(1f);
            attempts++;

            try
            {
                // 嘗試獲取已發現的設備列表
                var foundDevices = _scanner?.Sensors;
                LabTools.Log($"[BrainBit] Scan attempt {attempts}: Found {foundDevices?.Count ?? 0} devices");

                if (foundDevices != null && foundDevices.Count > 0)
                {
                    foreach (var device in foundDevices)
                    {
                        LabTools.Log($"[BrainBit] Found device: {device.Name} ({device.Address}) RSSI: {device.RSSI}");
                    }
                }
            }
            catch (Exception e)
            {
                LabTools.LogError($"[BrainBit] Error during scan debug: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 停止掃描
    /// </summary>
    public void StopScan()
    {
        if (!IsScanning) return;

        try
        {
            _scanner?.Stop();
            IsScanning = false;

            if (_scanTimeoutCoroutine != null)
            {
                StopCoroutine(_scanTimeoutCoroutine);
                _scanTimeoutCoroutine = null;
                LabTools.Log("[BrainBit] Scan timeout coroutine stopped");
            }

            LabTools.Log("[BrainBit] Device scan stopped");
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Error stopping scan: {e.Message}");
        }
    }

    /// <summary>
    /// 手動連接設備
    /// </summary>
    public void ManualConnect()
    {
        if (IsConnected)
        {
            LabTools.LogError("[BrainBit] Already connected to a device");
            return;
        }

        StartScan();
    }

    /// <summary>
    /// 手動斷開連接
    /// </summary>
    public void ManualDisconnect()
    {
        Disconnect();
    }

    /// <summary>
    /// 開始 EEG 數據流
    /// </summary>
    /// <param name="autoWriteToLabData">是否自動保存到 LabDataManager</param>
    /// <param name="tag">寫入資料的標籤(例如可傳入遊戲階段名稱)</param>
    public void StartEEGStream(bool autoWriteToLabData = true, string tag = "eeg")
    {
        if (!IsConnected)
        {
            LabTools.LogError("[BrainBit] Device not connected");
            OnError?.Invoke("Device not connected");
            return;
        }

        if (IsStreamingEEG)
        {
            LabTools.LogError("[BrainBit] EEG stream already active");
            return;
        }

        try
        {
            _autoWriteEEGData = autoWriteToLabData;
            _currentEEGTag = string.IsNullOrEmpty(tag) ? "eeg" : tag;

            _currentSensor.EventBrainBitSignalDataRecived += OnSignalDataReceived;
            _currentSensor.ExecCommand(SensorCommand.CommandStartSignal);

            IsStreamingEEG = true;
            LabTools.Log($"[BrainBit] EEG stream started with tag: {_currentEEGTag}");

            // 記錄開始事件
            if (LabDataManager.Instance.IsInited)
            {
                var connectionData = new BrainBit_ConnectionData(true, ConnectedDeviceName, ConnectedDeviceAddress, "EEG_Stream_Started");
                LabDataManager.Instance.WriteData(connectionData, "defaultPhase");
            }
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Failed to start EEG stream: {e.Message}");
            OnError?.Invoke($"EEG stream failed: {e.Message}");
        }
    }

    /// <summary>
    /// 停止 EEG 數據流
    /// </summary>
    public void StopEEGStream()
    {
        if (!IsStreamingEEG) return;

        try
        {
            _currentSensor?.ExecCommand(SensorCommand.CommandStopSignal);
            _currentSensor.EventBrainBitSignalDataRecived -= OnSignalDataReceived;

            IsStreamingEEG = false;
            _autoWriteEEGData = false;

            LabTools.Log("[BrainBit] EEG stream stopped");

            // 記錄停止事件
            if (LabDataManager.Instance.IsInited)
            {
                var connectionData = new BrainBit_ConnectionData(true, ConnectedDeviceName, ConnectedDeviceAddress, "EEG_Stream_Stopped");
                LabDataManager.Instance.WriteData(connectionData, "defaultPhase");
            }
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Error stopping EEG stream: {e.Message}");
        }
    }

    /// <summary>
    /// 開始阻抗數據流
    /// </summary>
    /// <param name="autoWriteToLabData">是否自動保存到 LabDataManager</param>
    /// <param name="tag">寫入資料的標籤(例如可傳入遊戲階段名稱)</param>
    public void StartImpedanceStream(bool autoWriteToLabData = true, string tag = "impedance")
    {
        if (!IsConnected)
        {
            LabTools.LogError("[BrainBit] Device not connected");
            OnError?.Invoke("Device not connected");
            return;
        }

        if (IsStreamingImpedance)
        {
            LabTools.LogError("[BrainBit] Impedance stream already active");
            return;
        }

        try
        {
            _autoWriteImpedanceData = autoWriteToLabData;
            _currentImpedanceTag = string.IsNullOrEmpty(tag) ? "impedance" : tag;

            _currentSensor.EventBrainBitResistDataRecived += OnResistanceDataReceived;
            _currentSensor.ExecCommand(SensorCommand.CommandStartResist);

            IsStreamingImpedance = true;
            LabTools.Log($"[BrainBit] Impedance stream started with tag: {_currentImpedanceTag}");

            // 記錄開始事件
            if (LabDataManager.Instance.IsInited)
            {
                var connectionData = new BrainBit_ConnectionData(true, ConnectedDeviceName, ConnectedDeviceAddress, "Impedance_Stream_Started");
                LabDataManager.Instance.WriteData(connectionData, "defaultPhase");
            }
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Failed to start impedance stream: {e.Message}");
            OnError?.Invoke($"Impedance stream failed: {e.Message}");
        }
    }

    /// <summary>
    /// 停止阻抗數據流
    /// </summary>
    public void StopImpedanceStream()
    {
        if (!IsStreamingImpedance) return;

        try
        {
            _currentSensor?.ExecCommand(SensorCommand.CommandStopResist);
            _currentSensor.EventBrainBitResistDataRecived -= OnResistanceDataReceived;

            IsStreamingImpedance = false;
            _autoWriteImpedanceData = false;

            LabTools.Log("[BrainBit] Impedance stream stopped");

            // 記錄停止事件
            if (LabDataManager.Instance.IsInited)
            {
                var connectionData = new BrainBit_ConnectionData(true, ConnectedDeviceName, ConnectedDeviceAddress, "Impedance_Stream_Stopped");
                LabDataManager.Instance.WriteData(connectionData, "connection");
            }
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Error stopping impedance stream: {e.Message}");
        }
    }

    /// <summary>
    /// 獲取最新的 EEG 數據
    /// </summary>
    public BrainBit_EEGData GetLatestEEGData()
    {
        return _lastEEGData;
    }

    /// <summary>
    /// 動態更改 EEG 寫入資料的 Tag（例如在不停止數據流的情況下，切換遊戲階段）
    /// </summary>
    /// <param name="tag">新的標籤名稱</param>
    public void SetEEGTag(string tag)
    {
        _currentEEGTag = string.IsNullOrEmpty(tag) ? "eeg" : tag;
        LabTools.Log($"[BrainBit] EEG data tag dynamically changed to: {_currentEEGTag}");
    }

    /// <summary>
    /// 獲取最新的阻抗數據
    /// </summary>
    public BrainBit_ImpedanceData GetLatestImpedanceData()
    {
        return _lastImpedanceData;
    }

    /// <summary>
    /// 動態更改阻抗寫入資料的 Tag
    /// </summary>
    /// <param name="tag">新的標籤名稱</param>
    public void SetImpedanceTag(string tag)
    {
        _currentImpedanceTag = string.IsNullOrEmpty(tag) ? "impedance" : tag;
        LabTools.Log($"[BrainBit] Impedance data tag dynamically changed to: {_currentImpedanceTag}");
    }
    #endregion

    #region Private Methods
    private IEnumerator DelayedAutoConnect()
    {
        yield return new WaitForSeconds(1.0f);
        ManualConnect();
    }

    private IEnumerator MonitorConnection()
    {
        bool lastConnectionStatus = false;

        while (true)
        {
            // 檢查連接狀態
            bool currentStatus = _currentSensor != null && _currentSensor.State == SensorState.StateInRange;

            if (currentStatus != lastConnectionStatus)
            {
                IsConnected = currentStatus;
                OnConnectionStatusChanged?.Invoke(IsConnected);

                if (!IsConnected && lastConnectionStatus)
                {
                    // 斷線處理
                    HandleDisconnection();
                }

                lastConnectionStatus = currentStatus;
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    private IEnumerator ScanTimeout()
    {
        LabTools.Log($"[BrainBit] Scan timeout set for {_config.ScanTimeoutSeconds} seconds");
        yield return new WaitForSeconds(_config.ScanTimeoutSeconds);

        if (_scanTimeoutCoroutine != null && _currentSensor == null)
        {
            StopScan();
            LabTools.LogError("[BrainBit] Scan timeout - no devices found");
            OnError?.Invoke("Scan timeout - no devices found");
        }
        else
        {
            LabTools.Log("[BrainBit] Scan completed before timeout");
        }
        _scanTimeoutCoroutine = null;
    }

    private void OnSensorsFound(IScanner scanner, IReadOnlyList<SensorInfo> sensors)
    {
        // 此回呼由 NeuroSDK 在背景執行緒觸發！
        // 必須將所有 Unity API 呼叫（StartCoroutine、StopCoroutine 等）派發到主執行緒
        try
        {
            LabTools.Log($"[BrainBit] Found {sensors.Count} device(s) (background thread)");

            if (sensors.Count > 0)
            {
                // 選擇要連接的設備（純邏輯，不涉及 Unity API，可以在背景執行緒執行）
                SensorInfo targetSensor = SelectBestDevice(sensors);
                LabTools.Log($"[BrainBit] Selected device: {targetSensor.Name} (RSSI: {targetSensor.RSSI})");

                // 先在背景緒停止 Scanner（SDK 層級，非 Unity API）
                _scanner?.Stop();
                _scanner.EventSensorsChanged -= OnSensorsFound;

                // 將剩下的操作排入主執行緒執行
                _mainThreadActions.Enqueue(() =>
                {
                    IsScanning = false;

                    // 觸發設備發現事件
                    OnDeviceFound?.Invoke(new List<SensorInfo>(sensors));

                    if (_scanTimeoutCoroutine != null)
                    {
                        StopCoroutine(_scanTimeoutCoroutine);
                        _scanTimeoutCoroutine = null;
                        LabTools.Log("[BrainBit] Scan timeout coroutine stopped");
                    }

                    // 等待一段時間後建立連接（確保掃描完全停止）
                    StartCoroutine(DelayedConnect(targetSensor));
                });
            }
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Error in OnSensorsFound: {e.Message}");
            _mainThreadActions.Enqueue(() => OnError?.Invoke($"Device discovery error: {e.Message}"));
        }
    }

    /// <summary>
    /// 選擇最佳設備
    /// </summary>
    /// <param name="sensors">發現的設備列表</param>
    /// <returns>選中的設備</returns>
    private SensorInfo SelectBestDevice(IReadOnlyList<SensorInfo> sensors)
    {
        if (sensors.Count == 1)
        {
            return sensors[0];
        }

        // 如果配置為自動選擇信號最強的設備
        if (_config.AutoSelectBestSignal)
        {
            SensorInfo bestDevice = sensors[0];
            foreach (var sensor in sensors)
            {
                LabTools.Log($"[BrainBit] Device: {sensor.Name}, RSSI: {sensor.RSSI} dBm");

                // RSSI 值越高（越接近 0）信號越強
                if (sensor.RSSI > bestDevice.RSSI)
                {
                    bestDevice = sensor;
                }
            }

            LabTools.Log($"[BrainBit] Best signal device: {bestDevice.Name} (RSSI: {bestDevice.RSSI} dBm)");
            return bestDevice;
        }
        else
        {
            // 選擇第一個設備
            return sensors[0];
        }
    }

    /// <summary>
    /// 延遲連接設備
    /// </summary>
    /// <param name="sensorInfo">設備信息</param>
    private IEnumerator DelayedConnect(SensorInfo sensorInfo)
    {
        LabTools.Log($"[BrainBit] Waiting {_config.ConnectDelaySeconds}s before connecting...");

        // 等待指定時間，確保掃描完全停止
        yield return new WaitForSeconds(_config.ConnectDelaySeconds);

        // 建立連接
        ConnectToDevice(sensorInfo);
    }

    private void ConnectToDevice(SensorInfo sensorInfo)
    {
        try
        {
            LabTools.Log($"[BrainBit] Connecting to device: {sensorInfo.Name} ({sensorInfo.Address})");

            // 清理舊的 Sensor（如果有的話），避免 native 資源洩漏
            if (_currentSensor != null)
            {
                try { _currentSensor.Dispose(); } catch (Exception) { }
                _currentSensor = null;
            }

            // 創建 Sensor（此時掃描已停止）
            _currentSensor = _scanner.CreateSensor(sensorInfo) as BrainBitSensor;

            if (_currentSensor != null)
            {
                LabTools.Log("[BrainBit] Sensor created successfully, attempting connection...");

                // 連接設備
                _currentSensor.Connect();

                // 設置連接信息
                ConnectedDeviceName = sensorInfo.Name;
                ConnectedDeviceAddress = sensorInfo.Address;

                LabTools.Log($"[BrainBit] Successfully connected to {ConnectedDeviceName}");

                // 記錄連接事件
                if (LabDataManager.Instance.IsInited)
                {
                    var connectionData = new BrainBit_ConnectionData(true, ConnectedDeviceName, ConnectedDeviceAddress, "Connected");
                    LabDataManager.Instance.WriteData(connectionData, "connection");
                }

                _reconnectAttempts = 0;
            }
            else
            {
                LabTools.LogError("[BrainBit] Failed to create sensor object - CreateSensor returned null");
                OnError?.Invoke("Failed to create sensor object");
            }
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Connection failed: {e.Message}");
            OnError?.Invoke($"Connection failed: {e.Message}");
        }
    }

    private void Disconnect()
    {
        try
        {
            if (IsConnected)
            {
                // 停止所有數據流
                StopEmotionsProcessing();
                StopEEGStream();
                StopImpedanceStream();

                // 記錄斷開事件
                if (LabDataManager.Instance.IsInited)
                {
                    var connectionData = new BrainBit_ConnectionData(false, ConnectedDeviceName, ConnectedDeviceAddress, "Disconnected");
                    LabDataManager.Instance.WriteData(connectionData, "connection");
                }

                LabTools.Log($"[BrainBit] Disconnected from {ConnectedDeviceName}");
            }

            _currentSensor = null;
            IsConnected = false;
            ConnectedDeviceName = "";
            ConnectedDeviceAddress = "";

            OnConnectionStatusChanged?.Invoke(false);
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Error during disconnect: {e.Message}");
        }
    }

    private void HandleDisconnection()
    {
        LabTools.LogError($"[BrainBit] Device disconnected: {ConnectedDeviceName}");

        if (_config.DisconnectNotification)
        {
            LabPromptBox.Show($"BrainBit 設備已斷線！\nDevice {ConnectedDeviceName} disconnected!");
        }

        // 嘗試重連
        if (_config.AutoReconnectAttempts > 0 && _reconnectAttempts < _config.AutoReconnectAttempts)
        {
            StartCoroutine(AttemptReconnect());
        }

        Disconnect();
    }

    private IEnumerator AttemptReconnect()
    {
        _reconnectAttempts++;
        LabTools.Log($"[BrainBit] Attempting reconnection ({_reconnectAttempts}/{_config.AutoReconnectAttempts})...");

        yield return new WaitForSeconds(_config.ReconnectIntervalSeconds);

        if (!IsConnected)
        {
            StartScan();
        }
    }

    private void OnSignalDataReceived(ISensor sensor, BrainBitSignalData[] data)
    {
        // 此回呼由 NeuroSDK 在背景執行緒觸發！
        try
        {
            foreach (var packet in data)
            {
                var eegData = new BrainBit_EEGData(packet.T3, packet.T4, packet.O1, packet.O2);
                _lastEEGData = eegData;

                // 將事件觸發和資料寫入排入主執行緒
                _mainThreadActions.Enqueue(() =>
                {
                    // 觸發事件（訂閱者可能更新 UI）
                    OnEEGDataReceived?.Invoke(eegData);

                    // 自動保存到 LabDataManager
                    if (_autoWriteEEGData && LabDataManager.Instance.IsInited)
                    {
                        LabDataManager.Instance.WriteData(eegData, _currentEEGTag);
                    }
                });
            }

            // 情緒處理（寄生在同一條 NeuroSDK 背景緒，不開額外 thread）
            if (IsProcessingEmotions)
            {
                _emotionsController?.ProcessData(data);
            }
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Error processing EEG data: {e.Message}");
        }
    }

    private void OnResistanceDataReceived(ISensor sensor, BrainBitResistData data)
    {
        // 此回呼由 NeuroSDK 在背景執行緒觸發！
        try
        {
            var impedanceData = new BrainBit_ImpedanceData(data.T3, data.T4, data.O1, data.O2);
            _lastImpedanceData = impedanceData;

            // 將事件觸發和資料寫入排入主執行緒
            _mainThreadActions.Enqueue(() =>
            {
                // 觸發事件（訂閱者可能更新 UI）
                OnImpedanceDataReceived?.Invoke(impedanceData);

                // 檢查阻抗警告: 當有任一通道阻抗超過 200,000 時
                if (!impedanceData.IsImpedanceGood)
                {
                    LabTools.LogError($"[BrainBit] High impedance detected: {impedanceData.GetImpedanceStatus()}");
                }

                // 自動保存到 LabDataManager
                if (_autoWriteImpedanceData && LabDataManager.Instance.IsInited)
                {
                    LabDataManager.Instance.WriteData(impedanceData, _currentImpedanceTag);
                }
            });
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Error processing impedance data: {e.Message}");
        }
    }

    private void CleanupResources()
    {
        try
        {
            // 清理掃描器
            if (_scanner != null)
            {
                _scanner.EventSensorsChanged -= OnSensorsFound;
                _scanner.Stop();
                _scanner = null;
            }

            // 清理傳感器
            if (_currentSensor != null)
            {
                _currentSensor.EventBrainBitSignalDataRecived -= OnSignalDataReceived;
                _currentSensor.EventBrainBitResistDataRecived -= OnResistanceDataReceived;
                _currentSensor = null;
            }

            // 清理情緒控制器
            if (_emotionsController != null)
            {
                UnwireEmotionsCallbacks();
                _emotionsController.Dispose();
                _emotionsController = null;
            }
            IsProcessingEmotions = false;
            IsEmotionsCalibrated = false;

            // 重置狀態
            IsConnected = false;
            IsStreamingEEG = false;
            IsStreamingImpedance = false;
            IsScanning = false;

            LabTools.Log("[BrainBit] Resources cleaned up");
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Error during cleanup: {e.Message}");
        }
    }
    #endregion

    #region Emotions Processing

    /// <summary>
    /// 啟動情緒處理（MindData / SpectralData / 校正進度）。
    /// 若 EEG 串流未啟動，會自動啟動；呼叫 StopEmotionsProcessing 時會同步停止該 EEG 串流。
    /// </summary>
    public void StartEmotionsProcessing(bool autoWriteToLabData = true,
                                        string mindTag = "mind",
                                        string spectralTag = "spectral")
    {
        if (!IsConnected)
        {
            LabTools.LogError("[BrainBit] Device not connected");
            OnError?.Invoke("Device not connected");
            return;
        }

        if (IsProcessingEmotions)
        {
            LabTools.LogError("[BrainBit] Emotions processing already active");
            return;
        }

        try
        {
            _autoWriteEmotionData = autoWriteToLabData;
            _currentMindTag       = string.IsNullOrEmpty(mindTag) ? "mind" : mindTag;
            _currentSpectralTag   = string.IsNullOrEmpty(spectralTag) ? "spectral" : spectralTag;

            // 若 EEG 未啟動，自動啟動並記錄
            if (!IsStreamingEEG)
            {
                StartEEGStream(autoWriteToLabData: false);
                _emotionsStartedEEG = true;
            }
            else
            {
                _emotionsStartedEEG = false;
            }

            // 建立 / 重建 controller（套用目前 BrainBitConfig）
            _emotionsController?.Dispose();
            _emotionsController = new EmotionsController(_config);
            WireEmotionsCallbacks();

            // 校正狀態重置
            IsEmotionsCalibrated = false;
            CalibrationProgress  = 0;
            _lastMindData        = null;
            _lastSpectralData    = null;

            _emotionsController.StartCalibration();
            IsProcessingEmotions = true;

            LabTools.Log($"[BrainBit] Emotions processing started (mindTag: {_currentMindTag}, spectralTag: {_currentSpectralTag})");
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Failed to start emotions processing: {e.Message}");
            OnError?.Invoke($"Emotions processing failed: {e.Message}");
        }
    }

    /// <summary>
    /// 停止情緒處理。若本次 EEG 串流是由情緒處理自動啟動的，同步停止該 EEG 串流；
    /// 否則保留 EEG 串流（避免誤停使用者正在錄的 EEG）。
    /// </summary>
    public void StopEmotionsProcessing()
    {
        if (!IsProcessingEmotions) return;

        try
        {
            IsProcessingEmotions = false;

            if (_emotionsController != null)
            {
                UnwireEmotionsCallbacks();
                _emotionsController.Dispose();
                _emotionsController = null;
            }

            _autoWriteEmotionData = false;
            IsEmotionsCalibrated  = false;
            CalibrationProgress   = 0;

            if (_emotionsStartedEEG && IsStreamingEEG)
            {
                StopEEGStream();
            }
            _emotionsStartedEEG = false;

            LabTools.Log("[BrainBit] Emotions processing stopped");
        }
        catch (Exception e)
        {
            LabTools.LogError($"[BrainBit] Error stopping emotions processing: {e.Message}");
        }
    }

    /// <summary>
    /// 重新校正（例如換受測者、中途摘下又戴回）。
    /// </summary>
    public void RestartCalibration()
    {
        if (!IsProcessingEmotions || _emotionsController == null)
        {
            LabTools.LogError("[BrainBit] Cannot restart calibration - emotions processing not active");
            return;
        }

        IsEmotionsCalibrated = false;
        CalibrationProgress  = 0;
        _emotionsController.StartCalibration();

        LabTools.Log("[BrainBit] Emotion calibration restarted");
    }

    /// <summary>
    /// 動態切換 MindData 寫入 Tag
    /// </summary>
    public void SetMindTag(string tag)
    {
        _currentMindTag = string.IsNullOrEmpty(tag) ? "mind" : tag;
        LabTools.Log($"[BrainBit] Mind data tag dynamically changed to: {_currentMindTag}");
    }

    /// <summary>
    /// 動態切換 SpectralData 寫入 Tag
    /// </summary>
    public void SetSpectralTag(string tag)
    {
        _currentSpectralTag = string.IsNullOrEmpty(tag) ? "spectral" : tag;
        LabTools.Log($"[BrainBit] Spectral data tag dynamically changed to: {_currentSpectralTag}");
    }

    /// <summary>
    /// 取得最新的情緒/心智資料。校正完成前回傳 null。
    /// </summary>
    public BrainBit_MindData GetLatestMindData() => _lastMindData;

    /// <summary>
    /// 取得最新的五頻段光譜資料。校正完成前回傳 null。
    /// </summary>
    public BrainBit_SpectralData GetLatestSpectralData() => _lastSpectralData;

    private void WireEmotionsCallbacks()
    {
        if (_emotionsController == null) return;

        _emotionsController.progressCalibrationCallback      = OnEmotionsCalibrationProgress;
        _emotionsController.lastMindDataCallback             = OnRawMindDataReceived;
        _emotionsController.lastSpectralDataCallback         = OnRawSpectralDataReceived;
        _emotionsController.isArtefactedSequenceCallback     = OnEmotionsArtifactDetected;
        _emotionsController.isBothSidesArtifactedCallback    = OnEmotionsArtifactDetected;
    }

    private void UnwireEmotionsCallbacks()
    {
        if (_emotionsController == null) return;

        _emotionsController.progressCalibrationCallback      = null;
        _emotionsController.lastMindDataCallback             = null;
        _emotionsController.lastSpectralDataCallback         = null;
        _emotionsController.isArtefactedSequenceCallback     = null;
        _emotionsController.isBothSidesArtifactedCallback    = null;
    }

    private void OnEmotionsCalibrationProgress(int progress)
    {
        // 這些 callback 由 NeuroSDK/EmotionsController 在背景緒觸發
        _mainThreadActions.Enqueue(() =>
        {
            CalibrationProgress = progress;
            OnCalibrationProgress?.Invoke(progress);

            if (progress >= 100 && !IsEmotionsCalibrated)
            {
                IsEmotionsCalibrated = true;
                OnCalibrationFinished?.Invoke();
                LabTools.Log("[BrainBit] Emotion calibration finished");
            }
        });
    }

    private void OnRawMindDataReceived(MindData raw)
    {
        var wrapped = new BrainBit_MindData(raw);
        _lastMindData = wrapped;

        _mainThreadActions.Enqueue(() =>
        {
            OnMindDataReceived?.Invoke(wrapped);

            if (_autoWriteEmotionData && LabDataManager.Instance.IsInited)
            {
                LabDataManager.Instance.WriteData(wrapped, _currentMindTag);
            }
        });
    }

    private void OnRawSpectralDataReceived(SpectralDataPercents raw)
    {
        var wrapped = new BrainBit_SpectralData(raw);
        _lastSpectralData = wrapped;

        _mainThreadActions.Enqueue(() =>
        {
            OnSpectralDataReceived?.Invoke(wrapped);

            if (_autoWriteEmotionData && LabDataManager.Instance.IsInited)
            {
                LabDataManager.Instance.WriteData(wrapped, _currentSpectralTag);
            }
        });
    }

    private void OnEmotionsArtifactDetected(bool hasArtifact)
    {
        _mainThreadActions.Enqueue(() => OnEmotionsArtifact?.Invoke(hasArtifact));
    }

    #endregion
}