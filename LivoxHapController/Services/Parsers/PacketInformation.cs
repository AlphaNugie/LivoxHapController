namespace LivoxHapController.Services.Parsers
{
    /// <summary>
    /// 点云整包packet信息解析器
    /// 用于解析pack_info字节中的各个部分
    /// </summary>
    public static class PacketInformation
    {
        /// <summary>
        /// 功能安全信息
        /// </summary>
        public enum SafetyInformation : byte
        {
            /// <summary>整包可信</summary>
            Valid = 0x00, // 00
            /// <summary>整包不可信</summary>
            Invalid = 0x01,    // 01
            /// <summary>非0点可信</summary>
            NonZeroValid = 0x02,       // 10
        }

        /// <summary>
        /// Tag类型
        /// </summary>
        public enum TagType : byte
        {
            /// <summary> 固定类型0 </summary>
            FixedType0 = 0x00,
        }

        /// <summary>
        /// 解析pack_info信息
        /// </summary>
        /// <param name="packInfo">标签信息字节</param>
        /// <returns>包含所有功能安全、标签类型的元组</returns>
        public static (SafetyInformation, TagType) Parse(byte packInfo)
        {
            // 空间位置置信度 (bit0-1)
            var safetyInfo = (SafetyInformation)(packInfo & 0x03);

            // 标签类型 (bit2-3)
            var tagType = (TagType)(packInfo >> 2 & 0x03);

            return (safetyInfo, tagType);
        }
    }
}
