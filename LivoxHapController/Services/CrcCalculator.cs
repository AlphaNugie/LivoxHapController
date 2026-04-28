using System;
using System.Runtime.InteropServices;

namespace LivoxHapController.Services
{
    /// <summary>
    /// Livox SDK 协议校验计算器
    /// 提供CRC-16/CCITT-FALSE和CRC-32/ISO-HDLC算法
    /// 参数来源: Livox-SDK-Communication-Protocol-HAP.md CRC ALGORITHM 章节
    /// </summary>
    public static class CrcCalculator
    {
        #region CRC-16 查表

        /// <summary>
        /// CRC-16/CCITT-FALSE 查表（MSB-first）
        /// 协议参数: 多项式=0x1021, 初始值=0xFFFF, 结果异或=0x0000, 输入反转=false, 输出反转=false
        /// </summary>
        private static readonly ushort[] Crc16Table = BuildCrc16Table();

        /// <summary>
        /// 构建CRC-16/CCITT-FALSE查表（MSB-first方式）
        /// 每个表项对应一个字节值，将该字节放在CRC高8位位置进行8次移位异或运算的结果
        /// </summary>
        private static ushort[] BuildCrc16Table()
        {
            ushort[] table = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                // 将索引值放入CRC的高8位
                ushort crc = (ushort)(i << 8);
                for (int j = 0; j < 8; j++)
                {
                    // 检测最高位是否为1，决定是否与多项式异或
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc = (ushort)(crc << 1);
                }
                table[i] = crc;
            }
            return table;
        }

        #endregion

        #region CRC-32 查表

        /// <summary>
        /// CRC-32 查表（LSB-first / 反射方式）
        /// 协议参数: 多项式=0x04C11DB7, 初始值=0xFFFFFFFF, 结果异或=0xFFFFFFFF, 输入反转=true, 输出反转=true
        /// 使用反射多项式 0xEDB88320（即 0x04C11DB7 的位反射）
        /// </summary>
        private static readonly uint[] Crc32Table = BuildCrc32Table();

        /// <summary>
        /// 构建CRC-32查表（LSB-first / 反射方式）
        /// 使用反射多项式 0xEDB88320（即 0x04C11DB7 的位反射）
        /// </summary>
        private static uint[] BuildCrc32Table()
        {
            uint[] table = new uint[256];
            for (int i = 0; i < 256; i++)
            {
                uint crc = (uint)i;
                for (int j = 0; j < 8; j++)
                {
                    // 反射多项式 0xEDB88320
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320u;
                    else
                        crc = crc >> 1;
                }
                table[i] = crc;
            }
            return table;
        }

        #endregion

        #region 公开方法

        /// <summary>
        /// 计算CRC-16/CCITT-FALSE校验值
        /// 用于SDK协议包头校验（crc16字段），校验包头前18字节
        /// 协议参数: 多项式=0x1021, 初始值=0xFFFF, 结果异或=0x0000, 输入反转=false, 输出反转=false
        /// 使用MSB-first查表法，与标准CRC-16/CCITT-FALSE定义完全一致
        /// </summary>
        /// <param name="data">待计算的数据</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="count">字节长度（通常为18字节，即SDK包头前18字节）</param>
        /// <returns>CRC-16校验值</returns>
        public static ushort ComputeCrc16(byte[] data, int offset, int count)
        {
            // 初始值 0xFFFF
            ushort crc = 0xFFFF;

            for (int i = offset; i < offset + count; i++)
            {
                // MSB-first查表法：用CRC高8位与数据字节异或后查表，结果与CRC低8位异或
                crc = (ushort)((crc << 8) ^ Crc16Table[(crc >> 8) ^ data[i]]);
            }

            // 无结果异或（XorOut=0x0000），无输出反转，直接返回
            return crc;
        }

        /// <summary>
        /// 计算CRC-16/CCITT-FALSE（简化版，从第0字节开始计算全部数据）
        /// </summary>
        /// <param name="data">待计算的数据</param>
        /// <returns>CRC-16校验值</returns>
        public static ushort ComputeCrc16(byte[] data)
        {
            return ComputeCrc16(data, 0, data.Length);
        }

        /// <summary>
        /// 计算CRC-32校验值
        /// 用于SDK协议数据段校验（crc32字段）
        /// 协议参数: 多项式=0x04C11DB7, 初始值=0xFFFFFFFF, 结果异或=0xFFFFFFFF, 输入反转=true, 输出反转=true
        /// 即标准以太网CRC32 / PKZIP CRC32
        /// </summary>
        /// <param name="data">待计算的数据（协议包的data段，不含包头）</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="count">字节长度</param>
        /// <returns>CRC-32校验值</returns>
        public static uint ComputeCrc32(byte[] data, int offset, int count)
        {
            // 初始值 0xFFFFFFFF
            uint crc = 0xFFFFFFFF;

            for (int i = offset; i < offset + count; i++)
            {
                // (crc >> 8) ^ table[(crc & 0xFF) ^ data[i]]
                crc = (crc >> 8) ^ Crc32Table[(crc & 0xFF) ^ data[i]];
            }

            // 结果异或 0xFFFFFFFF（XorOut=0xFFFFFFFF）
            return crc ^ 0xFFFFFFFF;
        }

        /// <summary>
        /// 计算CRC-32（简化版，从第0字节开始计算全部数据）
        /// </summary>
        /// <param name="data">待计算的数据</param>
        /// <returns>CRC-32校验值</returns>
        public static uint ComputeCrc32(byte[] data)
        {
            return ComputeCrc32(data, 0, data.Length);
        }

        /// <summary>
        /// 计算SDK协议包的CRC16（包头校验）
        /// 计算范围：包的前18字节（sof到resv[5]，不含crc16和crc32字段）
        /// </summary>
        /// <param name="packetBytes">完整的协议包字节</param>
        /// <returns>CRC-16校验值</returns>
        public static ushort ComputePacketCrc16(byte[] packetBytes)
        {
            // 常量 kPreambleCrcSize = 18（C++中硬编码）
            return ComputeCrc16(packetBytes, 0, 18);
        }

        /// <summary>
        /// 计算SDK协议包的CRC32（数据段校验）
        /// 计算范围：从偏移24（data段）开始到包末尾
        /// 如果数据段长度为0，返回0
        /// </summary>
        /// <param name="packetBytes">完整的协议包字节</param>
        /// <param name="totalLength">整包长度（包头length字段值）</param>
        /// <returns>CRC-32校验值</returns>
        public static uint ComputePacketCrc32(byte[] packetBytes, int totalLength)
        {
            // 数据段起始偏移 = 24（SDK包头大小）
            const int headerSize = 24;
            int dataLength = totalLength - headerSize;

            if (dataLength <= 0)
                return 0;

            return ComputeCrc32(packetBytes, headerSize, dataLength);
        }

        /// <summary>
        /// 验证SDK协议包的CRC校验
        /// 同时验证CRC16（包头）和CRC32（数据段）
        /// </summary>
        /// <param name="packetBytes">完整的协议包字节</param>
        /// <param name="totalLength">整包长度</param>
        /// <returns>校验是否通过</returns>
        public static bool VerifyPacketCrc(byte[] packetBytes, int totalLength)
        {
            if (packetBytes == null || totalLength < 24)
                return false;

            // 读取包头中的CRC16（偏移18-19，小端序）
            ushort expectedCrc16 = BitConverter.ToUInt16(packetBytes, 18);
            ushort actualCrc16 = ComputePacketCrc16(packetBytes);

            if (expectedCrc16 != actualCrc16)
                return false;

            // 读取包头中的CRC32（偏移20-23，小端序）
            uint expectedCrc32 = BitConverter.ToUInt32(packetBytes, 20);
            uint actualCrc32 = ComputePacketCrc32(packetBytes, totalLength);

            return expectedCrc32 == actualCrc32;
        }

        #endregion
    }
}
