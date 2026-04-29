using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LivoxHapController.Config;
using LivoxHapController.Enums;
using LivoxHapController.Models;
using LivoxHapController.Services;
using LivoxHapController.Services.Parsers;
using Microsoft.Win32;

namespace LivoxLidarConfigurator
{
    /// <summary>
    /// MainWindow 的交互逻辑
    /// 提供 Livox HAP LiDAR 设备发现、连接、配置、扫描的完整生命周期管理界面
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 字段

        /// <summary>LivoxHapRadar 上层管理类实例，封装设备发现、连接、配置、扫描的完整流程</summary>
        private LivoxHapRadar? _radar;

        /// <summary>已发现设备的可观察集合，用于 ListBox 数据绑定</summary>
        private readonly ObservableCollection<DeviceListItem> _devices = [];

        /// <summary>点云数据接收计数器，用于统计收到的点云包数</summary>
        private long _pclPacketCount;

        /// <summary>点云数据接收总字节数</summary>
        private long _pclTotalBytes;

        /// <summary>是否正在发现设备</summary>
        private bool _isDiscovering;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造 MainWindow
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            LstDevices.ItemsSource = _devices;
            Log("应用程序已启动，请先初始化并发现设备。");
        }

        #endregion

        #region 初始化与发现

        /// <summary>
        /// 浏览选择配置文件
        /// </summary>
        private void BtnBrowseConfig_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON配置文件|*.json|所有文件|*.*",
                Title = "选择配置文件"
            };
            if (dlg.ShowDialog() == true)
                TxtConfigFile.Text = dlg.FileName;
        }

        /// <summary>
        /// 初始化雷达管理器（从配置文件加载）
        /// </summary>
        private void BtnInitialize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _radar = new LivoxHapRadar();

                // 订阅事件
                SubscribeRadarEvents();

                // 从配置文件初始化
                _radar.Initialize(TxtConfigFile.Text.Trim());
                Log($"初始化成功，配置文件: {TxtConfigFile.Text.Trim()}");

                // 更新UI状态
                UpdateUiAfterInit("配置文件");
            }
            catch (Exception ex)
            {
                LogError($"初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 直接初始化雷达管理器（不依赖配置文件）
        /// 使用当前主机IP文本框的值和默认端口创建AppConfig进行初始化
        /// </summary>
        private void BtnInitDirect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string hostIp = TxtHostIp.Text.Trim();
                if (string.IsNullOrWhiteSpace(hostIp))
                {
                    LogError("请输入主机IP地址");
                    return;
                }

                _radar = new LivoxHapRadar();

                // 订阅事件
                SubscribeRadarEvents();

                // 使用 AppConfig 对象直接初始化，覆盖主机IP
                _radar.Initialize(
                    appConfig: new AppConfig(),
                    hostIp: hostIp
                );
                Log($"直接初始化成功，主机IP: {hostIp}");

                // 更新UI状态
                UpdateUiAfterInit("直接");
            }
            catch (Exception ex)
            {
                LogError($"直接初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 订阅雷达管理器事件
        /// 两种初始化方式共用此方法，避免重复代码
        /// </summary>
        private void SubscribeRadarEvents()
        {
            if (_radar == null) return;

            _radar.DeviceDiscovered += Radar_DeviceDiscovered;
            _radar.AckResponseReceived += Radar_AckResponseReceived;
            _radar.PushMessageReceived += Radar_PushMessageReceived;
            _radar.DeviceStatusUpdated += Radar_DeviceStatusUpdated;
            _radar.PointCloudDataReceived += Radar_PointCloudDataReceived;
            _radar.ImuDataReceived += Radar_ImuDataReceived;
        }

        /// <summary>
        /// 初始化成功后更新UI状态
        /// 两种初始化方式共用此方法
        /// </summary>
        /// <param name="initMode">初始化模式名称（用于日志）</param>
        private void UpdateUiAfterInit(string initMode)
        {
            BtnInitialize.IsEnabled = false;
            BtnInitDirect.IsEnabled = false;
            BtnDiscover.IsEnabled = true;
            TxtConfigFile.IsEnabled = false;
        }

        /// <summary>
        /// 开始广播发现设备
        /// </summary>
        private void BtnDiscover_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_radar == null || !_radar.IsInitialized)
                {
                    LogError("请先初始化");
                    return;
                }

                _radar.Discover(TxtHostIp.Text.Trim());
                _isDiscovering = true;

                BtnDiscover.IsEnabled = false;
                BtnStopDiscover.IsEnabled = true;
                Log($"开始发现设备，主机IP: {TxtHostIp.Text.Trim()}");
            }
            catch (Exception ex)
            {
                LogError($"启动发现失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止设备发现
        /// </summary>
        private void BtnStopDiscover_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isDiscovering = false;
                // 注意：当前LivoxHapRadar未提供单独停止发现的方法，停止需要Dispose
                BtnDiscover.IsEnabled = true;
                BtnStopDiscover.IsEnabled = false;
                Log("已停止发现新设备（已有设备仍可连接）");
            }
            catch (Exception ex)
            {
                LogError($"停止发现失败: {ex.Message}");
            }
        }

        #endregion

        #region 设备列表与连接

        /// <summary>
        /// 设备列表选中变更事件处理
        /// 更新设备信息面板和连接按钮状态
        /// </summary>
        private void LstDevices_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var item = LstDevices.SelectedItem as DeviceListItem;
            bool selected = item != null;

            BtnConnect.IsEnabled = selected && _radar is { IsConnected: false };
            UpdateDeviceInfoPanel(item?.DeviceInfo);
        }

        /// <summary>
        /// 连接到选中设备
        /// </summary>
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (LstDevices.SelectedItem is not DeviceListItem item || _radar == null) return;

                _radar.Connect(item.DeviceInfo);
                item.IsConnected = true;

                // 更新UI
                UpdateConnectionState(true);
                PnlDeviceInfo.IsEnabled = true;
                PnlConfig.IsEnabled = true;
                Log($"已连接设备: {item.DeviceInfo.LidarIpString} (SN: {item.DeviceInfo.SerialNumberString})");
            }
            catch (Exception ex)
            {
                LogError($"连接失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开当前设备连接
        /// </summary>
        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_radar == null) return;

                _radar.Disconnect();
                // 更新所有设备的连接状态
                foreach (var d in _devices)
                    d.IsConnected = false;

                UpdateConnectionState(false);
                PnlConfig.IsEnabled = false;
                Log("已断开设备连接");
            }
            catch (Exception ex)
            {
                LogError($"断开失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新连接状态的UI显示
        /// </summary>
        /// <param name="connected">是否已连接</param>
        private void UpdateConnectionState(bool connected)
        {
            ConnIndicator.Fill = connected ? Brushes.LimeGreen : Brushes.Gray;
            TxtConnStatus.Text = connected ? "已连接" : "未连接";
            TxtConnStatus.Foreground = connected ? Brushes.LimeGreen : Brushes.Gray;
            BtnConnect.IsEnabled = !connected && LstDevices.SelectedItem != null;
            BtnDisconnect.IsEnabled = connected;
        }

        /// <summary>
        /// 更新设备信息面板显示
        /// </summary>
        /// <param name="device">设备信息，为null时清空</param>
        private void UpdateDeviceInfoPanel(LidarDeviceInfo? device)
        {
            if (device == null)
            {
                TxtDeviceType.Text = "";
                TxtSerialNumber.Text = "";
                TxtLidarIp.Text = "";
                TxtCommandPort.Text = "";
                TxtDiscoveredTime.Text = "";
                TxtConnectedState.Text = "";
                return;
            }

            TxtDeviceType.Text = device.DeviceTypeName;
            TxtSerialNumber.Text = device.SerialNumberString;
            TxtLidarIp.Text = device.LidarIpString;
            TxtCommandPort.Text = device.CommandPort.ToString();
            TxtDiscoveredTime.Text = device.DiscoveredTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            TxtConnectedState.Text = device.IsConnected ? "已连接" : "未连接";
            TxtConnectedState.Foreground = device.IsConnected ? Brushes.LimeGreen : Brushes.Gray;
        }

        #endregion

        #region 扫描控制

        /// <summary>
        /// 启动正常扫描
        /// </summary>
        private void BtnStartScan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.StartScan();

                ScanIndicator.Fill = Brushes.LimeGreen;
                TxtScanStatus.Text = "扫描中";
                TxtScanStatus.Foreground = Brushes.LimeGreen;
                _pclPacketCount = 0;
                _pclTotalBytes = 0;
                Log("扫描已启动");
            }
            catch (Exception ex)
            {
                LogError($"启动扫描失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止扫描（进入休眠模式）
        /// </summary>
        private void BtnStopScan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.StopScan();

                ScanIndicator.Fill = Brushes.Gray;
                TxtScanStatus.Text = "未扫描";
                TxtScanStatus.Foreground = Brushes.Gray;
                Log("扫描已停止");
            }
            catch (Exception ex)
            {
                LogError($"停止扫描失败: {ex.Message}");
            }
        }

        #endregion

        #region 网络配置

        /// <summary>
        /// 手动应用网络配置
        /// </summary>
        private void BtnApplyNetwork_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.Configure(
                    TxtCfgHostIp.Text.Trim(),
                    ushort.Parse(TxtCfgCmdPort.Text.Trim()),
                    ushort.Parse(TxtCfgPointPort.Text.Trim()),
                    ushort.Parse(TxtCfgImuPort.Text.Trim()),
                    ushort.Parse(TxtCfgPushPort.Text.Trim())
                );
                Log($"网络配置已发送: HostIp={TxtCfgHostIp.Text.Trim()}");
            }
            catch (Exception ex)
            {
                LogError($"应用网络配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从配置文件应用网络配置
        /// </summary>
        private void BtnApplyConfigFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.ConfigureFromConfig();
                Log("已从配置文件应用网络配置");
            }
            catch (Exception ex)
            {
                LogError($"应用配置文件失败: {ex.Message}");
            }
        }

        #endregion

        #region 点云数据配置

        /// <summary>
        /// 设置点云数据类型
        /// </summary>
        private void BtnSetPclDataType_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                byte val = byte.Parse(((System.Windows.Controls.ComboBoxItem)CmbPclDataType.SelectedItem).Tag.ToString()!);
                _radar!.SetPclDataType(val);
                Log($"已设置点云数据类型: {CmbPclDataType.Text}");
            }
            catch (Exception ex)
            {
                LogError($"设置点云数据类型失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置扫描模式
        /// </summary>
        private void BtnSetScanPattern_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                byte val = byte.Parse(((System.Windows.Controls.ComboBoxItem)CmbScanPattern.SelectedItem).Tag.ToString()!);
                _radar!.SetScanPattern(val);
                Log($"已设置扫描模式: {CmbScanPattern.Text}");
            }
            catch (Exception ex)
            {
                LogError($"设置扫描模式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置双发射模式
        /// </summary>
        private void BtnSetDualEmit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                bool enable = ChkDualEmit.IsChecked == true;
                _radar!.SetDualEmit(enable);
                Log($"已设置双发射: {(enable ? "启用" : "禁用")}");
            }
            catch (Exception ex)
            {
                LogError($"设置双发射失败: {ex.Message}");
            }
        }

        #endregion

        #region IMU 控制

        /// <summary>
        /// 启用IMU数据
        /// </summary>
        private void BtnEnableImu_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.EnableImuData();
                Log("已发送启用IMU数据命令");
            }
            catch (Exception ex)
            {
                LogError($"启用IMU失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 禁用IMU数据
        /// </summary>
        private void BtnDisableImu_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.DisableImuData();
                Log("已发送禁用IMU数据命令");
            }
            catch (Exception ex)
            {
                LogError($"禁用IMU失败: {ex.Message}");
            }
        }

        #endregion

        #region 安装姿态

        /// <summary>
        /// 设置安装姿态（Roll/Pitch/Yaw角度 + X/Y/Z偏移）
        /// </summary>
        private void BtnSetAttitude_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.SetInstallAttitude(
                    float.Parse(TxtAttRoll.Text.Trim()),
                    float.Parse(TxtAttPitch.Text.Trim()),
                    float.Parse(TxtAttYaw.Text.Trim()),
                    int.Parse(TxtAttX.Text.Trim()),
                    int.Parse(TxtAttY.Text.Trim()),
                    int.Parse(TxtAttZ.Text.Trim())
                );
                Log($"已设置安装姿态: R={TxtAttRoll.Text}, P={TxtAttPitch.Text}, Y={TxtAttYaw.Text}, X={TxtAttX.Text}, Y={TxtAttY.Text}, Z={TxtAttZ.Text}");
            }
            catch (Exception ex)
            {
                LogError($"设置安装姿态失败: {ex.Message}");
            }
        }

        #endregion

        #region FOV 配置

        /// <summary>
        /// 设置FOV配置0
        /// </summary>
        private void BtnSetFov0_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.SetFovConfig0(
                    int.Parse(TxtFov0YawStart.Text.Trim()),
                    int.Parse(TxtFov0YawStop.Text.Trim()),
                    int.Parse(TxtFov0PitchStart.Text.Trim()),
                    int.Parse(TxtFov0PitchStop.Text.Trim())
                );
                Log("已设置FOV配置0");
            }
            catch (Exception ex)
            {
                LogError($"设置FOV0失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置FOV配置1
        /// </summary>
        private void BtnSetFov1_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.SetFovConfig1(
                    int.Parse(TxtFov0YawStart.Text.Trim()),
                    int.Parse(TxtFov0YawStop.Text.Trim()),
                    int.Parse(TxtFov0PitchStart.Text.Trim()),
                    int.Parse(TxtFov0PitchStop.Text.Trim())
                );
                Log("已设置FOV配置1");
            }
            catch (Exception ex)
            {
                LogError($"设置FOV1失败: {ex.Message}");
            }
        }

        #endregion

        #region 盲区设置

        /// <summary>
        /// 设置盲区距离
        /// </summary>
        private void BtnSetBlindSpot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                uint val = uint.Parse(TxtBlindSpot.Text.Trim());
                _radar!.SetBlindSpot(val);
                Log($"已设置盲区: {val}cm");
            }
            catch (Exception ex)
            {
                LogError($"设置盲区失败: {ex.Message}");
            }
        }

        #endregion

        #region 加热控制

        /// <summary>
        /// 启用窗口加热
        /// </summary>
        private void BtnEnableGlassHeat_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCommand("启用窗口加热", r => r.EnableGlassHeat());
        }

        /// <summary>
        /// 禁用窗口加热
        /// </summary>
        private void BtnDisableGlassHeat_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCommand("禁用窗口加热", r => r.DisableGlassHeat());
        }

        /// <summary>
        /// 启用强制加热
        /// </summary>
        private void BtnStartForceHeat_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCommand("启用强制加热", r => r.StartForcedHeating());
        }

        /// <summary>
        /// 停止强制加热
        /// </summary>
        private void BtnStopForceHeat_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCommand("停止强制加热", r => r.StopForcedHeating());
        }

        #endregion

        #region 开机模式

        /// <summary>
        /// 设置开机工作模式
        /// </summary>
        private void BtnSetBootMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                byte val = byte.Parse(((System.Windows.Controls.ComboBoxItem)CmbBootMode.SelectedItem).Tag.ToString()!);
                _radar!.SetWorkModeAfterBoot(val);
                Log($"已设置开机工作模式: {CmbBootMode.Text}");
            }
            catch (Exception ex)
            {
                LogError($"设置开机模式失败: {ex.Message}");
            }
        }

        #endregion

        #region 设备控制

        /// <summary>
        /// 重启设备
        /// </summary>
        private void BtnReboot_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要重启设备吗？", "确认重启", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                EnsureRadarConnected();
                _radar!.RebootDevice(500);
                Log("已发送重启设备命令（延迟500ms）");
            }
            catch (Exception ex)
            {
                LogError($"重启设备失败: {ex.Message}");
            }
        }

        #endregion

        #region 信息查询

        /// <summary>
        /// 查询固件类型
        /// </summary>
        private void BtnQueryFwType_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("固件类型", KeyType.FirmwareType);
        }

        /// <summary>
        /// 查询固件版本
        /// </summary>
        private void BtnQueryFwVer_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("固件版本", KeyType.VersionApp);
        }

        /// <summary>
        /// 查询序列号
        /// </summary>
        private void BtnQuerySN_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("序列号", KeyType.SerialNumber);
        }

        /// <summary>
        /// 查询MAC地址
        /// </summary>
        private void BtnQueryMAC_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("MAC地址", KeyType.MacAddress);
        }

        /// <summary>
        /// 查询工作状态
        /// </summary>
        private void BtnQueryWorkState_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("工作状态", KeyType.CurrentWorkState);
        }

        /// <summary>
        /// 查询核心温度
        /// </summary>
        private void BtnQueryCoreTemp_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("核心温度", KeyType.CoreTemp);
        }

        /// <summary>
        /// 查询状态码
        /// </summary>
        private void BtnQueryStatusCode_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("状态码", KeyType.StatusCode);
        }

        /// <summary>
        /// 执行单个键值的查询命令
        /// </summary>
        /// <param name="queryName">查询名称（用于日志显示）</param>
        /// <param name="key">要查询的参数键</param>
        private void ExecuteQuery(string queryName, KeyType key)
        {
            try
            {
                EnsureRadarConnected();
                //_radar!.QueryInternalInfo(new KeyType[] { key });
                _radar!.QueryInternalInfo([key]);
                Log($"已发送查询命令: {queryName}");
            }
            catch (Exception ex)
            {
                LogError($"查询{queryName}失败: {ex.Message}");
            }
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 设备发现事件处理：将新设备添加到列表
        /// </summary>
        private void Radar_DeviceDiscovered(object? sender, LidarDeviceInfo device)
        {
            // 跨线程更新UI
            Dispatcher.Invoke(() =>
            {
                // 避免重复添加
                if (_devices.Any(d => d.DeviceInfo.SerialNumberString == device.SerialNumberString))
                    return;

                _devices.Add(new DeviceListItem(device));
                Log($"发现设备: {device.DeviceTypeName} SN={device.SerialNumberString} IP={device.LidarIpString}");
            });
        }

        /// <summary>
        /// ACK响应事件处理：显示命令执行结果，包含Return Code的详细描述
        /// </summary>
        private void Radar_AckResponseReceived(object? sender, AsyncControlResponse response)
        {
            Dispatcher.Invoke(() =>
            {
                if (response.IsSuccess)
                {
                    Log($"ACK: 命令执行成功 (RetCode=0x00 - 执行成功)");
                }
                else
                {
                    // 使用ReturnCodeExtensions获取返回码描述
                    string retCodeDesc = ReturnCodeExtensions.GetDescription(response.RetCode);
                    LogError($"ACK: 命令执行失败 (RetCode={retCodeDesc}, ErrorKey=0x{response.ErrorKey:X4})");
                }
            });
        }

        /// <summary>
        /// 设备推送消息事件处理：显示原始推送数据
        /// </summary>
        private void Radar_PushMessageReceived(object? sender, byte[] data)
        {
            Dispatcher.Invoke(() =>
            {
                Log($"收到推送消息: {data.Length}字节, 命令ID=0x{BitConverter.ToUInt16(data, 8):X4}");
            });
        }

        /// <summary>
        /// 设备状态更新事件处理：显示查询结果，包含Return Code的详细描述
        /// </summary>
        private void Radar_DeviceStatusUpdated(object? sender, InternalInfoResponse info)
        {
            Dispatcher.Invoke(() =>
            {
                if (info.IsSuccess && info.ParamResults != null && info.ParamResults.Length > 0)
                {
                    // 格式化显示查询结果
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[查询成功] 共{info.ParamResults.Length}个参数:");
                    foreach (var kv in info.ParamResults)
                    {
                        sb.AppendLine($"  Key=0x{(ushort)kv.Key:X4} ({kv.Key})");
                        sb.AppendLine($"    原始值: {BitConverter.ToString(kv.Value).Replace("-", " ")}");
                        // 根据Key类型智能解析
                        sb.AppendLine($"    解析值: {ParseKeyValueResult(kv)}");
                    }
                    TxtQueryResult.Text = sb.ToString();
                    Log($"设备状态更新: 查询到{info.ParamResults.Length}个参数");
                }
                else
                {
                    // 查询失败时，显示Return Code的详细描述
                    string retCodeDesc = ReturnCodeExtensions.GetDescription(info.RetCode);
                    LogError($"设备状态查询失败: RetCode={retCodeDesc}");
                    TxtQueryResult.Text = $"查询失败: {retCodeDesc}";
                }
            });
        }

        /// <summary>
        /// 点云数据接收事件处理：显示点云数据统计和摘要
        /// </summary>
        private void Radar_PointCloudDataReceived(object? sender, byte[] data)
        {
            _pclPacketCount++;
            _pclTotalBytes += data.Length;

            // 只在UI线程可见时更新（降低UI刷新频率）
            if (_pclPacketCount % 10 == 0)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtPclStats.Text = $"包数: {_pclPacketCount} | 总字节: {_pclTotalBytes / 1024.0:F1}KB";

                    // 如果启用点云数据显示，解析并显示摘要
                    if (ChkShowPclData.IsChecked == true && _pclPacketCount % 100 == 0)
                    {
                        try
                        {
                            var packet = PointCloudParser.ParsePacket(data);
                            var header = packet.Header;
                            string pclInfo = $"[{DateTime.Now:HH:mm:ss.fff}] " +
                                             $"Ts={header.Timestamp:HH:mm:ss.fff} " +
                                             $"DotNum={header.DotNum} " +
                                             $"UdpCnt={header.UdpCnt} " +
                                             $"DataType={header.DataType}" + Environment.NewLine;

                            // 仅显示前3个点的坐标
                            if (packet.CartesianDataPoints.Count > 0)
                            {
                                int showCount = Math.Min(3, packet.CartesianDataPoints.Count);
                                for (int i = 0; i < showCount; i++)
                                {
                                    var p = packet.CartesianDataPoints[i];
                                    pclInfo += $"  P{i}: X={p.X:F3} Y={p.Y:F3} Z={p.Z:F3} Ref={p.Reflectivity}" + Environment.NewLine;
                                }
                                if (packet.CartesianDataPoints.Count > 3)
                                    pclInfo += $"  ... 共{packet.CartesianDataPoints.Count}个点" + Environment.NewLine;
                            }

                            AppendTextBox(TxtPointCloud, pclInfo, ChkAutoScrollPcl.IsChecked == true);
                        }
                        catch (Exception ex)
                        {
                            AppendTextBox(TxtPointCloud, $"[解析错误] {ex.Message}" + Environment.NewLine, false);
                        }
                    }
                });
            }
        }

        /// <summary>
        /// IMU数据接收事件处理：显示IMU数据摘要
        /// </summary>
        private void Radar_ImuDataReceived(object? sender, byte[] data)
        {
            // IMU数据频率较低，每次都显示
            Dispatcher.Invoke(() =>
            {
                if (ChkShowPclData.IsChecked == true)
                {
                    try
                    {
                        var packet = PointCloudParser.ParsePacket(data);
                        if (packet.ImuDataPoints.Count > 0)
                        {
                            var imu = packet.ImuDataPoints[0];
                            string imuInfo = $"[{DateTime.Now:HH:mm:ss.fff}] IMU: " +
                                             $"Gyro=({imu.GyroX:F3},{imu.GyroY:F3},{imu.GyroZ:F3}) " +
                                             $"Acc=({imu.AccX:F3},{imu.AccY:F3},{imu.AccZ:F3})" + Environment.NewLine;
                            AppendTextBox(TxtPointCloud, imuInfo, ChkAutoScrollPcl.IsChecked == true);
                        }
                    }
                    catch { /* 忽略IMU解析错误 */ }
                }
            });
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 确保雷达管理器已初始化且设备已连接
        /// </summary>
        /// <exception cref="InvalidOperationException">雷达未初始化或设备未连接</exception>
        private void EnsureRadarConnected()
        {
            if (_radar == null)
                throw new InvalidOperationException("请先初始化雷达管理器");
            if (!_radar.IsConnected)
                throw new InvalidOperationException("请先连接设备");
        }

        /// <summary>
        /// 通用命令执行方法，统一处理异常和日志
        /// </summary>
        /// <param name="commandName">命令名称（用于日志）</param>
        /// <param name="action">要执行的操作</param>
        private void ExecuteCommand(string commandName, Action<LivoxHapRadar> action)
        {
            try
            {
                EnsureRadarConnected();
                action(_radar!);
                Log($"已发送{commandName}命令");
            }
            catch (Exception ex)
            {
                LogError($"{commandName}失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 智能解析KeyValueResult，根据Key类型返回可读的值
        /// </summary>
        /// <param name="kv">键值对查询结果</param>
        /// <returns>格式化的值字符串</returns>
        private static string ParseKeyValueResult(KeyValueResult kv)
        {
            try
            {
                return kv.Key switch
                {
                    KeyType.SerialNumber => kv.AsAsciiString(),
                    KeyType.ProductInfo => kv.AsAsciiString(),
                    KeyType.VersionApp => FormatVersion(kv.Value),
                    KeyType.VersionLoader => FormatVersion(kv.Value),
                    KeyType.VersionHardware => FormatVersion(kv.Value),
                    KeyType.MacAddress => FormatMacAddress(kv.Value),
                    KeyType.CurrentWorkState => $"0x{kv.AsByte():X2} ({(DeviceWorkState)kv.AsByte()})",
                    KeyType.CoreTemp => $"{kv.AsFloat():F1}°C",
                    KeyType.StatusCode => $"0x{kv.AsUInt32():X8}",
                    KeyType.FirmwareType => $"0x{kv.AsByte():X2} ({(FirmwareType)kv.AsByte()})",
                    KeyType.PowerUpCount => kv.AsUInt32().ToString(),
                    KeyType.LidarIpConfig => kv.AsIpAddress(),
                    KeyType.StateInfoHostIpConfig => kv.AsIpAddress(),
                    KeyType.PointCloudHostIpConfig => kv.AsIpAddress(),
                    KeyType.ImuHostIpConfig => kv.AsIpAddress(),
                    _ => BitConverter.ToString(kv.Value).Replace("-", " ")
                };
            }
            catch
            {
                return BitConverter.ToString(kv.Value).Replace("-", " ");
            }
        }

        /// <summary>
        /// 格式化版本号（4字节 → A.B.C.D）
        /// </summary>
        /// <param name="value">原始字节</param>
        /// <returns>格式化版本号字符串</returns>
        private static string FormatVersion(byte[] value)
        {
            if (value == null || value.Length < 4) return "N/A";
            return $"{value[0]}.{value[1]}.{value[2]}.{value[3]}";
        }

        /// <summary>
        /// 格式化MAC地址（6字节 → XX:XX:XX:XX:XX:XX）
        /// </summary>
        /// <param name="value">原始字节</param>
        /// <returns>格式化MAC地址字符串</returns>
        private static string FormatMacAddress(byte[] value)
        {
            if (value == null || value.Length < 6) return "N/A";
            return string.Join(":", value.Take(6).Select(b => b.ToString("X2")));
        }

        /// <summary>
        /// 追加文本到TextBox（线程安全），并控制最大长度避免内存溢出
        /// </summary>
        /// <param name="textBox">目标文本框</param>
        /// <param name="text">要追加的文本</param>
        /// <param name="autoScroll">是否自动滚动到底部</param>
        private static void AppendTextBox(System.Windows.Controls.TextBox textBox, string text, bool autoScroll)
        {
            // 限制最大文本长度，超出时截断旧内容
            const int maxLength = 50000;
            if (textBox.Text.Length > maxLength)
                textBox.Text = textBox.Text[^30000..];

            textBox.AppendText(text);
            if (autoScroll)
                textBox.ScrollToEnd();
        }

        /// <summary>
        /// 记录普通消息到日志
        /// </summary>
        /// <param name="message">消息内容</param>
        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
            AppendTextBox(TxtLog, line, true);
        }

        /// <summary>
        /// 记录错误消息到日志（带[ERROR]前缀）
        /// </summary>
        /// <param name="message">错误消息内容</param>
        private void LogError(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] [ERROR] {message}{Environment.NewLine}";
            AppendTextBox(TxtLog, line, true);
        }

        /// <summary>
        /// 清空日志文本框
        /// </summary>
        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        /// <summary>
        /// 清空点云数据显示
        /// </summary>
        private void BtnClearPcl_Click(object sender, RoutedEventArgs e)
        {
            TxtPointCloud.Clear();
            _pclPacketCount = 0;
            _pclTotalBytes = 0;
            TxtPclStats.Text = "";
        }

        #endregion

        #region 窗口关闭

        /// <summary>
        /// 窗口关闭时释放雷达资源
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            _radar?.Dispose();
            base.OnClosed(e);
        }

        #endregion
    }

    #region 设备列表项模型

    /// <summary>
    /// 设备列表项模型，封装 LidarDeviceInfo 并提供 UI 显示所需的属性
    /// </summary>
    /// <remarks>
    /// 构造设备列表项
    /// </remarks>
    /// <param name="deviceInfo">设备信息</param>
    public class DeviceListItem(LidarDeviceInfo deviceInfo)
    {
        /// <summary>设备信息实体</summary>
        public LidarDeviceInfo DeviceInfo { get; } = deviceInfo;

        /// <summary>是否已连接</summary>
        public bool IsConnected { get; set; } = deviceInfo.IsConnected;

        /// <summary>显示名称（格式：型号 SN IP）</summary>
        public string DisplayName => $"{DeviceInfo.DeviceTypeName} | {DeviceInfo.SerialNumberString} | {DeviceInfo.LidarIpString}";

        /// <summary>连接状态指示灯颜色</summary>
        public Brush ConnStatusColor => IsConnected ? Brushes.LimeGreen : Brushes.Gray;
    }

    #endregion
}
