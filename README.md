# RcloneHelper

一个使用 Avalonia UI 开发的 rclone 辅助工具，支持 WebDAV、FTP、SFTP、S3 等多种存储类型的挂载管理。

## 功能特性

- **多类型挂载**：支持 WebDAV、FTP、SFTP、S3 等 rclone 支持的存储类型
- **开机自启动**：可设置 Windows 开机自动启动，支持静默启动（最小化到系统托盘）
- **自动挂载**：启动后自动挂载所有配置了"启动时自动挂载"的存储
- **网络代理**：支持 HTTP/HTTPS/SOCKS5 代理，可配置认证信息，内置代理测试
- **配置导入/导出**：通过文件选择器导出/导入挂载配置，支持跨设备迁移
- **密码安全存储**：使用 Windows DPAPI 加密敏感信息（密码、Secret Key）
- **高级参数**：支持配置 VFS 缓存模式和自定义 rclone 参数
- **深色/浅色主题**：支持动态切换主题
- **简洁的界面**：使用 Avalonia 构建的跨平台 UI

## 系统要求

- Windows 10/11
- .NET 10.0 或更高版本
- WinFsp（rclone mount 必需）
- rclone 已安装并在 PATH 中，或放在程序目录下

## 使用方法

### 1. 安装依赖

```bash
# 安装 WinFsp: https://github.com/winfsp/winfsp/releases

# 安装 rclone: https://rclone.org/downloads/
# 或使用 chocolatey
choco install rclone
```

### 2. 编译项目

```bash
dotnet restore
dotnet build -c Release
dotnet run --no-build -c Release
```

> ⚠️ **注意**：开发时请使用 Release 配置。Debug 编译输出会被正在运行的 Debug 实例锁定，导致编译失败。

### 3. 发布独立应用

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### 4. 运行测试

```bash
dotnet test
```

## 使用说明

### 添加挂载

1. 点击标题栏的"添加挂载"按钮
2. 选择存储类型并填写对应信息：
   - **WebDAV**：协议、主机地址、端口、路径、用户名/密码、服务商
   - **FTP/SFTP**：主机地址、端口、用户名/密码
   - **S3**：服务商、Access Key、Secret Key、区域、Endpoint、Bucket
3. 配置本地盘符、缓存模式、自动挂载等选项
4. 点击"保存"

### 挂载/卸载

- 点击列表中的 ▶（挂载）或 ⏸（卸载）图标按钮
- 支持取消正在进行的挂载操作
- 列表上方有"挂载全部"/"卸载全部"批量操作按钮

### 代理设置

进入"配置"页面，启用代理后可配置：
- 代理协议（HTTP/HTTPS/SOCKS5）
- 代理地址和端口
- 认证信息（可选）
- 点击"测试代理"验证连接

### 配置导入/导出

- **导出**：点击"导出配置"，选择保存位置，配置将保存为 JSON 文件
- **导入**：点击"导入配置"，选择备份文件，已存在的挂载会被跳过

### Rclone 管理

进入"Rclone 管理"页面可以：
- 查看 rclone 版本和配置文件路径
- 查看系统依赖状态（WinFsp、rclone）
- 管理 rclone remote 配置
- 卸载或删除 remote

## 配置文件

配置文件保存在：
```
%APPDATA%\RcloneHelper\
├── mounts.json       # 挂载配置（密码已加密）
├── mounts.json.bak   # 自动备份
├── settings.json     # 应用设置（主题、代理等）
└── log/              # 应用日志（按日期滚动，保留 7 天）
```

## 项目结构

```
RcloneHelper/
├── RcloneHelper/                    # UI 层 (Avalonia)
│   ├── Views/Pages/                # 页面控件
│   ├── Views/Windows/              # 主窗口
│   ├── Services/                   # UI 服务（DialogService, ThemeService）
│   └── App.axaml.cs               # DI 容器初始化
│
├── RcloneHelper.Core/              # 核心逻辑层
│   ├── Models/                     # 数据模型
│   ├── Pages/                      # ViewModels
│   ├── Services/                   # 核心服务（MountService, ConfigService）
│   └── Helpers/                    # 工具类（PathUtil, SecureStorageHelper）
│
└── RcloneHelper.Tests/             # 单元测试 (xUnit + Moq)
```

## 许可证

MIT License
