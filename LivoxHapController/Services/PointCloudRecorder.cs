#if NET45_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
#endif
using System.Diagnostics;

using System.Linq.Expressions;

namespace LivoxHapController.Services
{
    /// <summary>
    /// 点云数据录制器
    /// 将扫描仪原始 UDP 点云数据包按接收顺序写入 .pcr 文件
    /// 支持暂停/恢复、最大时长/截止时间限制、属性绑定暂停
    /// </summary>
    /// <remarks>
    /// 文件格式 (.pcr)：
    /// 每包结构：SegmentFlag(4B) + TimestampTicks(8B) + DataLength(4B) + RawData(N B)
    /// - SegmentFlag: 0xFFFFFFFF=新段开始，0x00000000=普通包
    /// - TimestampTicks: DateTime.UtcNow.Ticks，仅新段标识时有效
    /// - DataLength: 后续原始数据长度
    /// - RawData: 原始 UDP 点云数据
    /// </remarks>
    public class PointCloudRecorder : IDisposable
    {
        #region 常量

        /// <summary>新段开始标识</summary>
        private const uint SegmentFlagNew = 0xFFFFFFFF;

        /// <summary>普通数据包标识</summary>
        private const uint SegmentFlagData = 0x00000000;

        #endregion

        #region 私有字段

        /// <summary>文件写入流</summary>
#if NET9_0_OR_GREATER
        private FileStream? _fileStream;
        private BinaryWriter? _writer;
        private Timer? _pauseBindingTimer;
        private Timer? _limitCheckTimer;
        private Timer? _delayStopTimer;
#elif NET45_OR_GREATER
        private FileStream _fileStream;
        private BinaryWriter _writer;
        private Timer _pauseBindingTimer;
        private Timer _limitCheckTimer;
        private Timer _delayStopTimer;
#endif

        /// <summary>同步锁</summary>
#if NET9_0_OR_GREATER
        private readonly Lock _syncRoot = new();
#elif NET45_OR_GREATER
        private readonly object _syncRoot = new object();
#endif

        /// <summary>是否正在录制</summary>
        private volatile bool _isRecording;

        /// <summary>是否已暂停</summary>
        private volatile bool _isPaused;

        /// <summary>是否处于滚动缓冲模式（数据在内存中，尚未落盘）</summary>
        private volatile bool _isBuffering;

        /// <summary>是否已释放</summary>
        private volatile bool _disposed;

        /// <summary>累计录制包数</summary>
        private int _totalPackets;

        /// <summary>当前录制段起始时间</summary>
        private DateTime _segmentStartTime;

        /// <summary>实际录制时长计时器（排除暂停时间），仅在非暂停时运行</summary>
        private Stopwatch
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _activeRecordingStopwatch;

        /// <summary>暂停绑定信息</summary>
        private PauseBinding
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _pauseBinding;

        /// <summary>滚动缓冲帧队列（存储时间戳+原始数据），仅在缓冲模式使用</summary>
#if NET9_0_OR_GREATER
        private Queue<(DateTime Timestamp, byte[] Data)>? _bufferFrames;
#elif NET45_OR_GREATER
        private Queue<Tuple<DateTime, byte[]>> _bufferFrames;
#endif

        /// <summary>滚动缓冲时长（秒），用于裁剪过期帧</summary>
        private double _bufferSeconds;

        #endregion

        #region 属性

        /// <summary>是否正在录制</summary>
        public bool IsRecording { get { return _isRecording; } }

        /// <summary>是否已暂停</summary>
        public bool IsPaused { get { return _isPaused; } }

        /// <summary>是否处于滚动缓冲模式（数据在内存中，尚未落盘）</summary>
        public bool IsBuffering { get { return _isBuffering; } }

        /// <summary>当前录制段开始时间</summary>
        public DateTime SegmentStartTime { get { return _segmentStartTime; } }

        /// <summary>累计录制包数</summary>
        public int TotalRecordedPackets { get { return _totalPackets; } }

        /// <summary>已录制总时长（排除暂停时间）；Stopwatch 暂停后 Elapsed 自动冻结，无需额外判断 IsRunning</summary>
        public TimeSpan Elapsed => _activeRecordingStopwatch?.Elapsed ?? TimeSpan.Zero;

        /// <summary>最大持续时长（超限自动停止），null=不限制</summary>
        public TimeSpan? MaxDuration { get; set; }

        /// <summary>截止时间（超限自动停止），null=不限制</summary>
        public DateTime? Deadline { get; set; }

        /// <summary>当前录制文件路径</summary>
        public string
#if NET9_0_OR_GREATER
            ?
#endif
            FilePath { get; private set; }

        #endregion

        #region 事件

        /// <summary>达到最大录制时长时触发</summary>
        public event EventHandler
#if NET9_0_OR_GREATER
            ?
#endif
            MaxDurationReached;

        /// <summary>达到截止时间时触发</summary>
        public event EventHandler
#if NET9_0_OR_GREATER
            ?
#endif
            DeadlineReached;

        /// <summary>录制停止时触发（含手动停止和自动停止）</summary>
        public event EventHandler
#if NET9_0_OR_GREATER
            ?
#endif
            RecordingStopped;

        /// <summary>StopWithDelay 延时到期自动停止时触发</summary>
        public event EventHandler
#if NET9_0_OR_GREATER
            ?
#endif
            DelayedStopTriggered;

        #endregion

        #region 录制控制

        /// <summary>
        /// 开始录制
        /// </summary>
        /// <param name="filePath">输出文件路径（建议后缀 .pcr）</param>
        /// <exception cref="InvalidOperationException">已在录制中或处于缓冲模式</exception>
        /// <exception cref="ArgumentNullException">filePath 为空</exception>
        public void Start(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (_isRecording)
                throw new InvalidOperationException("已在录制中，请先调用 Stop()");
            if (_isBuffering)
                throw new InvalidOperationException("当前处于滚动缓冲模式，请先调用 FlushBuffer() 或 Stop()");

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new BinaryWriter(_fileStream);
            FilePath = filePath;
            _totalPackets = 0;
            _isPaused = false;
            _isRecording = true;

            // 创建并启动实际录制时长计时器（排除暂停时间）
            _activeRecordingStopwatch = new Stopwatch();
            _activeRecordingStopwatch.Start();

            // 写入段开始标识+时间戳
            WriteSegmentHeader();

            // 启动时间限制检查（100ms 周期）
            if (MaxDuration != null || Deadline != null)
                _limitCheckTimer = new Timer(OnLimitCheck, null, 100, 100);

            // 如果有暂停绑定，启动属性轮询
            if (_pauseBinding != null)
                _pauseBindingTimer = new Timer(OnPauseBindingCheck, null, 100, 100);
        }

        /// <summary>
        /// 停止录制（同时清理滚动缓冲区和延时停止定时器）
        /// </summary>
        public void Stop()
        {
            lock (_syncRoot)
            {
                if (!_isRecording && !_isBuffering) return;

                // 取消延时停止定时器
                _delayStopTimer?.Dispose();
                _delayStopTimer = null;

                // 清空滚动缓冲区
                _bufferFrames?.Clear();

                _isRecording = false;
                _isBuffering = false;

                // 停止定时器
                _limitCheckTimer?.Dispose();
                _limitCheckTimer = null;
                _pauseBindingTimer?.Dispose();
                _pauseBindingTimer = null;

                // 停止实际录制时长计时器
                _activeRecordingStopwatch?.Stop();

                // 关闭文件流
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
                _fileStream?.Dispose();
                _fileStream = null;
            }

            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 暂停录制（同时暂停实际录制时长计时器）
        /// </summary>
        public void Pause()
        {
            _isPaused = true;
            _activeRecordingStopwatch?.Stop();
        }

        /// <summary>
        /// 恢复录制（写入新段标识+时间戳，同时恢复实际录制时长计时器）
        /// </summary>
        public void Resume()
        {
            if (!_isRecording || !_isPaused) return;

            _isPaused = false;
            _activeRecordingStopwatch?.Start();
            WriteSegmentHeader();
        }

        #endregion

        #region 数据写入

        /// <summary>
        /// 记录一个点云数据包
        /// 缓冲模式：入队到滚动缓冲区（不写盘）；录制模式：直接写入文件；否则忽略
        /// </summary>
        /// <param name="rawData">原始 UDP 点云数据</param>
        public void Record(byte[] rawData)
        {
            // 无效数据直接丢弃
            if (rawData == null || rawData.Length == 0)
                return;

            // 缓冲模式：入队到内存滚动缓冲区
            if (_isBuffering)
            {
                lock (_syncRoot)
                {
                    if (!_isBuffering || _bufferFrames == null) return;

#if NET9_0_OR_GREATER
                    _bufferFrames.Enqueue((DateTime.UtcNow, rawData));
#elif NET45_OR_GREATER
                    _bufferFrames.Enqueue(Tuple.Create(DateTime.UtcNow, rawData));
#endif
                    // 裁剪过期帧（时间戳早于 bufferSeconds 秒前的帧出队丢弃）
                    TrimExpiredFrames();
                }
                return;
            }

            // 正常录制模式：暂停状态或未录制时忽略
            if (!_isRecording || _isPaused) return;

            lock (_syncRoot)
            {
                if (!_isRecording || _writer == null) return;

                try
                {
                    // SegmentFlag: 普通数据包
                    _writer.Write(SegmentFlagData);
                    // TimestampTicks: 0（普通包不使用）
                    _writer.Write(0L);
                    // DataLength
                    _writer.Write(rawData.Length);
                    // RawData
                    _writer.Write(rawData);

                    _totalPackets++;
                }
                catch (ObjectDisposedException)
                {
                    // 文件流已关闭，忽略
                }
            }
        }

        #endregion

        #region 滚动缓冲与延时录制

        /// <summary>
        /// 启动滚动缓冲模式
        /// 数据持续写入内存缓冲区（不落盘），仅保留最近 bufferSeconds 秒的数据
        /// 调用后 IsBuffering 返回 true，IsRecording 返回 false（尚未落盘）
        /// </summary>
        /// <param name="bufferSeconds">缓冲时长（秒），典型值 5.0</param>
        /// <exception cref="InvalidOperationException">已在录制中或已在缓冲模式</exception>
        /// <exception cref="ArgumentException">bufferSeconds 小于等于 0</exception>
        public void StartBufferedRecording(double bufferSeconds)
        {
            if (bufferSeconds <= 0)
                throw new ArgumentException("缓冲时长必须大于0", nameof(bufferSeconds));
            if (_isRecording)
                throw new InvalidOperationException("已在录制中，请先调用 Stop()");
            if (_isBuffering)
                throw new InvalidOperationException("已处于滚动缓冲模式");

            _bufferSeconds = bufferSeconds;
#if NET9_0_OR_GREATER
            _bufferFrames = new Queue<(DateTime, byte[])>();
#elif NET45_OR_GREATER
            _bufferFrames = new Queue<Tuple<DateTime, byte[]>>();
#endif
            _isBuffering = true;
        }

        /// <summary>
        /// 将缓冲区中所有帧写入指定文件并切换为正常录制模式
        /// 缓冲区内帧按时间戳升序依次写入，之后新到达的帧直接追加到文件
        /// </summary>
        /// <param name="filePath">输出 .pcr 文件路径</param>
        /// <exception cref="InvalidOperationException">当前不在缓冲模式</exception>
        /// <exception cref="ArgumentNullException">filePath 为空</exception>
        public void FlushBuffer(string filePath)
        {
            if (!_isBuffering)
                throw new InvalidOperationException("当前不在滚动缓冲模式，请先调用 StartBufferedRecording()");
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            lock (_syncRoot)
            {
                if (!_isBuffering || _bufferFrames == null) return;

                // 创建输出目录
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 打开文件流
                _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                _writer = new BinaryWriter(_fileStream);
                FilePath = filePath;
                _totalPackets = 0;
                _isPaused = false;

                // 创建并启动录制时长计时器
                _activeRecordingStopwatch = new Stopwatch();
                _activeRecordingStopwatch.Start();

                // 写入段开始标识
                WriteSegmentHeader();

                // 将缓冲区中所有帧按时间戳升序写入文件
                while (_bufferFrames.Count > 0)
                {
#if NET9_0_OR_GREATER
                    var (_, data) = _bufferFrames.Dequeue();
#elif NET45_OR_GREATER
                    var frame = _bufferFrames.Dequeue();
                    var data = frame.Item2;
#endif
                    if (data != null && data.Length > 0)
                    {
                        _writer.Write(SegmentFlagData);
                        _writer.Write(0L);
                        _writer.Write(data.Length);
                        _writer.Write(data);
                        _totalPackets++;
                    }
                }

                // 切换为正常录制模式
                _isBuffering = false;
                _isRecording = true;

                // 启动时间限制检查
                if (MaxDuration != null || Deadline != null)
                    _limitCheckTimer = new Timer(OnLimitCheck, null, 100, 100);

                // 如果有暂停绑定，启动轮询
                if (_pauseBinding != null)
                    _pauseBindingTimer = new Timer(OnPauseBindingCheck, null, 100, 100);
            }
        }

        /// <summary>
        /// 延时停止录制
        /// 继续录制 delaySeconds 秒后自动调用 Stop()
        /// 延时期间 MaxDuration 和 Deadline 仍然生效（取最早触发者）
        /// </summary>
        /// <param name="delaySeconds">延时秒数，典型值 5.0</param>
        /// <exception cref="InvalidOperationException">当前不在录制模式</exception>
        /// <exception cref="ArgumentException">delaySeconds 小于等于 0</exception>
        public void StopWithDelay(double delaySeconds)
        {
            if (!_isRecording)
                throw new InvalidOperationException("当前不在录制模式，无法延时停止");
            if (delaySeconds <= 0)
                throw new ArgumentException("延时时长必须大于0", nameof(delaySeconds));

            // 取消已有的延时定时器（如果重复调用 StopWithDelay）
            _delayStopTimer?.Dispose();
            _delayStopTimer = null;

            // 如果 MaxDuration 和 Deadline 都会在延时之前到期，则不需要额外定时器
            // OnLimitCheck 已在 100ms 周期轮询中处理，会在更早的时间点触发 Stop()
            // 这里仍需启动延时定时器，作为兜底保障

            int delayMs = (int)(delaySeconds * 1000);
            _delayStopTimer = new Timer(OnDelayStopCallback, null, delayMs, Timeout.Infinite);
        }

        /// <summary>
        /// 延时停止定时器回调
        /// </summary>
        private void OnDelayStopCallback(object
#if NET9_0_OR_GREATER
            ?
#endif
            state)
        {
            if (!_isRecording) return;

            // 先触发事件，再停止（确保上层能在停止前收到通知）
            DelayedStopTriggered?.Invoke(this, EventArgs.Empty);
            Stop();
        }

        /// <summary>
        /// 裁剪滚动缓冲区中的过期帧
        /// 将时间戳早于 DateTime.UtcNow - bufferSeconds 的帧出队丢弃
        /// 需在持有 _syncRoot 锁的情况下调用
        /// </summary>
        private void TrimExpiredFrames()
        {
            if (_bufferFrames == null || _bufferFrames.Count == 0) return;

            var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(_bufferSeconds);
            while (_bufferFrames.Count > 0)
            {
#if NET9_0_OR_GREATER
                var (timestamp, _) = _bufferFrames.Peek();
#elif NET45_OR_GREATER
                var timestamp = _bufferFrames.Peek().Item1;
#endif
                if (timestamp < cutoff)
                    _bufferFrames.Dequeue();
                else
                    break;
            }
        }

        #endregion

        #region 暂停绑定

        /// <summary>
        /// 将暂停动作绑定到指定对象的任意返回 bool 的表达式
        /// 表达式返回 true → Pause()；返回 false → Resume()
        /// 支持属性、字段、方法调用、复杂表达式等任意返回 bool 的 Lambda
        /// 后续绑定覆盖前一个绑定
        /// <para/> 示例：BindPauseToProperty(viewModel, vm => vm.ShouldPause);
        /// <para/> 示例：BindPauseToProperty(viewModel, vm => vm._isPaused);
        /// <para/> 示例：BindPauseToProperty(viewModel, vm => vm.Status == StatusEnum.Paused);
        /// </summary>
        /// <typeparam name="T">目标对象类型</typeparam>
        /// <param name="target">目标对象实例</param>
        /// <param name="propertySelector">返回 bool 的表达式选择器，编译后直接调用，零反射开销</param>
        /// <exception cref="ArgumentNullException">target 或 propertySelector 为 null</exception>
        public void BindPauseToProperty<T>(T target, Expression<Func<T, bool>> propertySelector) where T : class
        {
#if NET45_OR_GREATER
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (propertySelector == null)
                throw new ArgumentNullException(nameof(propertySelector));
#elif NET9_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(propertySelector);
#endif

            // 编译表达式为委托，轮询时直接调用，无需反射
            Func<T, bool> compiledFunc = propertySelector.Compile();

            _pauseBinding = new PauseBinding
            {
                Target = target,
                // 闭包捕获 target，返回 bool 值（null 也转为 false，确保 LastValue 比较稳定）
                ValueGetter = () =>
                {
                    try { return compiledFunc(target); }
                    catch { return false; }
                },
                // 重要：初始值设为 null，首次检测时同步当前值，避免 LastValue=false/true 导致的误触发/不触发
                LastValue = null
            };

            // 如果已在录制，启动轮询
            if (_isRecording && _pauseBindingTimer == null)
            {
                _pauseBindingTimer = new Timer(OnPauseBindingCheck, null, 100, 100);
            }
        }

        /// <summary>
        /// 解除暂停绑定
        /// </summary>
        public void UnbindPause()
        {
            _pauseBinding = null;
            _pauseBindingTimer?.Dispose();
            _pauseBindingTimer = null;
        }

        /// <summary>
        /// 暂停绑定轮询回调（100ms）
        /// 直接调用编译后的 ValueGetter 委托取值，无需反射
        /// </summary>
        private void OnPauseBindingCheck(object
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            state)
        {
            if (_pauseBinding == null || _pauseBinding.ValueGetter == null || !_isRecording) return;

            try
            {
                // 直接调用编译后的委托获取当前 bool 值（支持属性/字段/方法/表达式，零反射开销）
                bool currentValue = _pauseBinding.ValueGetter();

                // 首次检测时同步初始值，避免 LastValue=null 导致的误触发
                if (!_pauseBinding.LastValue.HasValue)
                {
                    _pauseBinding.LastValue = currentValue;
                    return;
                }

                if (currentValue != _pauseBinding.LastValue.Value)
                {
                    _pauseBinding.LastValue = currentValue;
                    if (currentValue)
                        Pause();
                    else
                        Resume();
                }
            }
            catch
            {
                // 目标对象可能已释放，忽略异常
            }
        }

        #endregion

        #region 时间限制检查

        /// <summary>
        /// 时间限制检查回调（100ms）
        /// 使用 Stopwatch 计量实际录制时长（排除暂停时间），与 MaxDuration 比较
        /// </summary>
        private void OnLimitCheck(object
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            state)
        {
            if (!_isRecording) return;

            // 检查最大时长：使用 Stopwatch 的实际录制时长（已排除暂停时间）
            if (MaxDuration != null && _activeRecordingStopwatch != null)
            {
                if (_activeRecordingStopwatch.Elapsed >= MaxDuration)
                {
                    MaxDurationReached?.Invoke(this, EventArgs.Empty);
                    Stop();
                    return;
                }
            }

            // 检查截止时间（绝对时间，与暂停无关）
            if (Deadline != null && DateTime.Now >= Deadline)
            {
                DeadlineReached?.Invoke(this, EventArgs.Empty);
                Stop();
                return;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 写入新段标识头
        /// </summary>
        private void WriteSegmentHeader()
        {
            lock (_syncRoot)
            {
                if (_writer == null) return;

                _segmentStartTime = DateTime.UtcNow;
                _writer.Write(SegmentFlagNew);
                _writer.Write(_segmentStartTime.Ticks);
                _writer.Write(0); // DataLength=0 表示段头无数据
            }
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 释放资源（清理录制、缓冲、定时器等所有资源）
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            UnbindPause();

            // 清理延时停止定时器
            _delayStopTimer?.Dispose();
            _delayStopTimer = null;

            // 清空滚动缓冲区
            _bufferFrames?.Clear();
            _bufferFrames = null;

            // 添加此行以防止执行终结器
            GC.SuppressFinalize(this);
        }

        #endregion

        #region 内部数据结构

        /// <summary>
        /// 暂停绑定信息
        /// 使用编译后的委托代替反射，支持属性/字段/方法/任意 bool 表达式
        /// </summary>
        private class PauseBinding
        {
            /// <summary>绑定目标对象</summary>
            public object
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
 Target;

            /// <summary>编译后的 bool 取值委托（闭包捕获 Target，直接调用无反射开销）</summary>
            public Func<bool>
#if NET9_0_OR_GREATER
            ?
#endif
            ValueGetter;

            /// <summary>上次检测的 bool 值（null=尚未检测）</summary>
            public bool? LastValue;
        }

        #endregion
    }
}
