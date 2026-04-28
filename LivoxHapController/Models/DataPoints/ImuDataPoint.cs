namespace LivoxHapController.Models.DataPoints
{
    /// <summary>
    /// IMU单点数据结构
    /// 对应IMU数据的单点24字节格式
    /// </summary>
    public class ImuDataPoint : DataPoint
    {
        /// <summary>
        /// 时间戳 (8字节)
        /// 表示第一个点云的时间，单位：纳秒(ns)
        /// </summary>
        public ulong TimestampNanoSec { get; internal set; }

        /// <summary>
        /// X轴角速度 (4字节)
        /// 单位：弧度/秒 (rad/s)
        /// </summary>
        public float GyroX { get; internal set; }

        /// <summary>
        /// Y轴角速度 (4字节)
        /// 单位：弧度/秒 (rad/s)
        /// </summary>
        public float GyroY { get; internal set; }

        /// <summary>
        /// Z轴角速度 (4字节)
        /// 单位：弧度/秒 (rad/s)
        /// </summary>
        public float GyroZ { get; internal set; }

        /// <summary>
        /// X轴加速度 (4字节)
        /// 单位：重力加速度 (g)
        /// </summary>
        public float AccX { get; internal set; }

        /// <summary>
        /// Y轴加速度 (4字节)
        /// 单位：重力加速度 (g)
        /// </summary>
        public float AccY { get; internal set; }

        /// <summary>
        /// Z轴加速度 (4字节)
        /// 单位：重力加速度 (g)
        /// </summary>
        public float AccZ { get; internal set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ImuDataPoint {{ " +
                   $"Timestamp: {TimestampNanoSec}, " +
                   $"GyroX: {GyroX}, " +
                   $"GyroY: {GyroY}, " +
                   $"GyroZ: {GyroZ}, " +
                   $"AccX: {AccX}, " +
                   $"AccY: {AccY}, " +
                   $"AccZ: {AccZ} " +
                   $"}}";
        }
    }
}