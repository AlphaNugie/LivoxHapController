using LivoxHapController.Models;
using LivoxHapController.Models.DataPoints;
#if NET45_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
#endif

namespace LivoxHapController.Services
{
    /// <summary>
    /// 坐标变换工具类，用于处理Livox HAP激光雷达的旋转和平移变换
    /// </summary>
    public static class CoordinateTransformer
    {
        /// <summary>
        /// 将点云坐标系中的多个点批量变换到现实空间坐标系，并更新点云坐标（假如XYZ坐标均为0，则返回原坐标）
        /// </summary>
        /// <param name="pointCloud">高精度坐标点列表，坐标单位为毫米</param>
        /// <param name="paramSet">空间旋转位移参数集，包含Roll、Pitch、Yaw、X、Y、Z，前三者单位为度、后三者单位为毫米</param>
        public static CartesianDataPoint[] TransformPoints(this IEnumerable<CartesianDataPoint> pointCloud, CoordTransParamSet paramSet)
        {
#if NET9_0_OR_GREATER
            if (pointCloud == null)
                return [];

            return [.. pointCloud.Select(p =>
            {
                TransformPoint(ref p, paramSet);
                return p;
            })];
#elif NET45_OR_GREATER
            if (pointCloud == null)
                return new CartesianDataPoint[0];

            return pointCloud.Select(p =>
            {
                TransformPoint(ref p, paramSet);
                return p;
            }).ToArray();
#endif
        }

        /// <summary>
        /// 将点云坐标系中的点变换到现实空间坐标系，并更新点云坐标（假如XYZ坐标均为0，则返回原坐标）
        /// </summary>
        /// <param name="rawPoint">笛卡尔坐标系高精度点对象，坐标单位为毫米</param>
        /// <param name="paramSet">空间旋转位移参数集，包含Roll、Pitch、Yaw、X、Y、Z，前三者单位为度、后三者单位为毫米</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void TransformPoint(/*this */ref CartesianDataPoint rawPoint, CoordTransParamSet paramSet)
        {
            if (paramSet == null)
                throw new ArgumentNullException(nameof(paramSet), "空间旋转位移参数不能为空");
            var coord = TransformPoint(rawPoint.X, rawPoint.Y, rawPoint.Z, paramSet);
            rawPoint.UpdateCoordinates(coord[0], coord[1], coord[2]);
        }

        /// <summary>
        /// 将点云坐标系中的点变换到现实空间坐标系（假如XYZ坐标均为0，则返回原坐标）
        /// </summary>
        /// <param name="x">点云坐标系X坐标（单位：毫米）</param>
        /// <param name="y">点云坐标系Y坐标（单位：毫米）</param>
        /// <param name="z">点云坐标系Z坐标（单位：毫米）</param>
        /// <param name="paramSet">储存空间旋转位移参数的集合</param>
        /// <returns>变换后的现实空间坐标（单位：毫米），以数组方式返回，顺序为X、Y、Z</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static double[] TransformPoint(double x, double y, double z, CoordTransParamSet paramSet)
        {
            if (paramSet == null)
                throw new ArgumentNullException(nameof(paramSet), "空间旋转位移参数不能为空");
            return TransformPoint(x, y, z, paramSet.Roll, paramSet.Pitch, paramSet.Yaw, paramSet.X, paramSet.Y, paramSet.Z,
                                  paramSet.DevicePitch, paramSet.DeviceYaw);
        }

        /// <summary>
        /// 将点云坐标系中的点变换到现实空间坐标系（假如XYZ坐标均为0，则返回原坐标）
        /// <para/>横滚角Roll绕X轴，俯仰角Pitch绕Y轴，回转角Yaw绕Z轴，每次都绕空间中的固定轴旋转（绕动轴旋转需修改变换矩阵相乘的顺序由Z→Y→X变为X→Y→Z）
        /// <para/>三轴的正旋转方向均符合右手法则，即绕此轴旋转时，使右手大拇指伸直指向此轴正向、其余四指握成拳状，则其余四指所指的方向则为正向
        /// </summary>
        /// <param name="x">点云坐标系X坐标（单位：毫米）</param>
        /// <param name="y">点云坐标系Y坐标（单位：毫米）</param>
        /// <param name="z">点云坐标系Z坐标（单位：毫米）</param>
        /// <param name="rollDeg">绕X旋转的横滚角（度）</param>
        /// <param name="pitchDeg">绕Y旋转的俯仰角（度）</param>
        /// <param name="yawDeg">绕Z旋转的偏航角（度）</param>
        /// <param name="xoffset">设备固定的X方向平移量（单位：毫米）</param>
        /// <param name="yoffset">设备固定的Y方向平移量（单位：毫米）</param>
        /// <param name="zoffset">设备固定的Z方向平移量（单位：毫米）</param>
        /// <param name="devicePitchDeg">设备自身的俯仰角（度），设备俯仰运动引起的额外旋转</param>
        /// <param name="deviceYawDeg">设备自身的回转角（度），设备回转运动引起的额外旋转</param>
        /// <returns>变换后的现实空间坐标（单位：毫米），以数组方式返回，顺序为X、Y、Z</returns>
        /// <remarks>
        /// 变换顺序：先应用激光雷达安装旋转（Roll→Pitch→Yaw），再应用设备旋转（俯仰+回转），最后应用平移
        /// <para/>
        /// 矩阵乘法顺序与变换执行顺序相反：对于点变换 P' = R × P，最后应用的变换矩阵放在最左边。
        /// <para/>
        /// 变换执行顺序：点P → 安装旋转(R_install) → 设备旋转(R_device) → 最终点P'
        /// <para/>
        /// 数学表达式：P' = R_device × (R_install × P) = (R_device × R_install) × P
        /// <para/>
        /// 因此组合矩阵：R_total = R_device × R_install（设备旋转矩阵在左，安装旋转矩阵在右）
        /// </remarks>
        public static double[] TransformPoint(double x, double y, double z,
                                            double rollDeg, double pitchDeg, double yawDeg, double xoffset, double yoffset, double zoffset,
                                            double devicePitchDeg = 0, double deviceYawDeg = 0)
        {
            // 如果原坐标近似位于坐标系零点，不进行角度转换、直接输出三轴校正值
            if (Math.Sqrt(x * x + y * y + z * z) < 0.0001)
#if NET45_OR_GREATER
                return new double[] { xoffset, yoffset , zoffset };
#elif NET9_0_OR_GREATER
                return [xoffset, yoffset, zoffset];
#endif

            // 生成激光雷达安装角度的旋转矩阵，旋转正向符合右手法则
            double[,] rx = rollDeg.CreateRotationX();   // 绕X旋转矩阵（横滚）
            double[,] ry = pitchDeg.CreateRotationY();  // 绕Y旋转矩阵（俯仰）
            double[,] rz = yawDeg.CreateRotationZ();    // 绕Z旋转矩阵（偏航）

            // 生成设备运动角度的旋转矩阵
            double[,] rDevicePitch = devicePitchDeg.CreateRotationY();  // 设备俯仰旋转矩阵
            double[,] rDeviceYaw = deviceYawDeg.CreateRotationZ();      // 设备回转旋转矩阵

            ////// 绕动轴的组合旋转矩阵：R_total = Rx * Ry * Rz（矩阵乘法顺序对应旋转顺序）
            ////double[,] totalRotation = MultiplyMatrices(MultiplyMatrices(rx, ry), rz);

            // 激光雷达安装旋转的组合旋转矩阵（绕固定轴）：R_install = Rz * Ry * Rx
            // 变换顺序：Roll(X) → Pitch(Y) → Yaw(Z)，固定轴旋转的矩阵乘法顺序与此相反
            double[,] installRotation = MathUtils.MultiplyMatrices(rz, MathUtils.MultiplyMatrices(ry, rx));

            // 设备运动旋转的组合旋转矩阵（绕固定轴）：R_device = R_yaw * R_pitch
            // 变换顺序：Pitch → Yaw，固定轴旋转的矩阵乘法顺序与此相反
            double[,] deviceRotation = MathUtils.MultiplyMatrices(rDeviceYaw, rDevicePitch);

            // 总旋转矩阵：先安装旋转后设备旋转，矩阵相乘顺序为 R_device * R_install
            // P' = R_device × (R_install × P) = (R_device × R_install) × P
            // 矩阵乘法满足结合律：(A × B) × (C × D) 与 A × (B × (C × D)) 等价
            double[,] totalRotation = MathUtils.MultiplyMatrices(deviceRotation, installRotation);

            // 应用旋转到原始点坐标
#if NET9_0_OR_GREATER
            double[] originalPoint = [x, y, z];
#elif NET45_OR_GREATER
            double[] originalPoint = { x, y, z };
#endif
            double[] rotatedPoint = MathUtils.MultiplyMatrixVector(totalRotation, originalPoint);

            return
#if NET9_0_OR_GREATER
            [
                rotatedPoint[0] + xoffset,  // 变换后X坐标
                rotatedPoint[1] + yoffset,  // 变换后Y坐标
                rotatedPoint[2] + zoffset   // 变换后Z坐标
            ];
#elif NET45_OR_GREATER
                new double[]
                {
                    rotatedPoint[0] + xoffset,  // 变换后X坐标
                    rotatedPoint[1] + yoffset,  // 变换后Y坐标
                    rotatedPoint[2] + zoffset   // 变换后Z坐标
                };
#endif
        }
    }
}
