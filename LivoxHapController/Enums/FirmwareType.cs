namespace LivoxHapController.Enums
{
    /// <summary>
    /// 固件类型枚举
    /// 定义固件升级中的固件类型
    /// </summary>
    public enum FirmwareType : byte
    {
        /// <summary>
        /// 大包固件 (0x00)
        /// 包含完整系统的固件包
        /// </summary>
        FullPackage = 0x00,

        /// <summary>
        /// 雷达固件 (0x01)
        /// 仅包含雷达功能的固件
        /// </summary>
        LidarFirmware = 0x01,

        /// <summary>
        /// 其他固件 (0x02)
        /// 其他类型的固件
        /// </summary>
        OtherFirmware = 0x02
    }
}