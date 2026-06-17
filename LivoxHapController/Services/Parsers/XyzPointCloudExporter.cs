#if NET45_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#endif
using LivoxHapController.Models.DataPoints;
using System.Globalization;

namespace LivoxHapController.Services.Parsers
{
    /// <summary>
    /// XYZ 点云文件导出器
    /// 将 CartesianDataPoint 集合导出为 .xyz 格式的点云文件
    /// 每行一个点，格式：X Y Z（单位毫米取整，空格分隔）
    /// </summary>
    /// <remarks>
    /// 输出格式：
    /// - 每行：x_mm y_mm z_mm
    /// - 坐标从米转换为毫米并取整（四舍五入）
    /// - 无列名行，纯数据
    /// 
    /// 使用示例：
    /// <code>
    /// XyzPointCloudExporter.Save(points, @"D:\点云\output.xyz");
    /// </code>
    /// </remarks>
    public static class XyzPointCloudExporter
    {
        #region 公共方法

        /// <summary>
        /// 将点云数据保存为 .xyz 文件
        /// 每行格式：x y z（毫米取整，空格分隔）
        /// </summary>
        /// <param name="points">点云数据集合</param>
        /// <param name="filePath">输出文件路径（建议后缀 .xyz）</param>
        /// <exception cref="ArgumentNullException">points 或 filePath 为 null</exception>
        public static void Save(this IEnumerable<CartesianDataPoint> points, string filePath)
        {
            Save(points, filePath, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 将点云数据保存为 .xyz 文件（指定区域性格式）
        /// 每行格式：x y z（毫米取整，空格分隔）
        /// </summary>
        /// <param name="points">点云数据集合</param>
        /// <param name="filePath">输出文件路径（建议后缀 .xyz）</param>
        /// <param name="culture">区域性信息（用于数值格式化）</param>
        /// <exception cref="ArgumentNullException">points 或 filePath 为 null</exception>
        public static void Save(this IEnumerable<CartesianDataPoint> points, string filePath, IFormatProvider culture)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            using (var writer = new StreamWriter(filePath, false))
            {
                SaveToWriter(points, writer, culture);
            }
        }

        /// <summary>
        /// 将点云数据写入流（每行格式：x y z，毫米取整）
        /// </summary>
        /// <param name="points">点云数据集合</param>
        /// <param name="stream">输出流</param>
        /// <param name="culture">区域性信息（可选，默认InvariantCulture）</param>
        /// <exception cref="ArgumentNullException">points 或 stream 为 null</exception>
        public static void Save(this IEnumerable<CartesianDataPoint> points, Stream stream, IFormatProvider
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  culture = null)
        {
#if NET45_OR_GREATER
            if (points == null)
                throw new ArgumentNullException(nameof(points));
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            culture = culture ?? CultureInfo.InvariantCulture;
#elif NET9_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(points);
            ArgumentNullException.ThrowIfNull(stream);

            culture ??= CultureInfo.InvariantCulture;
#endif

            using (var writer = new StreamWriter(stream))
            {
                SaveToWriter(points, writer, culture);
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 将点云数据逐行写入 StreamWriter
        /// </summary>
        private static void SaveToWriter(this IEnumerable<CartesianDataPoint> points, StreamWriter writer, IFormatProvider culture)
        {
            foreach (var point in points)
            {
                // 米 → 毫米，四舍五入取整
                int xMm = (int)Math.Round(point.X * 1000.0);
                int yMm = (int)Math.Round(point.Y * 1000.0);
                int zMm = (int)Math.Round(point.Z * 1000.0);

                // 格式：x y z（空格分隔，无多余空格）
                writer.WriteLine(string.Format(culture, "{0} {1} {2}", xMm, yMm, zMm));
            }
        }

        #endregion
    }
}
