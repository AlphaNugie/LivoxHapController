namespace LivoxHapController.Enums
{
    /// <summary>
    /// Livox HAP 命令类型枚举
    /// 定义所有支持的协议命令ID
    /// 对应C++ define.h 中的 LidarCommandID
    /// </summary>
    public enum CommandType : ushort
    {
        /// <summary>
        /// 设备广播发现命令 (0x0000)
        /// 用于发现网络中的Livox HAP设备
        /// </summary>
        BroadcastDiscovery = 0x0000,

        /// <summary>
        /// 请求固件信息命令 (0x00FF)
        /// 查询设备固件详细信息
        /// </summary>
        RequestFirmwareInfo = 0x00FF,

        /// <summary>
        /// 参数信息配置命令 (0x0100)
        /// 用于配置设备的各种参数
        /// </summary>
        ParameterConfiguration = 0x0100,

        /// <summary>
        /// 雷达信息查询命令 (0x0101)
        /// 用于查询设备的基本信息
        /// </summary>
        RadarInfoQuery = 0x0101,

        /// <summary>
        /// 雷达信息推送命令 (0x0102)
        /// 设备主动推送的状态信息
        /// </summary>
        RadarInfoPush = 0x0102,

        /// <summary>
        /// 设备重启命令 (0x0200)
        /// 用于重启设备
        /// </summary>
        DeviceReboot = 0x0200,

        /// <summary>
        /// 设备重置命令 (0x0201)
        /// 恢复设备出厂设置
        /// </summary>
        DeviceReset = 0x0201,

        /// <summary>
        /// PPS时间同步命令 (0x0202)
        /// GPS PPS时间同步配置
        /// </summary>
        PpsTimeSync = 0x0202,

        /// <summary>
        /// LOG文件推送命令 (0x0300)
        /// 设备推送日志文件
        /// </summary>
        LogFilePush = 0x0300,

        /// <summary>
        /// LOG配置命令 (0x0301)
        /// 配置日志记录参数
        /// </summary>
        LogConfiguration = 0x0301,

        /// <summary>
        /// 日志时间同步命令 (0x0302)
        /// 日志系统时间同步
        /// </summary>
        LogTimeSync = 0x0302,

        /// <summary>
        /// 调试点云控制命令 (0x0303)
        /// 控制调试点云的开关
        /// </summary>
        DebugPointCloudControl = 0x0303,

        /// <summary>
        /// 开始升级命令 (0x0400)
        /// 启动设备固件升级流程
        /// </summary>
        StartUpgrade = 0x0400,

        /// <summary>
        /// 固件数据传输命令 (0x0401)
        /// 传输固件数据包
        /// </summary>
        FirmwareTransfer = 0x0401,

        /// <summary>
        /// 固件传输结束命令 (0x0402)
        /// 标记固件传输完成
        /// </summary>
        FirmwareEndTransfer = 0x0402,

        /// <summary>
        /// 升级状态查询命令 (0x0403)
        /// 查询固件升级进度和状态
        /// </summary>
        UpgradeStatusQuery = 0x0403
    }
}
