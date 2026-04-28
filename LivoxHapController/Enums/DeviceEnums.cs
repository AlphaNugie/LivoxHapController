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
    /// 定义所有支持的Livox LiDAR设备型号
    /// </summary>
    public enum DeviceType : byte
    {
        /// <summary>Hub设备 (0)</summary>
        Hub = 0,

        /// <summary>Mid40 (1)</summary>
        Mid40 = 1,

        /// <summary>Tele (2)</summary>
        Tele = 2,

        /// <summary>Horizon (3)</summary>
        Horizon = 3,

        /// <summary>Mid70 (6)</summary>
        Mid70 = 6,

        /// <summary>Avia (7)</summary>
        Avia = 7,

        /// <summary>Mid360 (9)</summary>
        Mid360 = 9,

        /// <summary>IndustrialHAP (10) — 工业级HAP</summary>
        IndustrialHAP = 10,

        /// <summary>HAP (15)</summary>
        HAP = 15,

        /// <summary>PA (16)</summary>
        PA = 16
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
