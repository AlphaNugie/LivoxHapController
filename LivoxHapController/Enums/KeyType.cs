namespace LivoxHapController.Enums
{
    /// <summary>
    /// 键值类型枚举
    /// 定义参数配置和查询中使用的键值类型
    /// 包含Livox SDK协议中所有已知的参数键
    /// 对应C++ livox_lidar_def.h 中的 ParamKeyName
    /// </summary>
    public enum KeyType : ushort
    {
        /// <summary>
        /// 点云数据类型 (0x0000)
        /// 配置点云数据格式 (32位或16位)
        /// </summary>
        PclDataType = 0x0000,

        /// <summary>
        /// 扫描模式 (0x0001)
        /// 配置空间扫描模式 (非重复扫描或重复扫描)
        /// </summary>
        PatternMode = 0x0001,

        /// <summary>
        /// 双发射使能 (0x0002)
        /// 启用或禁用双发射模式
        /// </summary>
        DualEmitEnable = 0x0002,

        /// <summary>
        /// 点云发送控制 (0x0003)
        /// 控制是否在进入工作模式时发送点云
        /// </summary>
        PointSendEnable = 0x0003,

        /// <summary>
        /// 雷达IP配置 (0x0004)
        /// 配置雷达的IP地址、子网掩码和网关
        /// </summary>
        LidarIpConfig = 0x0004,

        /// <summary>
        /// 状态信息主机IP配置 (0x0005)
        /// 配置状态信息推送的目的IP地址和端口
        /// </summary>
        StateInfoHostIpConfig = 0x0005,

        /// <summary>
        /// 点云主机IP配置 (0x0006)
        /// 配置点云数据推送的目的IP地址和端口
        /// </summary>
        PointCloudHostIpConfig = 0x0006,

        /// <summary>
        /// IMU主机IP配置 (0x0007)
        /// 配置IMU数据推送的目的IP地址和端口
        /// </summary>
        ImuHostIpConfig = 0x0007,

        /// <summary>
        /// 命令主机IP配置 (0x0008)
        /// 配置命令通道的主机IP地址和端口
        /// </summary>
        CtlHostIpConfig = 0x0008,

        /// <summary>
        /// 日志主机IP配置 (0x0009)
        /// 配置日志推送的目的IP地址和端口
        /// </summary>
        LogHostIpConfig = 0x0009,

        /// <summary>
        /// 车速 (0x0010)
        /// 外部输入的车速信息
        /// </summary>
        VehicleSpeed = 0x0010,

        /// <summary>
        /// 环境温度 (0x0011)
        /// 外部输入的环境温度信息
        /// </summary>
        EnvironmentTemp = 0x0011,

        /// <summary>
        /// 安装姿态 (0x0012)
        /// 保存雷达在用户装置上的安装位置信息
        /// </summary>
        InstallAttitude = 0x0012,

        /// <summary>
        /// 盲区设置 (0x0013)
        /// 配置盲区范围 (50-200cm)
        /// </summary>
        BlindSpotSet = 0x0013,

        /// <summary>
        /// 帧率 (0x0014)
        /// 配置点云帧率
        /// </summary>
        FrameRate = 0x0014,

        /// <summary>
        /// FOV配置0 (0x0015)
        /// 配置视场区域0的参数
        /// </summary>
        FovConfig0 = 0x0015,

        /// <summary>
        /// FOV配置1 (0x0016)
        /// 配置视场区域1的参数
        /// </summary>
        FovConfig1 = 0x0016,

        /// <summary>
        /// FOV配置使能 (0x0017)
        /// 启用或禁用FOV配置
        /// </summary>
        FovConfigEnable = 0x0017,

        /// <summary>
        /// 检测模式 (0x0018)
        /// 配置正常或敏感检测模式
        /// </summary>
        DetectMode = 0x0018,

        /// <summary>
        /// 功能IO配置 (0x0019)
        /// 配置功能IO参数
        /// </summary>
        FuncIoConfig = 0x0019,

        /// <summary>
        /// 目标工作模式 (0x001A)
        /// 配置雷达的目标工作状态
        /// </summary>
        WorkTargetMode = 0x001A,

        /// <summary>
        /// 窗口加热支持 (0x001B)
        /// 启用或禁用窗口加热功能
        /// </summary>
        GlassHeatSupport = 0x001B,

        /// <summary>
        /// IMU数据使能 (0x001C)
        /// 启用或禁用IMU数据推送
        /// </summary>
        ImuDataEnable = 0x001C,

        /// <summary>
        /// 功能安全使能 (0x001D)
        /// 启用或禁用功能安全诊断功能
        /// </summary>
        FusaEnable = 0x001D,

        /// <summary>
        /// 强制加热使能 (0x001E)
        /// 启用或禁用强制加热功能
        /// </summary>
        ForceHeatEnable = 0x001E,

        /// <summary>
        /// 开机工作模式 (0x0020)
        /// 配置开机后初始工作模式
        /// </summary>
        WorkmodeAfterBoot = 0x0020,

        /// <summary>
        /// 日志参数设置 (0x7FFF)
        /// 配置日志记录参数
        /// </summary>
        LogParamSet = 0x7FFF,

        /// <summary>
        /// 序列号 (0x8000)
        /// 雷达的唯一序列号
        /// </summary>
        SerialNumber = 0x8000,

        /// <summary>
        /// 产品信息 (0x8001)
        /// 雷达类型和生产日期
        /// </summary>
        ProductInfo = 0x8001,

        /// <summary>
        /// 应用固件版本 (0x8002)
        /// 应用固件的版本号
        /// </summary>
        VersionApp = 0x8002,

        /// <summary>
        /// 加载器固件版本 (0x8003)
        /// 加载器固件的版本号
        /// </summary>
        VersionLoader = 0x8003,

        /// <summary>
        /// 硬件版本 (0x8004)
        /// 硬件版本号
        /// </summary>
        VersionHardware = 0x8004,

        /// <summary>
        /// MAC地址 (0x8005)
        /// 雷达的MAC地址
        /// </summary>
        MacAddress = 0x8005,

        /// <summary>
        /// 当前工作状态 (0x8006)
        /// 雷达当前的工作状态
        /// </summary>
        CurrentWorkState = 0x8006,

        /// <summary>
        /// 核心温度 (0x8007)
        /// 雷达核心温度信息
        /// </summary>
        CoreTemp = 0x8007,

        /// <summary>
        /// 上电次数 (0x8008)
        /// 雷达累计上电次数
        /// </summary>
        PowerUpCount = 0x8008,

        /// <summary>
        /// 本地当前时间 (0x8009)
        /// 雷达本机当前时间
        /// </summary>
        LocalTimeNow = 0x8009,

        /// <summary>
        /// 上次同步时间 (0x800A)
        /// 上次时间同步的时间
        /// </summary>
        LastSyncTime = 0x800A,

        /// <summary>
        /// 时间偏移 (0x800B)
        /// 时间同步偏移量
        /// </summary>
        TimeOffset = 0x800B,

        /// <summary>
        /// 时间同步类型 (0x800C)
        /// 当前时间同步方式
        /// </summary>
        TimeSyncType = 0x800C,

        /// <summary>
        /// 状态码 (0x800D)
        /// 雷达故障状态码
        /// </summary>
        StatusCode = 0x800D,

        /// <summary>
        /// 诊断状态 (0x800E)
        /// 雷达各模块的诊断状态
        /// </summary>
        LidarDiagStatus = 0x800E,

        /// <summary>
        /// Flash状态 (0x800F)
        /// Flash存储器的当前状态
        /// </summary>
        LidarFlashStatus = 0x800F,

        /// <summary>
        /// 固件类型 (0x8010)
        /// 当前运行的固件类型
        /// </summary>
        FirmwareType = 0x8010,

        /// <summary>
        /// HMS代码 (0x8011)
        /// 雷达HMS故障码
        /// </summary>
        HmsCode = 0x8011,

        /// <summary>
        /// 当前窗口加热状态 (0x8012)
        /// 窗口加热功能的当前状态
        /// </summary>
        CurrentGlassHeatState = 0x8012,

        /// <summary>
        /// ROI模式 (0xFFFE)
        /// 感兴趣区域模式配置
        /// </summary>
        RoiMode = 0xFFFE,

        /// <summary>
        /// 雷达诊断信息查询 (0xFFFF)
        /// 用于查询雷达诊断信息
        /// </summary>
        LidarDiagInfoQuery = 0xFFFF
    }
}
