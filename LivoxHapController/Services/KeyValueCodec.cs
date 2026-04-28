using System;
using LivoxHapController.Enums;

namespace LivoxHapController.Services
{
    /// <summary>
    /// KeyValue参数编码器
    /// 负责将键值对参数序列化/反序列化为Livox SDK协议要求的字节格式
    /// 对应C++源码中的 LivoxLidarKeyValueParam 及其在 command_impl.cpp 中的各种赋值方式
    /// </summary>
    /// <remarks>
    /// KeyValueParam 的二进制格式（在 #pragma pack(1) 下）：
    /// [0-1]  key     : uint16_le  — 参数键（见KeyType枚举）
    /// [2-3]  length  : uint16_le  — value的实际字节数
    /// [4+]   value   : uint8[]    — 参数值，长度由length决定
    /// 
    /// 注意：C++中是变长结构体，value[1]只是占位符。
    /// 单个KeyValueParam总大小 = 4 + length
    /// </remarks>
    public static class KeyValueCodec
    {
        #region 常量

        /// <summary>KeyValueParam固定头大小（key + length = 2+2 = 4字节）</summary>
        public const int KvHeaderSize = 4;

        /// <summary>在#pragma pack(1)下 sizeof(LivoxLidarKeyValueParam)的值（2+2+1=5）</summary>
        /// 用于C++中value长度=1时的简单参数赋值（如工作模式、启用/禁用等）
        public const int KvSizeWithOneByteValue = 5;

        #endregion

        #region 编码单个KeyValue

        /// <summary>
        /// 编码单个KeyValue参数
        /// 将 (key, value字节数组) 编码为二进制格式
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值的字节表示</param>
        /// <returns>编码后的字节数组（4 + value.Length 字节）</returns>
        public static byte[] Encode(KeyType key, byte[] value)
        {
#if NET45_OR_GREATER
            if (value == null)
                value = new byte[0];
#elif NET9_0_OR_GREATER
            value ??= [];
#endif


            byte[] result = new byte[KvHeaderSize + value.Length];
            SdkPacketBuilder.WriteUInt16Le(result, 0, (ushort)key);     // key
            SdkPacketBuilder.WriteUInt16Le(result, 2, (ushort)value.Length); // length
            if (value.Length > 0)
                Buffer.BlockCopy(value, 0, result, KvHeaderSize, value.Length);
            return result;
        }

        /// <summary>
        /// 编码单个KeyValue参数（简单uint8值）
        /// 适用于工作模式、启用/禁用开关等只需1个字节的参数
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值（单字节）</param>
        /// <returns>编码后的字节数组（5字节）</returns>
        public static byte[] EncodeByte(KeyType key, byte value)
        {
            return Encode(key,
#if NET45_OR_GREATER
                 new byte[] { value }
#elif NET9_0_OR_GREATER
                 [value]
#endif
                );
        }

        /// <summary>
        /// 编码单个KeyValue参数（uint32值）
        /// 适用于盲区设置等4字节参数
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值（4字节无符号整数）</param>
        /// <returns>编码后的字节数组（8字节）</returns>
        public static byte[] EncodeUInt32(KeyType key, uint value)
        {
            byte[] valueBytes = new byte[4];
            SdkPacketBuilder.WriteUInt32Le(valueBytes, 0, value);
            return Encode(key, valueBytes);
        }

        /// <summary>
        /// 编码单个KeyValue参数（int32值）
        /// 适用于盲区设置等4字节参数
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值（4字节有符号整数）</param>
        /// <returns>编码后的字节数组（8字节）</returns>
        public static byte[] EncodeInt32(KeyType key, int value)
        {
            byte[] valueBytes = new byte[4];
            SdkPacketBuilder.WriteInt32Le(valueBytes, 0, value);
            return Encode(key, valueBytes);
        }

        #endregion

        #region 编码多个KeyValue（命令数据段）

        /// <summary>
        /// 编码WorkModeControl命令的数据段
        /// 对应C++ command_impl.cpp 中各 Set 函数的数据段构建逻辑
        /// 
        /// 数据段格式：
        /// [0-1]  key_num  : uint16_le  — KeyValue参数个数
        /// [2-3]  rsvd     : uint16_le  — 保留字段，始终为0
        /// [4+]   kv_list  : KeyValue[] — 连续排列的KeyValueParam块
        /// </summary>
        /// <param name="keyValues">KeyValue参数数组</param>
        /// <returns>完整的数据段字节数组</returns>
        public static byte[] EncodeWorkModeControlData(byte[][] keyValues)
        {
            if (keyValues == null || keyValues.Length == 0)
                throw new ArgumentException("至少需要一个KeyValue参数");

            // key_num（2字节）+ rsvd（2字节）= 4字节头
            int headerSize = 4;
            int totalKvSize = 0;
            foreach (var kv in keyValues)
                totalKvSize += kv.Length;

            byte[] data = new byte[headerSize + totalKvSize];

            // 写入 key_num
            SdkPacketBuilder.WriteUInt16Le(data, 0, (ushort)keyValues.Length);
            // 写入 rsvd（保留字段，始终为0，new byte[]已初始化为0）
            // 偏移2-3已为0

            // 写入KeyValue列表
            int offset = headerSize;
            foreach (var kv in keyValues)
            {
                Buffer.BlockCopy(kv, 0, data, offset, kv.Length);
                offset += kv.Length;
            }

            return data;
        }

        /// <summary>
        /// 编码GetInternalInfo查询请求的数据段（cmd_id: 0x0101 Request）
        /// 
        /// 查询命令的key_list格式与配置命令(0x0100)不同：
        /// - 0x0100 Request: key_value_list 每项为 {key(2) + length(2) + value(N)}
        /// - 0x0101 Request: key_list 每项仅为 {key(2)}，无length和value字段
        /// 
        /// 数据段格式：
        /// [0-1]  key_num  : uint16_le  — 要查询的key数量
        /// [2-3]  rsvd     : uint16_le  — 保留字段，始终为0
        /// [4+]   key_list : uint16_t[] — 连续排列的key编号，每个占2字节
        /// </summary>
        /// <param name="keys">要查询的参数键列表</param>
        /// <returns>查询请求的数据段字节数组</returns>
        public static byte[] EncodeQueryData(KeyType[] keys)
        {
            if (keys == null || keys.Length == 0)
                throw new ArgumentException("至少需要一个查询键");

            // key_num（2字节）+ rsvd（2字节）= 4字节头
            int headerSize = 4;
            // 查询时每个key只占2字节（不含length和value）
            int totalKeySize = keys.Length * 2;

            byte[] data = new byte[headerSize + totalKeySize];

            // 写入 key_num
            SdkPacketBuilder.WriteUInt16Le(data, 0, (ushort)keys.Length);
            // rsvd已为0

            // 写入key列表（每个只写2字节的key值）
            int offset = headerSize;
            foreach (var key in keys)
            {
                SdkPacketBuilder.WriteUInt16Le(data, offset, (ushort)key);
                offset += 2;
            }

            return data;
        }

        #endregion

        #region 解码KeyValue

        /// <summary>
        /// 从字节数组解码KeyValueParam
        /// 包含边界检查，防止数据不完整时越界访问
        /// </summary>
        /// <param name="data">数据字节</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="key">输出的参数键</param>
        /// <param name="value">输出的参数值</param>
        /// <returns>此KeyValueParam占用的总字节数（4 + value.Length）</returns>
        public static int DecodeKeyValue(byte[] data, int offset, out KeyType key, out byte[] value)
        {
            // 边界检查：确保至少能读取key(2)和length(2)
            if (data == null || offset < 0 || offset + KvHeaderSize > data.Length)
            {
                key = (KeyType)0xFFFF;
#if NET45_OR_GREATER
                value = new byte[0];
#elif NET9_0_OR_GREATER
                value = [];
#endif
                return 0;
            }

            ushort keyVal = BitConverter.ToUInt16(data, offset);
            key = (KeyType)keyVal;
            ushort length = BitConverter.ToUInt16(data, offset + 2);

            // 边界检查：确保value部分数据完整
            if (offset + KvHeaderSize + length > data.Length)
            {
#if NET45_OR_GREATER
                value = new byte[0];
#elif NET9_0_OR_GREATER
                value = [];
#endif
                return data.Length - offset;
            }

            if (length == 0)
            {
#if NET45_OR_GREATER
                value = new byte[0];
#elif NET9_0_OR_GREATER
                value = [];
#endif
            }
            else
            {
                value = new byte[length];
                Buffer.BlockCopy(data, offset + KvHeaderSize, value, 0, length);
            }

            return KvHeaderSize + length;
        }

        /// <summary>
        /// 从数据段解码所有KeyValueParam（用于解析0x0100/0x0102的ACK响应）
        /// 数据段格式：[key_num(2)] [rsvd(2)] [kv_list...]
        /// </summary>
        /// <param name="data">完整的数据段字节</param>
        /// <param name="paramNum">参数个数（从数据段头部读取）</param>
        /// <returns>键值对数组</returns>
        public static KeyValueResult[] DecodeAllKeyValues(byte[] data, int paramNum)
        {
            // 数据段格式：[key_num(2)] [rsvd(2)] [kv_list...]
            KeyValueResult[] results = new KeyValueResult[paramNum];
            int offset = 4; // 跳过 key_num(2) + rsvd(2)

            for (int i = 0; i < paramNum; i++)
            {
                int size = DecodeKeyValue(data, offset, out KeyType key, out byte[] value);
                results[i] = new KeyValueResult(key, value);
                offset += size;
            }

            return results;
        }

        /// <summary>
        /// 从0x0101查询ACK的data段解码所有KeyValueParam
        /// 协议格式：[ret_code(1)] [key_num(2)] [key_value_list...]
        /// 注意：0x0101 ACK没有rsvd字段，key_value_list从偏移3开始
        /// </summary>
        /// <param name="data">0x0101 ACK的完整data段字节（含ret_code）</param>
        /// <param name="paramNum">参数个数（从data段偏移1处读取）</param>
        /// <returns>键值对数组</returns>
        public static KeyValueResult[] DecodeAllKeyValuesForQueryAck(byte[] data, int paramNum)
        {
            // 0x0101 ACK data段格式：[ret_code(1)] [key_num(2)] [kv_list...]
            KeyValueResult[] results = new KeyValueResult[paramNum];
            int offset = 3; // 跳过 ret_code(1) + key_num(2)，无rsvd

            for (int i = 0; i < paramNum; i++)
            {
                int size = DecodeKeyValue(data, offset, out KeyType key, out byte[] value);
                results[i] = new KeyValueResult(key, value);
                offset += size;
            }

            return results;
        }

        #endregion

        #region 复合结构编码

        /// <summary>
        /// 编码雷达IP配置值（LivoxLidarIpInfoValue）
        /// 对应C++ build_request.cpp 中的 InitLidarIpinfoVal
        /// 总长度：12字节（IP 4 + 子网掩码 4 + 网关 4）
        /// </summary>
        /// <param name="ipAddr">雷达IP地址（点分十进制）</param>
        /// <param name="netMask">子网掩码（点分十进制）</param>
        /// <param name="gwAddr">网关地址（点分十进制）</param>
        /// <returns>12字节的IP配置值</returns>
        public static byte[] EncodeLidarIpInfoValue(string ipAddr, string netMask, string gwAddr)
        {
            byte[] result = new byte[12];
            byte[] ipBytes = SdkPacketBuilder.IpToBytes(ipAddr);
            byte[] maskBytes = SdkPacketBuilder.IpToBytes(netMask);
            byte[] gwBytes = SdkPacketBuilder.IpToBytes(gwAddr);
            Buffer.BlockCopy(ipBytes, 0, result, 0, 4);
            Buffer.BlockCopy(maskBytes, 0, result, 4, 4);
            Buffer.BlockCopy(gwBytes, 0, result, 8, 4);
            return result;
        }

        /// <summary>
        /// 编码主机IP配置值（HostIpInfoValue）
        /// 用于 kKeyStateInfoHostIpCfg / kKeyLidarPointDataHostIpCfg / kKeyLidarImuHostIpCfg
        /// 总长度：8字节（主机IP 4 + 主机端口 2 + 雷达端口 2）
        /// </summary>
        /// <param name="hostIp">主机IP地址（点分十进制）</param>
        /// <param name="hostPort">主机端口号</param>
        /// <param name="lidarPort">雷达端口号</param>
        /// <returns>8字节的主机IP配置值</returns>
        public static byte[] EncodeHostIpInfoValue(string hostIp, ushort hostPort, ushort lidarPort)
        {
            byte[] result = new byte[8];
            byte[] ipBytes = SdkPacketBuilder.IpToBytes(hostIp);
            Buffer.BlockCopy(ipBytes, 0, result, 0, 4);
            SdkPacketBuilder.WriteUInt16Le(result, 4, hostPort);
            SdkPacketBuilder.WriteUInt16Le(result, 6, lidarPort);
            return result;
        }

        /// <summary>
        /// 编码安装姿态（LivoxLidarInstallAttitude）
        /// 对应 kKeyInstallAttitude (0x0012)
        /// 总长度：24字节（3个float角度 + 3个int32偏移）
        /// </summary>
        /// <param name="roll">横滚角（度）</param>
        /// <param name="pitch">俯仰角（度）</param>
        /// <param name="yaw">偏航角（度）</param>
        /// <param name="x">X偏移（mm）</param>
        /// <param name="y">Y偏移（mm）</param>
        /// <param name="z">Z偏移（mm）</param>
        /// <returns>24字节的安装姿态值</returns>
        public static byte[] EncodeInstallAttitude(float roll, float pitch, float yaw, int x, int y, int z)
        {
            byte[] result = new byte[24];
            SdkPacketBuilder.WriteFloatLe(result, 0, roll);
            SdkPacketBuilder.WriteFloatLe(result, 4, pitch);
            SdkPacketBuilder.WriteFloatLe(result, 8, yaw);
            SdkPacketBuilder.WriteInt32Le(result, 12, x);
            SdkPacketBuilder.WriteInt32Le(result, 16, y);
            SdkPacketBuilder.WriteInt32Le(result, 20, z);
            return result;
        }

        /// <summary>
        /// 编码FOV配置（FovCfg）
        /// 对应 kKeyFovCfg0 (0x0015) / kKeyFovCfg1 (0x0016)
        /// 总长度：20字节
        /// </summary>
        /// <param name="yawStart">水平起始角（0.01度）</param>
        /// <param name="yawStop">水平结束角（0.01度）</param>
        /// <param name="pitchStart">垂直起始角（0.01度）</param>
        /// <param name="pitchStop">垂直结束角（0.01度）</param>
        /// <returns>20字节的FOV配置值</returns>
        public static byte[] EncodeFovConfig(int yawStart, int yawStop, int pitchStart, int pitchStop)
        {
            byte[] result = new byte[20];
            SdkPacketBuilder.WriteInt32Le(result, 0, yawStart);
            SdkPacketBuilder.WriteInt32Le(result, 4, yawStop);
            SdkPacketBuilder.WriteInt32Le(result, 8, pitchStart);
            SdkPacketBuilder.WriteInt32Le(result, 12, pitchStop);
            // 偏移16-19为rsvd，已初始化为0
            return result;
        }

        #endregion
    }

#if NET45_OR_GREATER
    /// <summary>
    /// 解码后的KeyValue结果
    /// </summary>
    public class KeyValueResult
    {
        /// <summary>参数键</summary>
        public KeyType Key { get; private set; }

        /// <summary>参数值的原始字节</summary>
        public byte[] Value { get; private set; }

        /// <summary>
        /// 构造KeyValueResult
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值字节</param>
        public KeyValueResult(KeyType key, byte[] value)
        {
            Key = key;
            Value = value;
        }
#elif NET9_0_OR_GREATER
    /// <summary>
    /// 解码后的KeyValue结果
    /// </summary>
    /// <remarks>
    /// 构造KeyValueResult
    /// </remarks>
    /// <param name="key">参数键</param>
    /// <param name="value">参数值字节</param>
    public class KeyValueResult(KeyType key, byte[] value)
    {
        /// <summary>参数键</summary>
        public KeyType Key { get; private set; } = key;

        /// <summary>参数值的原始字节</summary>
        public byte[] Value { get; private set; } = value;
#endif

        /// <summary>
        /// 将值作为uint8读取
        /// </summary>
        public byte AsByte()
        {
            return Value.Length > 0 ? Value[0] : (byte)0;
        }

        /// <summary>
        /// 将值作为uint32读取（小端序）
        /// </summary>
        public uint AsUInt32()
        {
            return Value.Length >= 4 ? BitConverter.ToUInt32(Value, 0) : 0;
        }

        /// <summary>
        /// 将值作为int32读取（小端序）
        /// </summary>
        public int AsInt32()
        {
            return Value.Length >= 4 ? BitConverter.ToInt32(Value, 0) : 0;
        }

        /// <summary>
        /// 将值作为float读取（IEEE 754）
        /// </summary>
        public float AsFloat()
        {
            return Value.Length >= 4 ? BitConverter.ToSingle(Value, 0) : 0f;
        }

        /// <summary>
        /// 将值作为4字节IP地址读取
        /// </summary>
        public string AsIpAddress()
        {
            if (Value.Length < 4) return "0.0.0.0";
            return string.Format("{0}.{1}.{2}.{3}", Value[0], Value[1], Value[2], Value[3]);
        }

        /// <summary>
        /// 将值作为ASCII字符串读取
        /// </summary>
        public string AsAsciiString()
        {
            // 找到第一个'\0'字符作为字符串结尾
            int len = Array.IndexOf(Value, (byte)0);
            if (len < 0) len = Value.Length;
            return System.Text.Encoding.ASCII.GetString(Value, 0, len);
        }
    }
}
