#if NET45_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
#endif
using LivoxHapController.Models.DataPoints;
using System.Globalization;

namespace LivoxHapController.Services.Parsers
{
    /// <summary>
    /// Livox Viewer CSV 点云文件导入器
    /// 将 Livox Viewer 导出的 CSV 格式点云文件转换为 CartesianDataPoint 列表
    /// </summary>
    /// <remarks>
    /// CSV 文件格式（Livox Viewer 导出标准格式）：
    /// - 第1行：列名（Header），包含 Version,Handle,...,X,Y,Z,Reflectivity,Tag,... 等列
    /// - 第2行：设备元数据行（SN、角度参数等），自动跳过
    /// - 第3行起：正式点云数据行
    /// 
    /// 使用示例：
    /// <code>
    /// var points = CsvPointCloudImporter.Load(@"D:\点云\frame.csv");
    /// </code>
    /// </remarks>
    public static class CsvPointCloudImporter
    {
        #region 列名关键字常量

        #region 原始坐标
        ///// <summary>X轴列名关键字</summary>
        //private const string ColumnX = "X";

        ///// <summary>Y轴列名关键字</summary>
        //private const string ColumnY = "Y";

        ///// <summary>Z轴列名关键字</summary>
        //private const string ColumnZ = "Z";
        #endregion

        #region 经外部参数转换后坐标
        /// <summary>X轴列名关键字</summary>
        private const string ColumnX = "Ori_x";

        /// <summary>Y轴列名关键字</summary>
        private const string ColumnY = "Ori_y";

        /// <summary>Z轴列名关键字</summary>
        private const string ColumnZ = "Ori_z";
        #endregion

        /// <summary>反射率列名关键字</summary>
        private const string ColumnReflectivity = "Reflectivity";

        /// <summary>标签信息列名关键字</summary>
        private const string ColumnTag = "Tag";

        #endregion

        #region 公共方法

        /// <summary>
        /// 从CSV文件加载点云数据
        /// 自动解析列名，查找X/Y/Z列并映射到 CartesianDataPoint 属性
        /// 第1行为列名，第2行为元数据行（自动跳过），第3行起为数据
        /// </summary>
        /// <param name="filePath">CSV文件路径</param>
        /// <returns>CartesianDataPoint 列表</returns>
        /// <exception cref="FileNotFoundException">文件不存在</exception>
        /// <exception cref="FormatException">CSV格式无效（缺少X/Y/Z列或数据解析失败）</exception>
        public static List<CartesianDataPoint> Load(string filePath)
        {
            return Load(filePath, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 从CSV文件加载点云数据（指定区域性格式）
        /// 自动解析列名，查找X/Y/Z列并映射到 CartesianDataPoint 属性
        /// 第1行为列名，第2行为元数据行（自动跳过），第3行起为数据
        /// </summary>
        /// <param name="filePath">CSV文件路径</param>
        /// <param name="culture">区域性信息（用于数值解析，如小数点符号）</param>
        /// <returns>CartesianDataPoint 列表</returns>
        /// <exception cref="FileNotFoundException">文件不存在</exception>
        /// <exception cref="FormatException">CSV格式无效（缺少X/Y/Z列或数据解析失败）</exception>
        public static List<CartesianDataPoint> Load(string filePath, IFormatProvider culture)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("CSV文件未找到", filePath);

            // 一次性读取所有行
            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length < 3)
                throw new FormatException("CSV文件数据不足：至少需要3行（列名行、元数据行、数据行）");

            // 第1行：解析列名并建立列索引映射
            string headerLine = lines[0];
            var columnMap = BuildColumnMap(headerLine);

            // 验证必需的X/Y/Z列是否存在
            if (!columnMap.X.HasValue)
                throw new FormatException("CSV列名中未找到X列，列名中必须包含\"X\"关键字");
            if (!columnMap.Y.HasValue)
                throw new FormatException("CSV列名中未找到Y列，列名中必须包含\"Y\"关键字");
            if (!columnMap.Z.HasValue)
                throw new FormatException("CSV列名中未找到Z列，列名中必须包含\"Z\"关键字");

            // 第2行：元数据行（SN、角度参数等），无条件跳过
            // Livox Viewer 导出的CSV第2行固定为设备元数据，不是点云数据

            // 第3行起：逐行解析数据
            var points = new List<CartesianDataPoint>(lines.Length - 2);

            for (int i = 2; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue; // 跳过空行

                try
                {
                    var point = ParseDataRow(line, columnMap, culture);
                    if (point != null)
                        points.Add(point);
                }
                catch (Exception ex)
                {
                    throw new FormatException(
                        string.Format("第{0}行数据解析失败：{1}", i + 1, ex.Message), ex);
                }
            }

            return points;
        }

        /// <summary>
        /// 从CSV文件流加载点云数据
        /// 适用于网络流、内存流等场景
        /// </summary>
        /// <param name="stream">CSV文件流</param>
        /// <param name="culture">区域性信息（可选，默认InvariantCulture）</param>
        /// <returns>CartesianDataPoint 列表</returns>
        /// <exception cref="FormatException">CSV格式无效</exception>
        public static List<CartesianDataPoint> Load(Stream stream, IFormatProvider
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  culture = null)
        {
#if NET45_OR_GREATER
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            culture = culture ?? CultureInfo.InvariantCulture;
#elif NET9_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(stream);

            culture ??= CultureInfo.InvariantCulture;
#endif

            using (var reader = new StreamReader(stream))
            {
                // 第1行：列名
                string
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  headerLine = reader.ReadLine() ?? throw new FormatException("CSV流为空，无法读取列名行");
                var columnMap = BuildColumnMap(headerLine);

                if (!columnMap.X.HasValue)
                    throw new FormatException("CSV列名中未找到X列");
                if (!columnMap.Y.HasValue)
                    throw new FormatException("CSV列名中未找到Y列");
                if (!columnMap.Z.HasValue)
                    throw new FormatException("CSV列名中未找到Z列");

                // 第2行：跳过
                reader.ReadLine();

                // 第3行起：逐行解析数据
                var points = new List<CartesianDataPoint>();
                int lineNumber = 3;
                string
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  dataLine;

                while ((dataLine = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(dataLine))
                    {
                        lineNumber++;
                        continue;
                    }

                    try
                    {
                        var point = ParseDataRow(dataLine, columnMap, culture);
                        if (point != null)
                            points.Add(point);
                    }
                    catch (Exception ex)
                    {
                        throw new FormatException(
                            string.Format("第{0}行数据解析失败：{1}", lineNumber, ex.Message), ex);
                    }

                    lineNumber++;
                }

                return points;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 解析CSV列名行，建立列名→列索引的映射
        /// 通过关键字模糊匹配查找X/Y/Z/Reflectivity/Tag列
        /// 支持列名中包含单位等额外信息（如"X(m)"、"Y (m)"、"X [mm]"等）
        /// </summary>
        /// <param name="headerLine">CSV第1行列名字符串</param>
        /// <returns>列索引映射</returns>
        private static ColumnIndexMap BuildColumnMap(string headerLine)
        {
            string[] columns = SplitCsvLine(headerLine);
            var map = new ColumnIndexMap();

            for (int i = 0; i < columns.Length; i++)
            {
                string col = columns[i].Trim();

                // 列名去掉空格/括号后等于关键字
                string normalized = col.Replace(" ", "").Replace("(", "").Replace(")", "")
                                      .Replace("[", "").Replace("]", "");

                if (!map.X.HasValue && normalized.StartsWith(ColumnX, StringComparison.OrdinalIgnoreCase))
                    map.X = i;
                else if (!map.Y.HasValue && normalized.StartsWith(ColumnY, StringComparison.OrdinalIgnoreCase))
                    map.Y = i;
                else if (!map.Z.HasValue && normalized.StartsWith(ColumnZ, StringComparison.OrdinalIgnoreCase))
                    map.Z = i;
                else if (!map.Reflectivity.HasValue &&
                         col.IndexOf(ColumnReflectivity, StringComparison.OrdinalIgnoreCase) >= 0)
                    map.Reflectivity = i;
                else if (!map.Tag.HasValue &&
                         col.IndexOf(ColumnTag, StringComparison.OrdinalIgnoreCase) >= 0)
                    map.Tag = i;
            }

            return map;
        }

        /// <summary>
        /// 解析单行数据并创建 CartesianDataPoint 实例
        /// </summary>
        /// <param name="dataLine">数据行字符串</param>
        /// <param name="columnMap">列索引映射</param>
        /// <param name="culture">数值解析的区域性格式</param>
        /// <returns>CartesianDataPoint 实例，解析失败时返回null</returns>
        private static CartesianDataPoint
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  ParseDataRow(string dataLine, ColumnIndexMap columnMap, IFormatProvider culture)
        {
            string[] fields = SplitCsvLine(dataLine);

            if (!columnMap.X.HasValue)
            {
                // 处理 columnMap.X 为 null 的情况
                // 例如，返回 null 或者抛出异常
                return null;
            }

            // 同理检查 Y 和 Z
            if (!columnMap.Y.HasValue) return null;
            if (!columnMap.Z.HasValue) return null;

            // 验证列数：至少需要包含X/Y/Z列
            int minRequired = Math.Max(
                Math.Max(columnMap.X.Value, columnMap.Y.Value),
                columnMap.Z.Value) + 1;

            if (fields.Length < minRequired)
                return null;

            // 解析X/Y/Z坐标（必需字段）
            double x = ParseDoubleField(fields, columnMap.X.Value, culture);
            double y = ParseDoubleField(fields, columnMap.Y.Value, culture);
            double z = ParseDoubleField(fields, columnMap.Z.Value, culture);

            var point = new CartesianDataPoint
            {
                X = x,
                Y = y,
                Z = z
            };

            // 解析反射率（可选字段）
            if (columnMap.Reflectivity.HasValue)
            {
                byte reflectivity = ParseByteField(fields, columnMap.Reflectivity.Value, culture);
                point.Reflectivity = reflectivity;
            }

            // 解析标签信息（可选字段）
            if (columnMap.Tag.HasValue)
            {
                byte tag = ParseByteField(fields, columnMap.Tag.Value, culture);
                point.TagInformation = tag;
            }

            return point;
        }

        /// <summary>
        /// 将CSV行分割为字段数组
        /// 支持引号包裹的字段（处理字段内包含逗号的情况）
        /// </summary>
        /// <param name="line">CSV行字符串</param>
        /// <returns>字段字符串数组</returns>
        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            int startIndex = 0;

            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (line[i] == ',' && !inQuotes)
                {
                    fields.Add(line.Substring(startIndex, i - startIndex).Trim('"'));
                    startIndex = i + 1;
                }
            }

            // 最后一个字段
            fields.Add(line.Substring(startIndex).Trim('"'));

            return fields.ToArray();
        }

        /// <summary>
        /// 从字段数组中解析 double 值
        /// </summary>
        /// <param name="fields">字段数组</param>
        /// <param name="index">列索引</param>
        /// <param name="culture">区域性格式</param>
        /// <returns>解析后的double值</returns>
        /// <exception cref="FormatException">字段值无法解析为double</exception>
        private static double ParseDoubleField(string[] fields, int index, IFormatProvider culture)
        {
            string value = fields[index].Trim();
            if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, culture, out double result))
                return result;

            throw new FormatException(
                string.Format("列[{0}]的值\"{1}\"无法解析为数值", index, value));
        }

        /// <summary>
        /// 从字段数组中解析 byte 值
        /// </summary>
        /// <param name="fields">字段数组</param>
        /// <param name="index">列索引</param>
        /// <param name="culture">区域性格式</param>
        /// <returns>解析后的byte值</returns>
        private static byte ParseByteField(string[] fields, int index, IFormatProvider culture)
        {
            string value = fields[index].Trim();
            if (byte.TryParse(value, NumberStyles.Integer, culture, out byte result))
                return result;

            // 兼容浮点数格式的反射率（向下取整）
            if (double.TryParse(value, NumberStyles.Float, culture, out double dResult))
                return (byte)Math.Min(255, Math.Max(0, dResult));

            return 0;
        }

        #endregion

        #region 内部数据结构

        /// <summary>
        /// CSV列索引映射
        /// 记录各已知列在CSV行中的位置索引
        /// </summary>
        private struct ColumnIndexMap
        {
            /// <summary>X轴列索引</summary>
            public int? X;

            /// <summary>Y轴列索引</summary>
            public int? Y;

            /// <summary>Z轴列索引</summary>
            public int? Z;

            /// <summary>反射率列索引</summary>
            public int? Reflectivity;

            /// <summary>标签信息列索引</summary>
            public int? Tag;
        }

        #endregion
    }
}
