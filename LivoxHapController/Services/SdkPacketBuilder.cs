using System;
using System.Text;
using LivoxHapController.Enums;

namespace LivoxHapController.Services
{
    /// <summary>
    /// SDK协议包构建器
    /// 负责构建Livox SDK控制协议的数据包
    /// 对应C++源码中的 SdkProtocol::Pack() + CommPort::Pack()
    /// </summary>
    /// <remarks>
    /// 包结构（共24字节固定包头 + 变长数据段）：
    /// [0]    sof        = 0xAA
    /// [1]    version    = 0x00
    /// [2-3]  length     = 整包长度（LE）
    /// [4-7]  seq_num    = 序列号（LE）
    /// [8-9]  cmd_id     = 命令ID（LE）
    /// [10]   cmd_type   = 0(命令)/1(应答)
    /// [11]   sender_type = 0(上位机)/1(雷达)
    /// [12-17] rsvd       = 保留，全0
    /// [18-19] crc16_h    = 包头CRC16（LE）
    /// [20-23] crc32_d    = 数据段CRC32（LE）
    /// [24+]   data       = 命令数据段
    /// </remarks>
    public static class SdkPacketBuilder
    {
        #region 常量定义

        /// <summary>协议起始标志字节</summary>
        public const byte Sof = 0xAA;

        /// <summary>协议版本号</summary>
        public const byte Version = 0x00;

        /// <summary>SDK固定包头大小（字节）</summary>
        public const int HeaderSize = 24;

        /// <summary>CRC16计算范围大小（前18字节，sof到rsvd）</summary>
        private const int PreambleCrcSize = 18;

        /// <summary>最大命令缓冲区大小</summary>
        public const int MaxCommandBufferSize = 1400;

        /// <summary>发送方类型：上位机</summary>
        public const byte SenderHost = 0;

        /// <summary>发送方类型：雷达</summary>
        public const byte SenderLidar = 1;

        /// <summary>命令类型：命令</summary>
        public const byte CmdTypeCommand = 0;

        /// <summary>命令类型：应答</summary>
        public const byte CmdTypeAck = 1;

        /// <summary>设备广播发送端口</summary>
        public const int DetectionPort = 56000;

        /// <summary>设备广播响应监听端口</summary>
        public const int DetectionListenPort = 56001;

        #endregion

        #region 序列号生成

        /// <summary>
        /// 序列号计数器（对应C++ GenerateSeq::GetSeq()）
        /// 协议规定：从0开始递增，到65535后从0重新循环
        /// </summary>
        private static uint _sequenceNumber = 0;

        /// <summary>
        /// 序列号最大值，超过此值后循环回0（协议v1.4.7规定）
        /// </summary>
        private const uint MaxSequenceNumber = 65535;

        /// <summary>
        /// 生成下一个序列号
        /// 递增到65535后重新从0开始循环
        /// </summary>
        /// <returns>序列号</returns>
        public static uint NextSequenceNumber()
        {
            _sequenceNumber++;
            // 协议v1.4.7: seq_num递增到65535之后重新从0开始循环
            if (_sequenceNumber > MaxSequenceNumber)
                _sequenceNumber = 0;
            return _sequenceNumber;
        }

        /// <summary>
        /// 重置序列号计数器
        /// </summary>
        public static void ResetSequenceNumber()
        {
            _sequenceNumber = 0;
        }

        #endregion

        #region 包构建

        /// <summary>
        /// 构建SDK协议命令包（核心方法）
        /// 对应C++ SdkProtocol::Pack() 的完整逻辑
        /// </summary>
        /// <param name="cmdId">命令ID</param>
        /// <param name="cmdType">命令类型（0=命令, 1=应答）</param>
        /// <param name="senderType">发送方类型（0=上位机, 1=雷达）</param>
        /// <param name="data">数据段内容，可为null（表示空数据段）</param>
        /// <returns>完整的协议包字节数组</returns>
        public static byte[] BuildPacket(CommandType cmdId, byte cmdType, byte senderType, byte[]
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            data)
        {
            int dataLength = (data != null) ? data.Length : 0;

            // 整包长度 = 包头(24) + 数据段长度
            int totalLength = HeaderSize + dataLength;

            // 创建缓冲区（初始化为0，确保rsvd为全0）
            byte[] packet = new byte[totalLength];

            // 填充包头字段
            packet[0] = Sof;                                               // sof
            packet[1] = Version;                                           // version
            WriteUInt16Le(packet, 2, (ushort)totalLength);                 // length（整包长度）
            WriteUInt32Le(packet, 4, NextSequenceNumber());               // seq_num
            WriteUInt16Le(packet, 8, (ushort)cmdId);                      // cmd_id
            packet[10] = cmdType;                                          // cmd_type
            packet[11] = senderType;                                       // sender_type
            // 偏移12-17为rsvd，已由new byte[]初始化为0

            // 计算CRC16（对前18字节）
            ushort crc16 = CrcCalculator.ComputeCrc16(packet, 0, PreambleCrcSize);
            WriteUInt16Le(packet, 18, crc16);

            // 计算CRC32（对数据段）
            uint crc32;
            if (dataLength == 0 || data == null)
            {
                // 空数据段时CRC32为0（与C++逻辑一致）
                crc32 = 0;
            }
            else
            {
                // 先将data复制到packet中（偏移24开始）
                Buffer.BlockCopy(data, 0, packet, HeaderSize, dataLength);
                // 计算数据段的CRC32
                crc32 = CrcCalculator.ComputeCrc32(packet, HeaderSize, dataLength);
            }
            WriteUInt32Le(packet, 20, crc32);

            return packet;
        }

        /// <summary>
        /// 构建Host发送的命令包（简化版，senderType默认为Host）
        /// </summary>
        /// <param name="cmdId">命令ID</param>
        /// <param name="data">数据段内容</param>
        /// <returns>完整的协议包字节数组</returns>
        public static byte[] BuildCommand(CommandType cmdId, byte[] data)
        {
            return BuildPacket(cmdId, CmdTypeCommand, SenderHost, data);
        }

        /// <summary>
        /// 构建空数据段的命令包
        /// 用于广播搜索等不需要数据的命令
        /// </summary>
        /// <param name="cmdId">命令ID</param>
        /// <returns>完整的协议包字节数组</returns>
        public static byte[] BuildEmptyCommand(CommandType cmdId)
        {
            return BuildPacket(cmdId, CmdTypeCommand, SenderHost, null);
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 向字节数组写入UInt16（小端序）
        /// </summary>
        /// <param name="buffer">目标缓冲区</param>
        /// <param name="offset">写入偏移</param>
        /// <param name="value">要写入的值</param>
        public static void WriteUInt16Le(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        /// <summary>
        /// 向字节数组写入UInt32（小端序）
        /// </summary>
        /// <param name="buffer">目标缓冲区</param>
        /// <param name="offset">写入偏移</param>
        /// <param name="value">要写入的值</param>
        public static void WriteUInt32Le(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>
        /// 向字节数组写入Int32（小端序）
        /// </summary>
        /// <param name="buffer">目标缓冲区</param>
        /// <param name="offset">写入偏移</param>
        /// <param name="value">要写入的值</param>
        public static void WriteInt32Le(byte[] buffer, int offset, int value)
        {
            WriteUInt32Le(buffer, offset, unchecked((uint)value));
        }

        /// <summary>
        /// 向字节数组写入Float（小端序，IEEE 754）
        /// </summary>
        /// <param name="buffer">目标缓冲区</param>
        /// <param name="offset">写入偏移</param>
        /// <param name="value">要写入的值</param>
        public static void WriteFloatLe(byte[] buffer, int offset, float value)
        {
            byte[] floatBytes = BitConverter.GetBytes(value);
            // 确保是小端序
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(floatBytes);
            Buffer.BlockCopy(floatBytes, 0, buffer, offset, 4);
        }

        /// <summary>
        /// 将IPv4地址字符串转为4字节（用于网络协议打包）
        /// </summary>
        /// <param name="ipStr">点分十进制IP字符串，如 "192.168.1.100"</param>
        /// <returns>4字节的IP地址</returns>
        public static byte[] IpToBytes(string ipStr)
        {
            return System.Net.IPAddress.Parse(ipStr).GetAddressBytes();
        }

        #endregion
    }
}
