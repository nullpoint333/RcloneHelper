# RcloneHelper TODO

## 项目概述

**RcloneHelper** 是一个基于 Avalonia UI 开发的跨平台 rclone 辅助工具，用于管理 WebDAV、FTP、SFTP、S3 等多种存储类型的挂载操作。

---

## ✅ 已完成功能

### 主页（HomePage）
- [x] 挂载列表显示（全部/已挂载/未挂载分组）
- [x] 单个挂载：挂载、卸载、编辑、删除按钮
- [x] 批量操作：挂载全部、卸载全部
- [x] 挂载表单（支持 WebDAV/FTP/SFTP/S3 四种类型，动态显示对应字段）
- [x] 挂载状态实时刷新，支持取消挂载操作
- [x] 高级参数配置：VFS 缓存模式（off/minimal/writes/full）、自定义 rclone 参数

### 配置页（SettingsPage）
- [x] 开机自启动开关（Windows 任务计划程序）
- [x] 静默启动（最小化到系统托盘）
- [x] 代理设置：HTTP/HTTPS/SOCKS5 协议、地址、端口、认证信息、测试连接
- [x] 配置导入/导出（通过文件选择器，支持选择任意位置）
- [x] 数据存储路径显示，一键打开

### rclone 配置页（RcloneConfigPage）
- [x] 显示 rclone 版本信息
- [x] 显示/打开 rclone 配置文件路径和缓存目录
- [x] 读取并解析 rclone.conf，显示 remote 列表及挂载状态
- [x] 删除 remote（带确认对话框，走 MountService 配置锁）
- [x] 卸载选中的 remote
- [x] 系统依赖检测（WinFsp、rclone）及安装链接

### 核心服务
- [x] MountService：挂载 CRUD、rclone config create/delete、进程管理、自动备份
- [x] ConfigService：应用配置读写（settings.json）
- [x] ISystemService：平台抽象（Windows/Linux/MacOS 实现）
- [x] FileLoggerService：文件日志记录（按日期滚动，保留 7 天）
- [x] NotificationService：Toast 通知
- [x] DialogService：确认对话框、文件选择对话框
- [x] SecureStorageHelper：Windows DPAPI 密码加密

### 安全与健壮性
- [x] 密码安全存储（Windows DPAPI 加密，兼容未加密旧数据）
- [x] 配置文件原子写入（临时文件 + 替换，防止损坏）
- [x] 配置保存前自动备份（mounts.json.bak）
- [x] 导入备份时保留加密密码（避免跨用户/跨平台解密失败）
- [x] 挂载进程异常退出通知
- [x] 配置加载容错（单个配置失败不影响其他配置）
- [x] SOCKS5 代理使用 ALL_PROXY 环境变量

### 测试
- [x] 创建 RcloneHelper.Tests 项目（xUnit + Moq）
- [x] 测试覆盖：MountInfo, PathUtil, AppConfig, SecureStorageHelper
- [x] 43 个测试全部通过

---

## 📋 待办事项

### 🟡 P1 - 功能完善

- [ ] **rclone 版本自动检查更新**
  - 当前仅有跳转下载页面的按钮
  - 调用 `rclone version` 获取当前版本
  - 调用 GitHub API 或 rclone.org 获取最新版本
  - 显示更新提示

### 🟢 P2 - 体验提升

- [ ] **Windows 原生 Toast 通知**
  - 当前使用应用内 Toast，托盘状态下用户看不到
  - 使用 `Microsoft.Toolkit.Uwp.Notifications` 或 WinRT API
  - 支持操作按钮（如"重新挂载"）

- [ ] **挂载历史记录**
  - 新增 `MountHistory` 表：时间、操作类型、成功/失败、耗时
  - 新增历史查看页面或面板

- [ ] **日志等级控制**
  - 用户可配置日志级别（Debug/Info/Warning/Error）
  - 在设置页面添加日志级别下拉框

### 🔵 P3 - 扩展功能（可选）

- [ ] **多语言支持（i18n）**
  - 当前硬编码中文字符串
  - 可使用 .resx 资源文件支持中英文切换

- [ ] **自动更新机制**
  - 应用自身版本检查和更新

- [ ] **Linux/macOS 实际测试与修复**
  - 跨平台实现已完成，但未在真实环境测试

- [ ] **rclone.conf 直接编辑**
  - 在 RcloneConfigPage 中支持直接编辑配置文件内容
  - 语法高亮和自动补全

---

## 📝 配置文件位置

```
%APPDATA%\RcloneHelper\
├── mounts.json       # 挂载配置（密码已加密）
├── mounts.json.bak   # 自动备份（每次保存前创建）
├── settings.json     # 应用设置（主题、代理等）
└── log/              # 应用日志（按日期滚动，保留 7 天）
    ├── app_2025-01-01.log
    └── ...
```

---

## 构建命令

```bash
# 还原依赖
dotnet restore

# 构建（开发时使用 Release 避免文件锁定）
dotnet build -c Release

# 运行
dotnet run -c Release --no-build

# 运行测试
dotnet test

# 发布独立应用
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
