using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using LabFrame2023;
using System;
using SignalMath;

/// <summary>
/// BrainBit EEG 數據
/// </summary>
[Serializable]
public class BrainBit_EEGData : LabDataBase
{
    /// <summary>
    /// T3 通道 EEG 值
    /// </summary>
    public double T3;

    /// <summary>
    /// T4 通道 EEG 值
    /// </summary>
    public double T4;

    /// <summary>
    /// O1 通道 EEG 值
    /// </summary>
    public double O1;

    /// <summary>
    /// O2 通道 EEG 值
    /// </summary>
    public double O2;

    /// <summary>
    /// 所有 EEG 值的列表 (T3, T4, O1, O2)
    /// </summary>
    public List<double> EEGValues => new List<double>() { T3, T4, O1, O2 };

    public BrainBit_EEGData() : base() { }

    public BrainBit_EEGData(double t3, double t4, double o1, double o2) : base()
    {
        T3 = t3;
        T4 = t4;
        O1 = o1;
        O2 = o2;
    }

    public override string ToString()
    {
        return $"T3: {T3:N4} | T4: {T4:N4} | O1: {O1:N4} | O2: {O2:N4}";
    }
}

/// <summary>
/// BrainBit 阻抗數據
/// </summary>
[Serializable]
public class BrainBit_ImpedanceData : LabDataBase
{
    /// <summary>
    /// T3 通道阻抗值
    /// </summary>
    public double T3;

    /// <summary>
    /// T4 通道阻抗值
    /// </summary>
    public double T4;

    /// <summary>
    /// O1 通道阻抗值
    /// </summary>
    public double O1;

    /// <summary>
    /// O2 通道阻抗值
    /// </summary>
    public double O2;

    /// <summary>
    /// 所有阻抗值的列表 (T3, T4, O1, O2)
    /// </summary>
    public List<double> ImpedanceValues => new List<double>() { T3, T4, O1, O2 };

    public BrainBit_ImpedanceData() : base() { }

    public BrainBit_ImpedanceData(double t3, double t4, double o1, double o2) : base()
    {
        T3 = t3;
        T4 = t4;
        O1 = o1;
        O2 = o2;
    }

    /// <summary>
    /// 檢查所有通道阻抗是否皆正常(小於閾值)
    /// </summary>
    public bool IsImpedanceGood
    {
        get
        {
            double threshold = 200000.0;
            return T3 < threshold && T4 < threshold && O1 < threshold && O2 < threshold;
        }
    }

    /// <summary>
    /// 一次取得各通道數值以及各通道是否阻抗過高(大於200,000)
    /// </summary>
    /// <returns>(T3數值, T3太高?, T4數值, T4太高?, O1數值, O1太高?, O2數值, O2太高?)</returns>
    public (double t3_val, bool t3_high, double t4_val, bool t4_high, double o1_val, bool o1_high, double o2_val, bool o2_high) GetImpedanceValues()
    {
        double threshold = 200000.0;
        return (
            T3, T3 > threshold,
            T4, T4 > threshold,
            O1, O1 > threshold,
            O2, O2 > threshold
        );
    }

    /// <summary>
    /// 獲取阻抗狀態描述
    /// </summary>
    /// <param name="threshold">阻抗警告閾值</param>
    /// <returns>阻抗狀態字符串</returns>
    public string GetImpedanceStatus(double threshold = 200000.0)
    {
        var status = new List<string>();
        if (T3 > threshold) status.Add("T3");
        if (T4 > threshold) status.Add("T4");
        if (O1 > threshold) status.Add("O1");
        if (O2 > threshold) status.Add("O2");

        if (status.Count == 0)
            return "All channels good";
        else
            return $"High impedance: {string.Join(", ", status)}";
    }

    public override string ToString()
    {
        return $"T3: {T3:N0}Ω | T4: {T4:N0}Ω | O1: {O1:N0}Ω | O2: {O2:N0}Ω";
    }
}

/// <summary>
/// BrainBit 連接狀態數據
/// </summary>
[Serializable]
public class BrainBit_ConnectionData : LabDataBase
{
    /// <summary>
    /// 連接狀態
    /// </summary>
    public bool IsConnected;

    /// <summary>
    /// 設備名稱
    /// </summary>
    public string DeviceName;

    /// <summary>
    /// 設備地址
    /// </summary>
    public string DeviceAddress;

    /// <summary>
    /// 連接/斷開時間戳
    /// </summary>
    public string EventType; // "Connected" or "Disconnected"

    public BrainBit_ConnectionData() : base() { }

    public BrainBit_ConnectionData(bool isConnected, string deviceName, string deviceAddress, string eventType) : base()
    {
        IsConnected = isConnected;
        DeviceName = deviceName;
        DeviceAddress = deviceAddress;
        EventType = eventType;
    }

    public override string ToString()
    {
        return $"{EventType}: {DeviceName} ({DeviceAddress}) - Connected: {IsConnected}";
    }
}

/// <summary>
/// BrainBit 情緒/心智狀態數據（專注、放鬆）
/// </summary>
[Serializable]
public class BrainBit_MindData : LabDataBase
{
    /// <summary>相對專注度（平滑值，0-100，建議使用）</summary>
    public double Attention;

    /// <summary>相對放鬆度（平滑值，0-100，建議使用）</summary>
    public double Relaxation;

    /// <summary>瞬時專注度（抖動大，進階用）</summary>
    public double InstAttention;

    /// <summary>瞬時放鬆度（抖動大，進階用）</summary>
    public double InstRelaxation;

    public BrainBit_MindData() : base() { }

    public BrainBit_MindData(MindData raw) : base()
    {
        Attention      = raw.RelAttention;
        Relaxation     = raw.RelRelaxation;
        InstAttention  = raw.InstAttention;
        InstRelaxation = raw.InstRelaxation;
    }

    public override string ToString()
        => $"Attention: {Attention:F1} | Relaxation: {Relaxation:F1}";
}

/// <summary>
/// BrainBit 五頻段光譜百分比
/// </summary>
[Serializable]
public class BrainBit_SpectralData : LabDataBase
{
    /// <summary>δ 波（深度放鬆 / 睡眠）</summary>
    public double Delta;

    /// <summary>θ 波（冥想 / 創意）</summary>
    public double Theta;

    /// <summary>α 波（放鬆清醒）</summary>
    public double Alpha;

    /// <summary>β 波（專注思考）</summary>
    public double Beta;

    /// <summary>γ 波（高階認知）</summary>
    public double Gamma;

    public BrainBit_SpectralData() : base() { }

    public BrainBit_SpectralData(SpectralDataPercents raw) : base()
    {
        Delta = raw.Delta;
        Theta = raw.Theta;
        Alpha = raw.Alpha;
        Beta  = raw.Beta;
        Gamma = raw.Gamma;
    }

    public override string ToString()
        => $"δ:{Delta:F1} θ:{Theta:F1} α:{Alpha:F1} β:{Beta:F1} γ:{Gamma:F1}";
}