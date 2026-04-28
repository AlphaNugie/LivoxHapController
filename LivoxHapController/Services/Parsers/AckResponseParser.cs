using System;
using LivoxHapController.Enums;
using LivoxHapController.Services;

namespace LivoxHapController.Services.Parsers
{
    /// <summary>
    /// ACK响应解析器
    /// 负责解析雷达返回的ACK响应包
    /// 对应C++中各Handler对ACK包的处理逻辑
    /// </summary>
    public static class AckResponseParser
    {
        #region 通用控制命令ACK

        /// <summary>
        /// 解析通用控制命令ACK响应
        /// 对应C++ LivoxLidarAsyncControlResponse 结构体
        /// 格式：{ret_code(uint8), error_key(uint16_le)}
        /// </summary>
        /// <param name="packetData">包头的data段字节</param>
        /// <returns>ACK响应结果</returns>
        public static AsyncControlResponse ParseAsyncControlResponse(byte[] packetData)
        {
            if (packetData == null || packetData.Length < 3)
                return new AsyncControlResponse { RetCode = 0xFF, ErrorKey = 0xFFFF };

            return new AsyncControlResponse
            {
                RetCode = packetData[0],
                ErrorKey = BitConverter.ToUInt16(packetData, 1)
            };
        }

        /// <summary>
        /// 从完整协议包中解析通用控制命令ACK
        /// </summary>
        /// <param name="fullPacket">完整的协议包字节（含24字节包头）</param>
        /// <returns>ACK响应结果</returns>
        public static AsyncControlResponse ParseAsyncControlResponseFromPacket(byte[] fullPacket)
        {
            if (fullPacket == null || fullPacket.Length < SdkPacketBuilder.HeaderSize + 3)
                return new AsyncControlResponse { RetCode = 0xFF, ErrorKey = 0xFFFF };

            // 跳过24字节包头，读取data段
            byte[] dataSegment = new byte[fullPacket.Length - SdkPacketBuilder.HeaderSize];
            Buffer.BlockCopy(fullPacket, SdkPacketBuilder.HeaderSize, dataSegment, 0, dataSegment.Length);
            return ParseAsyncControlResponse(dataSegment);
        }

        #endregion

        #region 内部信息查询ACK

        /// <summary>
        /// 解析内部信息查询ACK响应（cmd_id: 0x0101 ACK）
        /// 协议格式：{ret_code(uint8), key_num(uint16_le), key_value_list(KeyValue[])}
        /// 注意：与0x0100配置命令ACK不同，0x0101查询ACK没有rsvd字段
        /// </summary>
        /// <param name="packetData">包头的data段字节</param>
        /// <returns>内部信息查询结果</returns>
        public static InternalInfoResponse ParseInternalInfoResponse(byte[] packetData)
        {
            // 最小长度：ret_code(1) + key_num(2) = 3字节
            if (packetData == null || packetData.Length < 3)
                return new InternalInfoResponse { RetCode = 0xFF, ParamResults = new KeyValueResult[0] };

            // ret_code: 偏移0, uint8_t
            byte retCode = packetData[0];
            // key_num: 偏移1, uint16_t（协议无rsvd字段，ret_code后紧跟key_num）
            ushort paramNum = BitConverter.ToUInt16(packetData, 1);

            // 若ret_code非0，表示查询失败，无需解析key_value_list
            if (retCode != 0)
            {
                return new InternalInfoResponse
                {
                    RetCode = retCode,
#if NET45_OR_GREATER
                    ParamResults = new KeyValueResult[0]
#elif NET9_0_OR_GREATER
                    ParamResults = []
#endif
                };
            }

            // 解码所有KeyValue
            KeyValueResult[] results = KeyValueCodec.DecodeAllKeyValuesForQueryAck(packetData, paramNum);

            return new InternalInfoResponse
            {
                RetCode = retCode,
                ParamResults = results
            };
        }

        /// <summary>
        /// 从完整协议包中解析内部信息查询ACK
        /// </summary>
        /// <param name="fullPacket">完整的协议包字节（含24字节包头）</param>
        /// <returns>内部信息查询结果</returns>
        public static InternalInfoResponse ParseInternalInfoResponseFromPacket(byte[] fullPacket)
        {
            // 最小长度：包头(24) + ret_code(1) + key_num(2) = 27字节
            if (fullPacket == null || fullPacket.Length < SdkPacketBuilder.HeaderSize + 3)
                return new InternalInfoResponse { RetCode = 0xFF, ParamResults = new KeyValueResult[0] };

            byte[] dataSegment = new byte[fullPacket.Length - SdkPacketBuilder.HeaderSize];
            Buffer.BlockCopy(fullPacket, SdkPacketBuilder.HeaderSize, dataSegment, 0, dataSegment.Length);
            return ParseInternalInfoResponse(dataSegment);
        }

        #endregion

        #region 判断包类型

        /// <summary>
        /// 判断是否为ACK包
        /// </summary>
        /// <param name="packetData">完整协议包字节</param>
        /// <returns>是否为ACK包</returns>
        public static bool IsAckPacket(byte[] packetData)
        {
            if (packetData == null || packetData.Length < SdkPacketBuilder.HeaderSize)
                return false;
            return packetData[10] == SdkPacketBuilder.CmdTypeAck;
        }

        /// <summary>
        /// 判断是否为工作模式控制命令的ACK
        /// </summary>
        /// <param name="packetData">完整协议包字节</param>
        /// <returns>是否为工作模式控制ACK</returns>
        public static bool IsWorkModeControlAck(byte[] packetData)
        {
            if (!IsAckPacket(packetData)) return false;
            ushort cmdId = BitConverter.ToUInt16(packetData, 8);
            return cmdId == (ushort)CommandType.ParameterConfiguration;
        }

        /// <summary>
        /// 判断是否为信息查询ACK
        /// </summary>
        /// <param name="packetData">完整协议包字节</param>
        /// <returns>是否为信息查询ACK</returns>
        public static bool IsInternalInfoAck(byte[] packetData)
        {
            if (!IsAckPacket(packetData)) return false;
            ushort cmdId = BitConverter.ToUInt16(packetData, 8);
            return cmdId == (ushort)CommandType.RadarInfoQuery;
        }

        #endregion
    }

    #region 响应数据结构

    /// <summary>
    /// 通用控制命令ACK响应结果
    /// 对应C++ LivoxLidarAsyncControlResponse
    /// </summary>
    public struct AsyncControlResponse
    {
        /// <summary>返回码（0=成功）</summary>
        public byte RetCode;

        /// <summary>错误键（仅失败时有意义，指示哪个参数出错）</summary>
        public ushort ErrorKey;

        /// <summary>是否成功</summary>
        public bool IsSuccess { get { return RetCode == 0; } }
    }

    /// <summary>
    /// 内部信息查询响应结果
    /// 对应C++ LivoxLidarDiagInternalInfoResponse
    /// </summary>
    public struct InternalInfoResponse
    {
        /// <summary>返回码（0=成功）</summary>
        public byte RetCode;

        /// <summary>查询到的参数结果列表</summary>
        public KeyValueResult[] ParamResults;

        /// <summary>是否成功</summary>
        public bool IsSuccess { get { return RetCode == 0; } }
    }

    #endregion
}
