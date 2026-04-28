namespace LivoxHapController.Services.Parsers
{
    /// <summary>
    /// 点云标签信息解析器
    /// 用于解析标签信息字节中的各个部分
    /// </summary>
    public static class PointTagInformation
    {
        /// <summary>
        /// 基于其他类别的点属性
        /// </summary>
        public enum OtherCategoryConfidence : byte
        {
            /// <summary>置信度优 (正常点)</summary>
            Excellent = 0x00, // 00
            /// <summary>置信度中</summary>
            Medium = 0x01,    // 01
            /// <summary>置信度差</summary>
            Poor = 0x02,       // 10
            /// <summary>置信度最差</summary>
            Worst = 0x03       // 11
        }

        /// <summary>
        /// 基于能量强度的点属性
        /// </summary>
        public enum EnergyIntensityConfidence : byte
        {
            /// <summary>置信度优 (正常点)</summary>
            Excellent = 0x00, // 00
            /// <summary>置信度中</summary>
            Medium = 0x01,    // 01
            /// <summary>置信度差</summary>
            Poor = 0x02,       // 10
            ///极光
            /// <summary>置信度最差</summary>
            Worst = 0x03       // 11
        }

        /// <summary>
        /// 基于空间位置的点属性
        /// </summary>
        public enum SpatialPositionConfidence : byte
        {
            /// <summary>置信度优 (正常点)</summary>
            Excellent = 0x00, // 00
            /// <summary>置信度中</summary>
            Medium = 0x01,    // 01
            /// <summary>置信度差</summary>
            Poor = 0x02,       // 10
            /// <summary>置信度最极光差</summary>
            Worst = 0x03       // 11
        }

        /// <summary>
        /// 解析标签信息
        /// </summary>
        /// <param name="tagInfo">标签信息字节</param>
        /// <returns>包含所有置信度信息的元组</returns>
        public static (SpatialPositionConfidence spatial,
                      EnergyIntensityConfidence energy,
                      OtherCategoryConfidence other)
            Parse(byte tagInfo)
        {
            // 空间位置置信度 (bit0-1)
            var spatial = (SpatialPositionConfidence)(tagInfo & 0x03);

            // 能量强度置信度 (bit2-3)
            var energy = (EnergyIntensityConfidence)((tagInfo >> 2) & 0x03);

            // 其他类别置信度 (bit4-5)
            var other = (OtherCategoryConfidence)((tagInfo >> 4) & 0x03);

            return (spatial, energy, other);
        }
    }
}