//using CommonLib.Helpers;
using LivoxHapController.Enums;
using LivoxHapController.Utilities;

#if NET45_OR_GREATER
using System;
#endif
using static LivoxHapController.Services.Parsers.PacketInformation;

namespace LivoxHapController.Models.DataPoints
{
    /// <summary>
    /// 点云数据包头结构
    /// 对应协议中从version到timestamp的字段
    /// </summary>
    public class PointCloudHeader
    {
        /// <summary>
        /// 包协议版本 (1字节)
        /// 当前固定为0
        /// </summary>
        public byte Version { get; internal set; }

        /// <summary>
        /// 整个UDP数据段长度 (2字节)
        /// 从version开始到数据结束的总长度
        /// </summary>
        public ushort Length { get; internal set; }

        private ushort _timeInterval_Point1NanoSec;
        /// <summary>
        /// 帧内点云采样时间 (2字节)
        /// 单位：0.1微秒
        /// 注：IMU数据此字段保留为0
        /// </summary>
        public ushort TimeInterval_Point1MicroSec
        {
            get { return _timeInterval_Point1NanoSec; }
            set
            {
                _timeInterval_Point1NanoSec = value;
                //TimeInterval = value / 10000000.0;
                TimeIntervalMicroSec = value / 10.0;
            }
        }

        /// <summary>
        /// 帧内点云采样时间，单位：微秒，精度为小数点后1位（达到0.1微秒）
        /// 注：IMU数据此字段保留为0
        /// </summary>
        public double TimeIntervalMicroSec { get; private set; }

        /// <summary>
        /// 当前包包含的点数 (2字节)
        /// 点云数据固定为96，IMU数据固定为1
        /// </summary>
        public ushort DotNum { get; internal set; }

        /// <summary>
        /// UDP包计数 (2字节)
        /// 每个UDP包依次加1，点云帧起始包清0
        /// </summary>
        public ushort UdpCnt { get; internal set; }

        /// <summary>
        /// 帧计数 (1字节)
        /// HAP设备此字段固定为0
        /// </summary>
        public byte FrameCnt { get; internal set; }

        /// <summary>
        /// 数据类型 (1字节)
        /// 0: IMU数据, 1: 32位点云数据, 2: 16位点云数据
        /// </summary>
        public PointCloudDataType DataType { get; internal set; }

        /// <summary>
        /// 时间戳类型 (1字节)
        /// 0: 无同步源, 1: gPTP同步
        /// </summary>
        public TimeType TimeType { get; internal set; }

        private byte _packInfo;
        /// <summary>
        /// 封装信息 (1字节)
        /// bit0-1: 功能安全信息 (0:整包可信, 1:整包不可信, 2:非0点可信)
        /// bit2-3: 标签类型 (HAP固定为0)
        /// bit4-7: 保留
        /// </summary>
        public byte PackInfo
        {
            get { return _packInfo; }
            internal set
            {
                _packInfo = value;
                (SafetyInformation, TagType) = Parse(value);
            }
        }

        /// <summary>
        /// 功能安全信息
        /// </summary>
        public SafetyInformation SafetyInformation { get; private set; }

        /// <summary>
        /// 标签类型
        /// </summary>
        public TagType TagType { get; private set; }

        /// <summary>
        /// 保留字段 (11字节)
        /// </summary>
        public byte[] Reserved { get; internal set; } = new byte[11];

        /// <summary>
        /// CRC32校验值 (4字节)
        /// 校验timestamp+data段
        /// </summary>
        public uint Crc32 { get; internal set; }

        private ulong _timestampNanoSec;
        /// <summary>
        /// 时间戳 (8字节)
        /// 表示第一个点云的时间，单位：纳秒(ns)
        /// </summary>
        public ulong TimestampNanoSec
        {
            get { return _timestampNanoSec; }
            internal set
            {
                _timestampNanoSec = value;
                Timestamp = DateTimeUtils.GetUtcTimeByTimeStampMillisec(value / 1000);
            }
        }

        /// <summary>
        /// 第一个点云的时间，单位：毫秒
        /// </summary>
        public DateTime Timestamp { get; private set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"PointCloudHeader {{ " +
                   $"Version: {Version}, " +
                   $"Length: {Length}, " +
                   //$"TimeInterval_Point1NanoSec: {TimeInterval_Point1MicroSec}, " +
                   $"TimeIntervalMicroSec: {TimeIntervalMicroSec:F3}μs, " +
                   $"DotNum: {DotNum}, " +
                   $"UdpCnt: {UdpCnt}, " +
                   $"FrameCnt: {FrameCnt}, " +
                   $"DataType: {DataType}, " +
                   $"TimeType: {TimeType}, " +
                   $"PackInfo: {PackInfo}, " +
                   $"SafetyInformation: {SafetyInformation}, " +
                   $"TagType: {TagType}, " +
                   //$"Reserved: {BitConverter.ToString(Reserved)}, " +
                   $"Crc32: {Crc32}, " +
                   $"TimestampNanoSec: {TimestampNanoSec}, " +
                   $"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss.fff} " +
                   $"}}";
        }
    }
}