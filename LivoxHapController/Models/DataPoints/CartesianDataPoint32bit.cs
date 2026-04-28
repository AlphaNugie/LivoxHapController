namespace LivoxHapController.Models.DataPoints
{
    /// <summary>
    /// 标准格式（32位）笛卡尔坐标系点云数据结构
    /// 对应Point Cloud Data1格式（14字节）
    /// </summary>
    public class CartesianDataPoint32bit : CartesianDataPoint
    {
        private int _x1mm;
        /// <summary>
        /// X轴坐标 (4字节)
        /// 单位：毫米(mm)
        /// </summary>
        public int X_1mm
        {
            get { return _x1mm; }
            internal set
            {
                _x1mm = value;
                X = value / 1000.0;
            }
        }

        private int _y1mm;
        /// <summary>
        /// Y轴坐标 (4字节)
        /// 单位：毫米(mm)
        /// </summary>
        public int Y_1mm
        {
            get { return _y1mm; }
            internal set
            {
                _y1mm = value;
                Y = value / 1000.0;
            }
        }

        private int _z1mm;
        /// <summary>
        /// Z轴坐标 (4字节)
        /// 单位：毫米(mm)
        /// </summary>
        public int Z_1mm
        {
            get { return _z1mm; }
            internal set
            {
                _z1mm = value;
                Z = value / 1000.0;
            }
        }

        ///// <summary>
        ///// X轴坐标 (4字节)
        ///// 单位：毫米(mm)
        ///// </summary>
        //public int X { get; internal set; }

        ///// <summary>
        ///// Y轴坐标 (4字节)
        ///// 单位：毫米(mm)
        ///// </summary>
        //public int Y { get; internal set; }

        ///// <summary>
        ///// Z轴坐标 (4字节)
        ///// 单位：毫米(mm)
        ///// </summary>
        //public int Z { get; internal set; }

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