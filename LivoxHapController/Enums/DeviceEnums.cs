#if NET45_OR_GREATER
using System;
#endif

namespace LivoxHapController.Enums
{
    /// <summary>
    /// 工作模式枚举
    /// 对应C++ LivoxLidarWorkMode
    /// 用于设置雷达的目标工作状态
    /// </summary>
    public enum WorkMode : byte
    {
        /// <summary>正常工作模式 (0x01)</summary>
        Normal = 0x01,

        /// <summary>唤醒模式 (0x02)</summary>
        WakeUp = 0x02,

        /// <summary>休眠模式 (0x03)</summary>
        Sleep = 0x03,

        /// <summary>错误状态 (0x04)</summary>
        Error = 0x04,

        /// <summary>上电自检 (0x05)</summary>
        PowerOnSelfTest = 0x05,

        /// <summary>电机启动中 (0x06)</summary>
        MotorStarting = 0x06,

        /// <summary>电机停止中 (0x07)</summary>
        MotorStopping = 0x07,

        /// <summary>升级中 (0x08)</summary>
        Upgrade = 0x08
    }

    /// <summary>
    /// 开机工作模式枚举
    /// 对应C++ LivoxLidarWorkModeAfterBoot
    /// 用于配置设备开机后自动进入的工作模式
    /// </summary>
    public enum WorkModeAfterBoot : byte
    {
        /// <summary>默认模式 (0x00)</summary>
        Default = 0x00,

        /// <summary>正常工作 (0x01)</summary>
        Normal = 0x01,

        /// <summary>唤醒模式 (0x02)</summary>
        WakeUp = 0x02
    }

    /// <summary>
    /// 扫描模式枚举
    /// 对应C++ LivoxLidarScanPattern
    /// </summary>
    public enum ScanPattern : byte
    {
        /// <summary>非重复扫描 (0x00)</summary>
        NoneRepetitive = 0x00,

        /// <summary>重复扫描 (0x01)</summary>
        Repetitive = 0x01,

        /// <summary>重复扫描低帧率 (0x02)</summary>
        RepetitiveLowFrameRate = 0x02
    }

    /// <summary>
    /// 设备类型枚举
    /// 对应C++ LivoxLidarDeviceType
    /// 定义所有Livox LiDAR设备型号
    /// 
    /// 支持状态说明：
    /// - [已支持] 基础库有完整的配置、连接、命令控制、数据接收逻辑
    /// - [协议预留] 枚举值按协议定义保留，但基础库暂无专用逻辑
    /// </summary>
    public enum DeviceType : byte
    {
        /// <summary>Hub设备 (0) — [协议预留] 集线器，多雷达汇聚设备</summary>
        Hub = 0,

        /// <summary>Mid40 (1) — [协议预留] Mid-40 激光雷达</summary>
        Mid40 = 1,

        /// <summary>Tele (2) — [协议预留] Tele 远距激光雷达</summary>
        Tele = 2,

        /// <summary>Horizon (3) — [协议预留] Horizon 地平线雷达</summary>
        Horizon = 3,

        /// <summary>Mid70 (6) — [协议预留] Mid-70 激光雷达</summary>
        Mid70 = 6,

        /// <summary>Avia (7) — [协议预留] Avia 天空端雷达</summary>
        Avia = 7,

        /// <summary>Mid360 (9) — [已支持] Mid-360 激光雷达，有独立配置项 Mid360Config</summary>
        Mid360 = 9,

        /// <summary>IndustrialHAP (10) — [协议预留] 工业级HAP</summary>
        IndustrialHAP = 10,

        /// <summary>HAP (15) — [已支持] HAP 激光雷达，有独立配置项 HapConfig</summary>
        HAP = 15,

        /// <summary>PA (16) — [协议预留] PA 雷达</summary>
        PA = 16
    }

    /// <summary>
    /// DeviceType 枚举的扩展方法类
    /// 提供设备类型的显示名称获取和基础库支持状态判断功能
    /// 消除 LidarDeviceInfo、DeviceDiscoveredEventArgs 中重复的 switch 映射
    /// </summary>
    public static class DeviceTypeExtensions
    {
        /// <summary>
        /// 获取设备类型的显示名称
        /// 用于在UI和日志中显示可读的设备型号名称
        /// </summary>
        /// <param name="deviceType">设备类型枚举值</param>
        /// <returns>设备型号的显示名称，未知值返回 "Unknown(值)" 格式</returns>
        public static string GetDisplayName(this DeviceType deviceType)
        {
#if NET45_OR_GREATER
            switch (deviceType)
            {
                case DeviceType.Hub: return "Hub";
                case DeviceType.Mid40: return "Mid40";
                case DeviceType.Tele: return "Tele";
                case DeviceType.Horizon: return "Horizon";
                case DeviceType.Mid70: return "Mid70";
                case DeviceType.Avia: return "Avia";
                case DeviceType.Mid360: return "Mid360";
                case DeviceType.IndustrialHAP: return "IndustrialHAP";
                case DeviceType.HAP: return "HAP";
                case DeviceType.PA: return "PA";
                default: return string.Format("Unknown({0})", (byte)deviceType);
            }
#elif NET9_0_OR_GREATER
            return deviceType switch
            {
                DeviceType.Hub => "Hub",
                DeviceType.Mid40 => "Mid40",
                DeviceType.Tele => "Tele",
                DeviceType.Horizon => "Horizon",
                DeviceType.Mid70 => "Mid70",
                DeviceType.Avia => "Avia",
                DeviceType.Mid360 => "Mid360",
                DeviceType.IndustrialHAP => "IndustrialHAP",
                DeviceType.HAP => "HAP",
                DeviceType.PA => "PA",
                _ => string.Format("Unknown({0})", deviceType),
            };
#endif
            //switch (deviceType)
            //{
            //    case DeviceType.Hub: return "Hub";
            //    case DeviceType.Mid40: return "Mid40";
            //    case DeviceType.Tele: return "Tele";
            //    case DeviceType.Horizon: return "Horizon";
            //    case DeviceType.Mid70: return "Mid70";
            //    case DeviceType.Avia: return "Avia";
            //    case DeviceType.Mid360: return "Mid360";
            //    case DeviceType.IndustrialHAP: return "IndustrialHAP";
            //    case DeviceType.HAP: return "HAP";
            //    case DeviceType.PA: return "PA";
            //    default: return string.Format("Unknown({0})", (byte)deviceType);
            //}
        }

        /// <summary>
        /// 判断设备类型是否在基础库中被完整支持
        /// 已支持的设备拥有独立的配置项（HapConfig / Mid360Config）和完整的连接、命令、数据接收逻辑
        /// </summary>
        /// <param name="deviceType">设备类型枚举值</param>
        /// <returns>true 表示已支持，false 表示仅协议预留</returns>
        public static bool IsSupported(this DeviceType deviceType)
        {
#if NET45_OR_GREATER
            switch (deviceType)
            {
                case DeviceType.HAP:
                case DeviceType.Mid360:
                    return true;
                default:
                    return false;
            }
#elif NET9_0_OR_GREATER
            return deviceType switch
            {
                DeviceType.HAP or DeviceType.Mid360 => true,
                _ => false,
            };
#endif
        }

        /// <summary>
        /// 将字节值安全地解析为 DeviceType 枚举
        /// 若字节值不是已定义的枚举值，返回 null
        /// </summary>
        /// <param name="deviceTypeByte">协议中的设备类型原始字节值</param>
        /// <returns>解析成功的 DeviceType 枚举，未定义值返回 null</returns>
        public static DeviceType? SafeParse(byte deviceTypeByte)
        {
            if (Enum.IsDefined(typeof(DeviceType), deviceTypeByte))
                return (DeviceType)deviceTypeByte;
            return null;
        }

        /// <summary>
        /// 将字节值解析为 DeviceType 枚举并获取显示名称
        /// 用于从协议原始字节直接获取可读名称
        /// </summary>
        /// <param name="deviceTypeByte">协议中的设备类型原始字节值</param>
        /// <returns>格式化的设备类型名称，已定义值如 "HAP"，未定义值如 "Unknown(0xFF)"</returns>
        public static string GetDisplayName(byte deviceTypeByte)
        {
            var dt = SafeParse(deviceTypeByte);
            if (dt.HasValue)
                return dt.Value.GetDisplayName();
            else
                return string.Format("Unknown({0})", deviceTypeByte);
        }
    }

    /// <summary>
    /// 检测模式枚举
    /// 对应C++ LivoxLidarDetectMode
    /// </summary>
    public enum DetectMode : byte
    {
        /// <summary>正常检测模式 (0x00)</summary>
        Normal = 0x00,

        /// <summary>敏感检测模式 (0x01)</summary>
        Sensitive = 0x01
    }

    /// <summary>
    /// 窗口加热模式枚举
    /// 对应C++ LivoxLidarGlassHeat
    /// </summary>
    public enum GlassHeatMode : byte
    {
        /// <summary>停止加热或诊断加热 (0x00)</summary>
        StopPowerOnHeatingOrDiagnosticHeating = 0x00,

        /// <summary>开启加热 (0x01)</summary>
        TurnOnHeating = 0x01,

        /// <summary>诊断加热 (0x02)</summary>
        DiagnosticHeating = 0x02,

        /// <summary>停止自加热 (0x03)</summary>
        StopSelfHeating = 0x03
    }

    /// <summary>
    /// 点云帧率枚举
    /// 对应C++ LivoxLidarPointFrameRate
    /// </summary>
    public enum PointFrameRate : byte
    {
        /// <summary>10Hz (0x00)</summary>
        Hz10 = 0x00,

        /// <summary>15Hz (0x01)</summary>
        Hz15 = 0x01,

        /// <summary>20Hz (0x02)</summary>
        Hz20 = 0x02,

        /// <summary>25Hz (0x03)</summary>
        Hz25 = 0x03
    }
}
