#if NET45_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
#endif

namespace LivoxHapController.Services
{
    /// <summary>
    /// 点云数据模拟播放器
    /// 从 .pcr 录制文件读取原始 UDP 数据包，按 4包/ms 速率注入到 UdpCommunicator
    /// 数据经过与网络接收完全一致的流程：InjectPointCloudData → PointCloudDataReceived → MergePointCloudData
    /// </summary>
    public class PointCloudPlayer : IDisposable
    {
        #region 常量

        /// <summary>新段开始标识</summary>
        private const uint SegmentFlagNew = 0xFFFFFFFF;

        /// <summary>普通数据包标识</summary>
        private const uint SegmentFlagData = 0x00000000;

        #endregion

        #region 私有字段

        /// <summary>播放定时器</summary>
#if NET9_0_OR_GREATER
        private Timer? _playbackTimer;
#elif NET45_OR_GREATER
        private Timer _playbackTimer;
#endif

        /// <summary>同步锁</summary>
#if NET9_0_OR_GREATER
        private readonly Lock _syncRoot = new();
#elif NET45_OR_GREATER
        private readonly object _syncRoot = new object();
#endif

        /// <summary>已解析的数据包列表（仅存储原始 byte[]）</summary>
        private List<byte[]>
 //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _packets;

        /// <summary>目标 UdpCommunicator</summary>
#if NET9_0_OR_GREATER
        private UdpCommunicator? _target;
#elif NET45_OR_GREATER
        private UdpCommunicator _target;
#endif

        /// <summary>是否正在播放</summary>
        private volatile bool _isPlaying;

        /// <summary>是否已暂停</summary>
        private volatile bool _isPaused;

        /// <summary>是否已释放</summary>
        private volatile bool _disposed;

        /// <summary>当前播放索引</summary>
        private int _currentIndex;

        #endregion

        #region 属性

        /// <summary>是否正在播放</summary>
        public bool IsPlaying { get { return _isPlaying; } }

        /// <summary>是否已暂停</summary>
        public bool IsPaused { get { return _isPaused; } }

        /// <summary>当前播放包索引</summary>
        public int CurrentPacketIndex { get { return _currentIndex; } }

        /// <summary>总包数</summary>
        public int TotalPackets { get { return _packets != null ? _packets.Count : 0; } }

        /// <summary>播放进度 (0.0 ~ 1.0)</summary>
        public double Progress
        {
            get
            {
                if (_packets == null || _packets.Count == 0) return 0;
                return (double)_currentIndex / _packets.Count;
            }
        }

        /// <summary>是否循环播放（默认 false，播放完停止）</summary>
        public bool Loop { get; set; }

        /// <summary>每毫秒播放的包数（默认4，可调速）</summary>
        public int PacketsPerMillisec { get; set; } = 4;

        #endregion

        #region 事件

        /// <summary>播放完成时触发</summary>
        public event EventHandler
#if NET9_0_OR_GREATER
            ?
#endif
            PlaybackCompleted;

        /// <summary>播放进度变更时触发</summary>
        public event EventHandler<PlaybackProgressEventArgs>
#if NET9_0_OR_GREATER
            ?
#endif
            ProgressChanged;

        #endregion

        #region 播放控制

        /// <summary>
        /// 开始模拟播放（可指定是否循环播放，默认不循环）
        /// 读取 .pcr 文件，按 PacketsPerMillisec/ms 速率注入数据，不循环自动停止，循环则播放完从头继续
        /// </summary>
        /// <param name="filePath">.pcr 录制文件路径</param>
        /// <param name="target">目标 UdpCommunicator（数据注入目标）</param>
        /// <param name="loop">是否循环播放，true=播放完从头开始</param>
        /// <exception cref="ArgumentNullException">filePath 或 target 为 null</exception>
        /// <exception cref="FileNotFoundException">文件不存在</exception>
        /// <exception cref="InvalidOperationException">已在播放中</exception>
        public void Start(string filePath, UdpCommunicator target, bool loop = false)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            _target = target ?? throw new ArgumentNullException(nameof(target));

            if (_isPlaying)
                throw new InvalidOperationException("已在播放中，请先调用 Stop()");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("录制文件未找到", filePath);

            // 读取并解析 .pcr 文件
            _packets = LoadPackets(filePath);
            if (_packets.Count == 0)
                throw new InvalidOperationException("录制文件中没有有效数据包");

            Loop = loop;
            _currentIndex = 0;
            _isPaused = false;
            _isPlaying = true;

            // 启动 1ms 定时器
            _playbackTimer = new Timer(OnPlaybackTick, null, 0, 1);
        }

        /// <summary>
        /// 停止播放
        /// </summary>
        public void Stop()
        {
            lock (_syncRoot)
            {
                if (!_isPlaying) return;

                _isPlaying = false;
                _isPaused = false;

                _playbackTimer?.Dispose();
                _playbackTimer = null;
            }

            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 暂停播放
        /// </summary>
        public void Pause()
        {
            _isPaused = true;
        }

        /// <summary>
        /// 恢复播放
        /// </summary>
        public void Resume()
        {
            _isPaused = false;
        }

        #endregion

        #region 播放回调

        /// <summary>
        /// 播放定时器回调（每1ms执行）
        /// </summary>
        private void OnPlaybackTick(object
 //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            state)
        {
            if (!_isPlaying || _isPaused || _target == null || _packets == null)
                return;

            try
            {
                // 每次取 PacketsPerMillisec 个包注入
                for (int i = 0; i < PacketsPerMillisec; i++)
                {
                    if (_currentIndex >= _packets.Count)
                    {
                        if (Loop)
                        {
                            // 循环播放：回到开头继续
                            _currentIndex = 0;
                            ProgressChanged?.Invoke(this, new PlaybackProgressEventArgs
                            {
                                CurrentIndex = 0,
                                TotalPackets = _packets.Count,
                                Progress = 0
                            });
                        }
                        else
                        {
                            Stop();
                        }
                        return;
                    }

                    byte[] data = _packets[_currentIndex];
                    _currentIndex++;

                    // 通过 InjectPointCloudData 注入，走与网络接收完全一致的流程
                    _target.InjectPointCloudData(data);
                }

                // 触发进度事件
                ProgressChanged?.Invoke(this, new PlaybackProgressEventArgs
                {
                    CurrentIndex = _currentIndex,
                    TotalPackets = _packets.Count,
                    Progress = Progress
                });
            }
            catch (ObjectDisposedException)
            {
                // 目标已释放，停止播放
                Stop();
            }
        }

        #endregion

        #region 文件读取

        /// <summary>
        /// 从 .pcr 文件读取所有数据包
        /// 段标识头自动跳过，仅提取普通数据包的 byte[]
        /// </summary>
        /// <param name="filePath">.pcr 文件路径</param>
        /// <returns>原始数据包列表</returns>
        private static List<byte[]> LoadPackets(string filePath)
        {
            var packets = new List<byte[]>();

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs))
            {
                while (fs.Position < fs.Length)
                {
                    if (fs.Position + 16 > fs.Length) break; // 至少需要 4+8+4=16 字节头

                    uint segmentFlag = reader.ReadUInt32();
                    //long timestampTicks = reader.ReadInt64();
                    int dataLength = reader.ReadInt32();

                    if (segmentFlag == SegmentFlagNew)
                    {
                        // 段头：dataLength=0，无数据，继续读下一个
                        // 读取段头的时间戳（可选，当前不使用）
                        continue;
                    }
                    else if (segmentFlag == SegmentFlagData)
                    {
                        // 普通数据包：读取 dataLength 字节
                        if (dataLength <= 0 || fs.Position + dataLength > fs.Length)
                            continue;

                        byte[] data = reader.ReadBytes(dataLength);
                        if (data.Length == dataLength)
                            packets.Add(data);
                    }
                }
            }

            return packets;
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            _packets?.Clear();
            _packets = null;
            _target = null;

            // 添加此行以防止执行终结器
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    #region 播放进度事件参数

    /// <summary>
    /// 播放进度事件参数
    /// </summary>
    public class PlaybackProgressEventArgs : EventArgs
    {
        /// <summary>当前包索引</summary>
        public int CurrentIndex { get; set; }

        /// <summary>总包数</summary>
        public int TotalPackets { get; set; }

        /// <summary>进度 (0.0 ~ 1.0)</summary>
        public double Progress { get; set; }
    }

    #endregion
}
