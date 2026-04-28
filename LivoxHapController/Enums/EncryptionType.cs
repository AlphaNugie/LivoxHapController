namespace LivoxHapController.Enums
{
    /// <summary>
    /// 加密类型枚举
    /// 定义固件传输中使用的加密算法
    /// </summary>
    public enum EncryptionType : byte
    {
        /// <summary>
        /// 无加密 (0x00)
        /// 固件数据不加密传输
        /// </summary>
        None = 0x00,

        /// <summary>
        /// AES128加密 (0x01)
        /// 使用AES-128算法加密固件数据
        /// </summary>
        Aes128 = 0x01,

        /// <summary>
        /// AES256加密 (0x02)
        /// 使用AES-256算法加密固件数据
        /// </summary>
        Aes256 = 0x02,

        /// <summary>
        /// DES加密 (0x03)
        /// 使用DES算法加密固件数据
        /// </summary>
        Des = 0x03,

        /// <summary>
        /// 3DES加密 (0x04)
        /// 使用3DES算法加密固件数据
        /// </summary>
        TripleDes = 0x04
    }
}