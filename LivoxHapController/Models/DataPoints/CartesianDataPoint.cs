#if NET45_OR_GREATER
using System;
#endif
using static LivoxHapController.Services.Parsers.PointTagInformation;

namespace LivoxHapController.Models.DataPoints
{
    /// <summary>
    /// 笛卡尔坐标系点云数据结构
    /// Point Cloud Data1 和 Point Cloud Data2 格式的父类
    /// </summary>
    public class CartesianDataPoint : DataPoint
    {
        /// <summary>
        /// 时间戳 (8字节)
        /// 表示第一个点云的时间，单位：纳秒(ns)
        /// </summary>
        public ulong TimestampNanoSec { get; internal set; }

        /// <summary>
        /// X轴坐标，单位：米(m)
        /// </summary>
        //public double X { get; protected set; }
        public double X { get; internal set; }

        /// <summary>
        /// Y轴坐标，单位：米(m)，精度为小数点后3位（32位高精度点云）或2位（16位低精度点云）
        /// </summary>
        //public double Y { get; protected set; }
        public double Y { get; internal set; }

        /// <summary>
        /// Z轴坐标，单位：米(m)
        /// </summary>
        //public double Z { get; protected set; }
        public double Z { get; internal set; }

        /// <summary>
        /// 距离，单位：米(m)
        /// 距离公式：sqrt(X^2 + Y^2 + Z^2)
        /// </summary>
        public double Distance { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }

        /// <summary>
        /// 反射率 (1字节)
        /// 表示激光反射强度
        /// </summary>
        public byte Reflectivity { get; internal set; }

        private byte _tagInfo;
        /// <summary>
        /// 标签信息 (1字节)
        /// 包含点的置信度等信息
        /// </summary>
        public byte TagInformation
        {
            get { return _tagInfo; }
            internal set
            {
                _tagInfo = value;
                (SpatialPositionConfidence,
                      EnergyIntensityConfidence,
                      OtherCategoryConfidence) = Parse(value);
            }
        }

        /// <summary>
        /// 基于空间位置的点属性
        /// </summary>
        public SpatialPositionConfidence SpatialPositionConfidence { get; private set; }

        /// <summary>
        /// 基于能量强度的点属性
        /// </summary>
        public EnergyIntensityConfidence EnergyIntensityConfidence { get; private set; }

        /// <summary>
        /// 基于其他类别的点属性
        /// </summary>
        public OtherCategoryConfidence OtherCategoryConfidence { get; private set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"CartesianDataPoint {{ " +
                   $"Timestamp: {TimestampNanoSec}, " +
                   $"X: {X:F3}, " +
                   $"Y: {Y:F3}, " +
                   $"Z: {Z:F3}, " +
                   $"Reflectivity: {Reflectivity}, " +
                   $"TagInformation: {TagInformation}, " +
                   $"SpatialPositionConfidence: {SpatialPositionConfidence}, " +
                   $"EnergyIntensityConfidence: {EnergyIntensityConfidence}, " +
                   $"OtherCategoryConfidence: {OtherCategoryConfidence} " +
                   $"}}";
        }

        /// <summary>
        /// 更新XYZ坐标
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        public void UpdateCoordinates(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
