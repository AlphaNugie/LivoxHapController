namespace LivoxHapController.Enums
{
    /// <summary>
    /// 设备工作状态枚举
    /// 定义Livox HAP设备可能的各种工作状态
    /// </summary>
    public enum DeviceWorkState : byte
    {
        /// <summary>
        /// 采样状态 (0x01)
        /// 雷达在此状态下发送点云和IMU数据
        /// </summary>
        Sampling = 0x01,

        /// <summary>
        /// 待机状态 (0x02)
        /// 雷达处于空闲状态，等待用户操作
        /// </summary>
        Standby = 0x02,

        /// <summary>
        /// 休眠状态 (0x03)
        /// (仅HAP T1版本支持) 雷达进入低功耗休眠模式
        /// </summary>
        Sleep = 0x03,

        /// <summary>
        /// 错误状态 (0x04)
        /// 雷达识别到错误，自动切换至本状态
        /// </summary>
        Error = 0x04,

        /// <summary>
        /// 自检状态 (0x05)
        /// 上电时雷达自动进行上电自检
        /// </summary>
        SelfCheck = 0x05,

        /// <summary>
        /// 电机启动状态 (0x06)
        /// 雷达正在启动电机
        /// </summary>
        MotorStarting = 0x06,

        /// <summary>
        /// 电机停止状态 (0x07)
        /// 雷达正在停止电机
        /// </summary>
        MotorStopping = 0x07,

        /// <summary>
        /// 升级状态 (0x08)
        /// 设备正在进行固件升级
        /// </summary>
        Upgrading = 0x08
    }
}