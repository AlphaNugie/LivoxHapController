LivoxHapController/
├── Enums/                     // 协议枚举定义
│   ├── CommandType.cs
│   ├── DataType.cs
│   ├── DeviceState.cs
│   ├── ErrorCode.cs
│   └── ...其他枚举
│
├── Models/                    // 协议数据结构
│   ├── NetworkConfig.cs
│   ├── PointCloudData.cs
│   ├── ImuData.cs
│   ├── KeyValueItem.cs
│   └── ...其他模型
│
├── Services/
│   ├── ProtocolParser.cs      // 协议解析核心
│   ├── UdpCommunicator.cs     // UDP通信处理
│   └── RadarCommander.cs      // 雷达控制命令
│
├── Events/
│   ├── DataReceivedEventArgs.cs
│   ├── StateChangedEventArgs.cs
│   └── ...自定义事件
│
└── LivoxHapRadar.cs           // 主控制类