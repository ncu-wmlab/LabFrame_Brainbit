using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// BrainBit 設備配置
/// </summary>
public class BrainBitConfig
{
    /// <summary>
    /// 啟動時自動開啟連線
    /// </summary>
    public bool AutoConnectOnInit = false;

    /// <summary>
    /// 是否顯示斷線通知
    /// </summary>
    public bool DisconnectNotification = true;

    /// <summary>
    /// 掃描設備的超時時間（秒）
    /// </summary>
    public float ScanTimeoutSeconds = 10.0f;

    /// <summary>
    /// 阻抗警告閾值（歐姆）
    /// 超過此值會顯示警告
    /// </summary>
    public double ImpedanceWarningThreshold = 200000.0;

    /// <summary>
    /// 自動重連嘗試次數
    /// </summary>
    public int AutoReconnectAttempts = 3;

    /// <summary>
    /// 重連間隔時間（秒）
    /// </summary>
    public float ReconnectIntervalSeconds = 2.0f;

    /// <summary>
    /// 自動選擇信號最強的設備
    /// </summary>
    public bool AutoSelectBestSignal = false;

    /// <summary>
    /// 連接前等待時間（秒）
    /// 停止掃描後等待一段時間再建立連接
    /// </summary>
    public float ConnectDelaySeconds = 1.0f;
}