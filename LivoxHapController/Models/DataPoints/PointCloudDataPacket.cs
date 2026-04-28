using LivoxHapController.Enums;
#if NET45_OR_GREATER
using System.Collections.Generic;
#endif
using static LivoxHapController.Services.Parsers.PacketInformation;

namespace LivoxHapController.Models.DataPoints
{
    /// <summary>
    /// 完整的点云数据包
    /// 包含包头和所有数据点
    /// </summary>
    public class PointCloudDataPacket
    {
        /// <summary>
        /// 数据包头信息
        /// </summary>
        public PointCloudHeader Header { get; set; } = new PointCloudHeader();

        ///// <summary>
        ///// 数据点集合
        ///// 可能是IMU数据点或点云数据点
        ///// </summary>
        //public List<DataPoint> Points { get; set; }

        /// <summary>
        /// IMU数据点的集合，假如数据类型 <see cref="PointCloudHeader.DataType"/> 不是 <see cref="PointCloudDataType.ImuData"/>，则此集合为空
        /// </summary>
#if NET45_OR_GREATER
        public List<ImuDataPoint> ImuDataPoints { get; set; } = new List<ImuDataPoint>();
#elif NET9_0_OR_GREATER
        public List<ImuDataPoint> ImuDataPoints { get; set; } = [];
#endif

        /// <summary>
        /// 笛卡尔坐标系数据点的集合，假如数据类型 <see cref="PointCloudHeader.DataType"/> 不是 <see cref="PointCloudDataType.Cartesian16Bit"/>、<see cref="PointCloudDataType.Cartesian32Bit"/>中任何一种，则此集合为空
        /// </summary>
#if NET45_OR_GREATER
        public List<CartesianDataPoint> CartesianDataPoints { get; set; } = new List<CartesianDataPoint>();
#elif NET9_0_OR_GREATER
        public List<CartesianDataPoint> CartesianDataPoints { get; set; } = [];
#endif

        /// <summary>
        /// 获取时间戳类型描述
        /// </summary>
#if NET45_OR_GREATER
        public string TimeTypeDescription
        {
            get
            {
                switch (Header.TimeType)
                {
                    case TimeType.NoSync:
                        return "无同步源 (雷达开机时间)";
                    case TimeType.GptpSync:
                        return "gPTP同步 (主时钟源时间)";
                    default:
                        return "未知时间类型";
                }
            }
        }
#elif NET9_0_OR_GREATER
        public string TimeTypeDescription => Header.TimeType switch
        {
            TimeType.NoSync => "无同步源 (雷达开机时间)",
            TimeType.GptpSync => "gPTP同步 (主时钟源时间)",
            _ => "未知时间类型"
        };
#endif

        /// <summary>
        /// 获取数据类型描述
        /// </summary>
#if NET45_OR_GREATER
        public string DataTypeDescription
        {
            get
            {
                switch (Header.DataType)
                {
                    case PointCloudDataType.ImuData:
                        return "IMU数据";
                    case PointCloudDataType.Cartesian32Bit:
                        return "32位笛卡尔坐标点云";
                    case PointCloudDataType.Cartesian16Bit:
                        return "16位笛卡尔坐标点云";
                    default:
                        return "未知数据类型";
                }
            }
        }
#elif NET9_0_OR_GREATER
        public string DataTypeDescription => Header.DataType switch
        {
            PointCloudDataType.ImuData => "IMU数据",
            PointCloudDataType.Cartesian32Bit => "32位笛卡尔坐标点云",
            PointCloudDataType.Cartesian16Bit => "16位笛卡尔坐标点云",
            _ => "未知数据类型"
        };
#endif

        /// <summary>
        /// 获取包信息描述
        /// </summary>
        public string PackInfoDescription
        {
            get
            {
#if NET45_OR_GREATER
                string safetyInfo, tagType;
                //switch (Header.PackInfo & 0x03)
                //{
                //    case 0:
                //        safetyInfo = "整包可信";
                //        break;
                //    case 1:
                //        safetyInfo = "整包不可信";                        
                //        break;
                //    case 2:
                //        safetyInfo = "非0点可信";
                //        break;
                //    default:
                //        safetyInfo = "未知";
                //        break;
                //}
                //switch (Header.PackInfo >> 2 & 0x03)
                //{
                //    case 0:
                //        tagType = "固定标签类型0";
                //        break;
                //    default:
                //        tagType = "未知标签类型";
                //        break;
                //}
                switch (Header.SafetyInformation)
                {
                    case SafetyInformation.Valid:
                        safetyInfo = "整包可信";
                        break;
                    case SafetyInformation.Invalid:
                        safetyInfo = "整包不可信";
                        break;
                    case SafetyInformation.NonZeroValid:
                        safetyInfo = "非0点可信";
                        break;
                    default:
                        safetyInfo = "未知";
                        break;
                }
                switch (Header.TagType)
                {
                    case TagType.FixedType0:
                        tagType = "固定标签类型0";
                        break;
                    default:
                        tagType = "未知标签类型";
                        break;
                }
#elif NET9_0_OR_GREATER
                //var safetyInfo = (Header.PackInfo & 0x03) switch
                //{
                //    0 => "整包可信",
                //    1 => "整包不可信",
                //    2 => "非0点可信",
                //    _ => "未知"
                //};

                //var tagType = (Header.PackInfo >> 2 & 0x03) switch
                //{
                //    0 => "固定标签类型0",
                //    _ => "未知标签类型"
                //};
                var safetyInfo = (Header.SafetyInformation) switch
                {
                    SafetyInformation.Valid => "整包可信",
                    SafetyInformation.Invalid => "整包不可信",
                    SafetyInformation.NonZeroValid => "非0点可信",
                    _ => "未知"
                };

                var tagType = (Header.TagType) switch
                {
                    TagType.FixedType0 => "固定标签类型0",
                    _ => "未知标签类型"
                };
#endif

                return $"安全信息: {safetyInfo}, 标签类型: {tagType}";
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"PointCloudDataPacket {{ " +
                $"Header: {{ {Header} }}, " +
                //$"ImuDataPoints: {{ {string.Join(", ", ImuDataPoints)} }}, " +
                //$"CartesianDataPoints: {{ {string.Join(", ", CartesianDataPoints)} }}, " +
                $"ImuDataPointsLen: {ImuDataPoints.Count} , " +
                $"CartesianDataPointsLen: {CartesianDataPoints.Count}, " +
                $"TimeTypeDesc: {TimeTypeDescription}, " +
                $"DataTypeDesc: {DataTypeDescription}, " +
                $"PackInfoDesc: {PackInfoDescription} " +
                   $"}}";
        }
    }
}
