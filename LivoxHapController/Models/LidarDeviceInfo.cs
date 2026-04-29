using System;
using System.Net;
using System.Text;
using LivoxHapController.Enums;

namespace LivoxHapController.Models
{
    /// <summary>
    /// LiDAR设备信息模型
    /// 存储设备发现后获取的雷达设备基本信息
    /// 用于在发现设备后存储和管理设备信息，供后续命令控制和数据接收使用
    /// </summary>
    public class LidarDeviceInfo
    {
        #region 属性

        /// <summary>
        /// 设备句柄（唯一标识符）
        /// 在发现设备时分配，用于内部管理和索引
        /// </summary>
        public int Handle { get; set; }

        /// <summary>
        /// 设备类型枚举（见 DeviceType 枚举定义）
        /// 使用枚举类型替代原始 byte，提供类型安全和可读性
        /// </summary>
        public DeviceType DeviceType { get; set; }

        /// <summary>
        /// 设备序列号（原始16字节ASCII数据）
        /// 雷达的唯一硬件标识
        /// </summary>
        public byte[] SerialNumber { get; set; }

        /// <summary>
        /// 雷达IP地址（原始4字节数据）
        /// </summary>
        public byte[] LidarIpBytes { get; set; }

        /// <summary>
        /// 雷达命令端口
        /// 用于发送控制命令和接收ACK响应
        /// </summary>
        public ushort CommandPort { get; set; }

        /// <summary>
        /// 响应来源的远程端点
        /// 记录设备发现时的网络端点信息
        /// </summary>
        public IPEndPoint
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             RemoteEndPoint
        { get; set; }

        #endregion

        #region 计算属性

        /// <summary>
        /// 雷达IP地址的点分十进制字符串表示
        /// 例如 "192.168.1.100"
        /// </summary>
        public string LidarIpString
        {
            get
            {
                if (LidarIpBytes == null || LidarIpBytes.Length < 4) return "0.0.0.0";
                return string.Format("{0}.{1}.{2}.{3}", LidarIpBytes[0], LidarIpBytes[1], LidarIpBytes[2], LidarIpBytes[3]);
            }
        }

        /// <summary>
        /// 设备序列号的字符串形式
        /// 截取到第一个空字符（'\0'）作为有效内容
        /// </summary>
        public string SerialNumberString
        {
            get
            {
                if (SerialNumber == null) return string.Empty;
                int len = Array.IndexOf(SerialNumber, (byte)0);
                if (len < 0) len = SerialNumber.Length;
                return Encoding.ASCII.GetString(SerialNumber, 0, len);
            }
        }

        /// <summary>
        /// 设备类型名称
        /// 使用 DeviceTypeExtensions.GetDisplayName() 获取可读的设备型号名称
        /// 消除了原有的硬编码 switch 映射，复用枚举扩展方法
        /// </summary>
        public string DeviceTypeName
        {
            get { return DeviceType.GetDisplayName(); }
        }

        /// <summary>
        /// 设备是否已连接
        /// 标记设备是否已通过命令端口建立通信
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// 设备发现时间
        /// 记录此设备信息首次被发现的UTC时间
        /// </summary>
        public DateTime DiscoveredTime { get; set; }

        #endregion

        #region 构造函数

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public LidarDeviceInfo()
        {
            Handle = -1;
            DeviceType = DeviceType.Hub;
            SerialNumber = new byte[16];
            LidarIpBytes = new byte[4];
            CommandPort = 56000;
            IsConnected = false;
            DiscoveredTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 从设备发现事件参数构造设备信息
        /// </summary>
        /// <param name="handle">设备句柄</param>
        /// <param name="deviceType">设备类型枚举</param>
        /// <param name="serialNumber">序列号（16字节）</param>
        /// <param name="lidarIp">雷达IP（4字节）</param>
        /// <param name="commandPort">命令端口</param>
        /// <param name="remoteEndPoint">响应来源端点</param>
        public LidarDeviceInfo(int handle, DeviceType deviceType, byte[] serialNumber, byte[] lidarIp,
            ushort commandPort, IPEndPoint remoteEndPoint)
        {
            Handle = handle;
            DeviceType = deviceType;
            SerialNumber = serialNumber;
            LidarIpBytes = lidarIp;
            CommandPort = commandPort;
            RemoteEndPoint = remoteEndPoint;
            IsConnected = false;
            DiscoveredTime = DateTime.UtcNow;
        }

        #endregion

        #region 方法

        /// <summary>
        /// 返回设备信息的可读字符串表示
        /// </summary>
        /// <returns>格式化的设备信息字符串</returns>
        public override string ToString()
        {
            return string.Format("[Handle={0}, Type={1}({2}), SN={3}, IP={4}:{5}]",
                Handle, DeviceTypeName, (byte)DeviceType, SerialNumberString, LidarIpString, CommandPort);
        }

        #endregion
    }
}
