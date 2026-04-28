namespace LivoxHapController.Enums
{
    /// <summary>
    /// 点云数据类型枚举
    /// 定义Livox HAP设备支持的点云数据格式
    /// </summary>
    public enum PointCloudDataType : byte
    {
        /// <summary>
        /// IMU数据格式 (0x00)
        /// 包含加速度计和陀螺仪数据
        /// </summary>
        ImuData = 0x00,

        /// <summary>
        /// 笛卡尔坐标系点云数据 (32位) (0x01)
        /// 包含X,Y,Z坐标和反射率的标准点云格式
        /// </summary>
        Cartesian32Bit = 0x01,

        /// <summary>
        /// 笛卡尔坐标系点云数据 (16位) (0x02)
        /// 包含X,Y,Z坐标和反射率的压缩点云格式
        /// </summary>
        Cartesian16Bit = 0x02
    }
}