namespace LivoxHapController.Models.DataPoints
{
    /// <summary>
    /// 压缩格式（16位）笛卡尔坐标系点云数据结构
    /// 对应Point Cloud Data2格式（8字节）
    /// </summary>
    public class CartesianDataPoint16bit : CartesianDataPoint
    {
        private short _x10mm;
        /// <summary>
        /// X轴坐标 (2字节)
        /// 单位：10毫米(10mm)
        /// </summary>
        public short X_10mm
        {
            get { return _x10mm; }
            internal set
            {
                _x10mm = value;
                X = value / 100.0;
            }
        }

        private short _y10mm;
        /// <summary>
        /// Y轴坐标 (2字节)
        /// 单位：10毫米(10mm)
        /// </summary>
        public short Y_10mm
        {
            get { return _y10mm; }
            internal set
            {
                _y10mm = value;
                Y = value / 100.0;
            }
        }

        private short _z10mm;
        /// <summary>
        /// Z轴坐标 (2字节)
        /// 单位：10毫米(10mm)
        /// </summary>
        public short Z_10mm
        {
            get { return _z10mm; }
            internal set
            {
                _z10mm = value;
                Z = value / 100.0;
            }
        }

        ///// <summary>
        ///// X轴坐标 (2字节)
        ///// 单位：10毫米(10mm)
        ///// </summary>
        //public short X { get; internal set; }

        ///// <summary>
        ///// Y轴坐标 (2字节)
        ///// 单位：10毫米(10mm)
        ///// </summary>
        //public short Y { get; internal set; }

        ///// <summary>
        ///// Z轴坐标 (2字节)
        ///// 单位：10毫米(10mm)
        ///// </summary>
        //public short Z { get; internal set; }

        ///// <summary>
        ///// 反射率 (1字节)
        ///// 表示激光反射强度
        ///// </summary>
        //public byte Reflectivity { get; internal set; }

        ///// <summary>
        ///// 标签信息 (1字节)
        ///// 包含点的置信度等信息
        ///// </summary>
        //public byte TagInformation { get; internal set; }
    }
}