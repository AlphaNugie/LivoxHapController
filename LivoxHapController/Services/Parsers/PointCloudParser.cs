#if NET45_OR_GREATER
//using CommonLib.Function;
using System;
using System.Collections.Generic;
#elif NET9_0_OR_GREATER
using CommonLib.Helpers;
#endif
using LivoxHapController.Enums;
using LivoxHapController.Models.DataPoints;
using LivoxHapController.Utilities;

namespace LivoxHapController.Services.Parsers
{
    /// <summary>
    /// 点云数据包解析器
    /// 负责解析完整的点云数据包（包头+数据点）
    /// </summary>
    public static class PointCloudParser
    {
        /// <summary>
        /// 解析点云数据包
        /// </summary>
        /// <param name="dataStr">UDP接收的原始数据，16进制字符串格式</param>
        /// <returns>解析后的点云数据包信息</returns>
        public static PointCloudDataPacket ParsePacket(string dataStr)
        {
            // 校验数据长度
            if (string.IsNullOrWhiteSpace(dataStr))
                throw new ArgumentException("Invalid data length for point cloud packet");
            byte[] data = HexUtils.HexString2Bytes(dataStr);
            return ParsePacket(data);
        }

        /// <summary>
        /// 解析点云数据包
        /// </summary>
        /// <param name="data">UDP接收的原始数据</param>
        /// <returns>解析后的点云数据包信息</returns>
        public static PointCloudDataPacket ParsePacket(byte[] data)
        {
            // 校验数据长度
            if (data == null || data.Length < 36)
                throw new ArgumentException("Invalid data length for point cloud packet");
            //// 校验是否存在起始字节AA
            //if (!data.Any(b => b == 0xaa))
            //    throw new ArgumentException("Sof not found in point cloud packet");

            //int startIndex = Array.IndexOf(data, 0xaa) + 1;
            //// 校验起始字节sof（0xaa）的位置，不能是最后一个元素
            //if (startIndex >= data.Length)
            //    throw new ArgumentException("Sof not found in point cloud packet");
            //data = data.Skip(startIndex).ToArray(); // 截取有效数据部分

            // 解析包头
            var header = ParseHeader(data);

            // 解析数据点
            //var points = ParseDataPoints(data, header);
            List<ImuDataPoint> imuDataPoints;
            List<CartesianDataPoint> cartesianDataPoints;
            (imuDataPoints, cartesianDataPoints) = ParseDataPoints(data, header);

            return new PointCloudDataPacket
            {
                Header = header,
                //Points = points
                ImuDataPoints = imuDataPoints,
                CartesianDataPoints = cartesianDataPoints
            };
        }

        /// <summary>
        /// 解析包头信息
        /// </summary>
        /// <param name="data">UDP接收的原始数据</param>
        /// <returns>点云包头信息</returns>
        private static PointCloudHeader ParseHeader(byte[] data)
        {
            return new PointCloudHeader
            {
                Version = data[0],
                Length = BitConverter.ToUInt16(data, 1),
                TimeInterval_Point1MicroSec = BitConverter.ToUInt16(data, 3),
                DotNum = BitConverter.ToUInt16(data, 5),
                UdpCnt = BitConverter.ToUInt16(data, 7),
                FrameCnt = data[9],
                DataType = (PointCloudDataType)data[10],
                TimeType = (TimeType)data[11],
                PackInfo = data[12],
                //Reserved = new byte[11],
                Crc32 = BitConverter.ToUInt32(data, 24),
                TimestampNanoSec = BitConverter.ToUInt64(data, 28)
            };
        }

        /// <summary>
        /// 解析数据点部分
        /// </summary>
        /// <param name="data">UDP接收的原始数据</param>
        /// <param name="header">已解析的包头信息</param>
        /// <returns>返回IMU数据的集合与笛卡尔坐标点数据的集合（以元组的方式，根据数据类型，只有其中一个集合包含数据，另一个将为空集合）</returns>
        ///// <returns>数据点集合</returns>
        //private static List<DataPoint> ParseDataPoints(byte[] data, PointCloudHeader header)
        private static (List<ImuDataPoint>, List<CartesianDataPoint>) ParseDataPoints(byte[] data, PointCloudHeader header)
        {
            //var points = new List<DataPoint>();
            var imuPoints = new List<ImuDataPoint>();
            var cartesianPoints = new List<CartesianDataPoint>();
            int offset = 36; // 包头结束位置

            switch (header.DataType)
            {
                case PointCloudDataType.ImuData:
                    if (header.DotNum != 1)
                        throw new FormatException($"IMU data should have DotNum=1, got {header.DotNum}");

                    //points.Add(ParseImuDataPoint(data, offset));
                    imuPoints.Add(ParseImuDataPoint(data, offset, header.TimestampNanoSec));
                    break;

                case PointCloudDataType.Cartesian32Bit:
                    for (int i = 0; i < header.DotNum; i++)
                    {
                        //points.Add(ParseCartesian32Point(data, offset));
                        cartesianPoints.Add(ParseCartesian32Point(data, offset, header.TimestampNanoSec));
                        offset += 14; // 每个点14字节
                    }
                    break;

                case PointCloudDataType.Cartesian16Bit:
                    for (int i = 0; i < header.DotNum; i++)
                    {
                        //points.Add(ParseCartesian16Point(data, offset));
                        cartesianPoints.Add(ParseCartesian16Point(data, offset, header.TimestampNanoSec));
                        offset += 8; // 每个点8字节
                    }
                    break;

                default:
                    throw new NotSupportedException($"Unsupported data type: {header.DataType}");
            }

            //return points;
            return (imuPoints, cartesianPoints);
        }

        /// <summary>
        /// 解析IMU数据点
        /// </summary>
        private static ImuDataPoint ParseImuDataPoint(byte[] data, int offset, ulong timestamp = 0)
        {
            return new ImuDataPoint
            {
                TimestampNanoSec = timestamp,
                GyroX = BitConverter.ToSingle(data, offset),
                GyroY = BitConverter.ToSingle(data, offset + 4),
                GyroZ = BitConverter.ToSingle(data, offset + 8),
                AccX = BitConverter.ToSingle(data, offset + 12),
                AccY = BitConverter.ToSingle(data, offset + 16),
                AccZ = BitConverter.ToSingle(data, offset + 20)
            };
        }

        ///// <summary>
        ///// 解析32位笛卡尔坐标点
        ///// </summary>
        //private static CartesianDataPoint32bit ParseCartesian32Point(byte[] data, int offset)
        //{
        //    return new CartesianDataPoint32bit
        //    {
        //        //X = BitConverter.ToInt32(data, offset),
        //        //Y = BitConverter.ToInt32(data, offset + 4),
        //        //Z = BitConverter.ToInt32(data, offset + 8),
        //        X_1mm = BitConverter.ToInt32(data, offset),
        //        Y_1mm = BitConverter.ToInt32(data, offset + 4),
        //        Z_1mm = BitConverter.ToInt32(data, offset + 8),
        //        Reflectivity = data[offset + 12],
        //        TagInformation = data[offset + 13]
        //    };
        //}

        ///// <summary>
        ///// 解析16位笛卡尔坐标点
        ///// </summary>
        //private static CartesianDataPoint16bit ParseCartesian16Point(byte[] data, int offset)
        //{
        //    return new CartesianDataPoint16bit
        //    {
        //        //X = BitConverter.ToInt16(data, offset),
        //        //Y = BitConverter.ToInt16(data, offset + 2),
        //        //Z = BitConverter.ToInt16(data, offset + 4),
        //        X_10mm = BitConverter.ToInt16(data, offset),
        //        Y_10mm = BitConverter.ToInt16(data, offset + 2),
        //        Z_10mm = BitConverter.ToInt16(data, offset + 4),
        //        Reflectivity = data[offset + 6],
        //        TagInformation = data[offset + 7]
        //    };
        //}

        /// <summary>
        /// 解析32位笛卡尔坐标点
        /// </summary>
        private static CartesianDataPoint ParseCartesian32Point(byte[] data, int offset, ulong timestamp = 0)
        {
            return new CartesianDataPoint
            {
                TimestampNanoSec = timestamp,
                X = BitConverter.ToInt32(data, offset) / 1000.0,
                Y = BitConverter.ToInt32(data, offset + 4) / 1000.0,
                Z = BitConverter.ToInt32(data, offset + 8) / 1000.0,
                Reflectivity = data[offset + 12],
                TagInformation = data[offset + 13]
            };
        }

        /// <summary>
        /// 解析16位笛卡尔坐标点
        /// </summary>
        private static CartesianDataPoint ParseCartesian16Point(byte[] data, int offset, ulong timestamp = 0)
        {
            return new CartesianDataPoint
            {
                TimestampNanoSec = timestamp,
                X = BitConverter.ToInt16(data, offset) / 100.0f,
                Y = BitConverter.ToInt16(data, offset + 2) / 100.0f,
                Z = BitConverter.ToInt16(data, offset + 4) / 100.0f,
                Reflectivity = data[offset + 6],
                TagInformation = data[offset + 7]
            };
        }
    }
}