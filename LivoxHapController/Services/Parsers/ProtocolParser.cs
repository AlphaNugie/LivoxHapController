#if NET45_OR_GREATER
using System;
#endif
using LivoxHapController.Enums;

namespace LivoxHapController.Services.Parsers
{
    /// <summary>
    /// Livox协议解析器
    /// </summary>
    public static class ProtocolParser
    {
        /// <summary>
        /// 解析控制协议头
        /// </summary>
        public static ControlProtocolHeader ParseControlHeader(byte[] data)
        {
            if (data.Length < 24)
                throw new ArgumentException("Invalid data length for protocol header");

            return new ControlProtocolHeader
            {
                Sof = data[0],
                Version = data[1],
                Length = BitConverter.ToUInt16(data, 2),
                SeqNum = BitConverter.ToUInt32(data, 4),
                CmdId = (CommandType)BitConverter.ToUInt16(data, 8),
                CmdType = data[10],
                SenderType = data[11],
                Crc16 = BitConverter.ToUInt16(data, 18),
                Crc32 = BitConverter.ToUInt32(data, 20)
            };
        }

        ///// <summary>
        ///// 解析点云数据头
        ///// </summary>
        //public static PointCloudHeader ParsePointCloudHeader(byte[] data)
        //{
        //    return new PointCloudHeader
        //    {
        //        Version = data[0],
        //        Length = BitConverter.ToUInt16(data, 1),
        //        TimeInterval = BitConverter.ToUInt16(data, 3),
        //        DotNum = BitConverter.ToUInt16(data, 5),
        //        UdpCnt = BitConverter.ToUInt16(data, 7),
        //        FrameCnt = data[9],
        //        DataType = (DataType)data[10],
        //        TimeType = data[11],
        //        PackInfo = data[12],
        //        Timestamp = BitConverter.ToUInt64(data, 28)
        //    };
        //}

        /// <summary>
        /// 解析广播响应数据
        /// </summary>
        public static BroadcastResponse ParseBroadcastResponse(byte[] data)
        {
#if NET45_OR_GREATER
            byte[] serialNumber = new byte[16];
            byte[] lidarIp = new byte[4];

            Array.Copy(data, 2, serialNumber, 0, 16);
            Array.Copy(data, 18, lidarIp, 0, 4);
#endif

            return new BroadcastResponse
            {
                RetCode = data[0],
                DevType = data[1],
#if NET45_OR_GREATER
                SerialNumber = serialNumber,
                LidarIp = lidarIp,
#elif NET9_0_OR_GREATER
                SerialNumber = data[2..18],
                LidarIp = data[18..22],
#endif
                CmdPort = BitConverter.ToUInt16(data, 22)
            };
        }
    }

    /// <summary>
    /// 控制协议头结构
    /// 对应Livox控制协议的数据包包头，共24字节
    /// 协议定义：sof(1)+version(1)+length(2)+seq_num(4)+cmd_id(2)+cmd_type(1)+sender_type(1)+resv(6)+crc16(2)+crc32(4) = 24
    /// </summary>
    public struct ControlProtocolHeader
    {
        //public byte Sof;         // 起始字节(0xAA)
        //public byte Version;     // 协议版本
        //public ushort Length;    // 数据帧长度
        //public uint SeqNum;      // 序列号
        //public CommandType CmdId; // 命令ID
        //public byte CmdType;      // 命令类型 (0x00:REQ, 0x01:ACK)
        //public byte SenderType;   // 发送设备类型 (0:上位机, 1:雷达)
        //public ushort Crc16;      // 包头CRC校验
        //public uint Crc32;        // 数据CRC校验

        /// <summary>起始帧标识，固定值0xAA</summary>
        public byte Sof;

        /// <summary>协议版本号</summary>
        public byte Version;

        /// <summary>整包长度（从sof开始到整个data段结束的所有字节数，含24字节包头）</summary>
        public ushort Length;

        /// <summary>包序列号，用于请求与响应的配对；递增到65535后从0开始循环</summary>
        public uint SeqNum;

        /// <summary>命令ID，标识具体的命令类型</summary>
        public CommandType CmdId;

        /// <summary>命令类型：0x00=请求(REQ)，0x01=应答(ACK)</summary>
        public byte CmdType;

        /// <summary>发送方设备类型：0=上位机，1=雷达</summary>
        public byte SenderType;

        /// <summary>协议头的CRC16校验值，校验范围：sof到resv末尾（前18字节）</summary>
        public ushort Crc16;

        /// <summary>数据段的CRC32校验值，校验范围：data段（偏移24开始）</summary>
        public uint Crc32;
    }

    ///// <summary>
    ///// 点云数据头结构
    ///// </summary>
    //public struct PointCloudHeader
    //{
    //    public byte Version;       // 协议版本
    //    public ushort Length;      // 数据长度
    //    public ushort TimeInterval; // 采样时间间隔(0.1us)
    //    public ushort DotNum;      // 包含点数
    //    public ushort UdpCnt;      // UDP包计数
    //    public byte FrameCnt;      // 帧计数(HAP固定为0)
    //    public DataType DataType;  // 数据类型
    //    public byte TimeType;      // 时间类型
    //    public byte PackInfo;      // 封装信息
    //    public ulong Timestamp;    // 时间戳(ns)
    //}

    /// <summary>
    /// 广播响应结构
    /// 设备收到广播发现请求后返回的响应数据
    /// </summary>
    public struct BroadcastResponse
    {
        /// <summary>返回码，0表示成功</summary>
        public byte RetCode;

        /// <summary>设备类型，标识雷达的具体型号</summary>
        public byte DevType;

        /// <summary>16字节设备序列号</summary>
        public byte[] SerialNumber;

        /// <summary>4字节设备IP地址</summary>
        public byte[] LidarIp;

        /// <summary>设备命令端口，用于后续命令通信</summary>
        public ushort CmdPort;
    }
}