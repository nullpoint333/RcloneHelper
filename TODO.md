# RcloneHelper TODO

## 项目概述

**RcloneHelper** 是一个基于 Avalonia UI 开发的跨平台 rclone 辅助工具，用于管理 WebDAV、FTP、SFTP、S3 等多种存储类型的挂载操作。

---

## ✅ 已完成功能

### 主页（HomePage）
- [x] 挂载列表（状态灯 + 名称 + 类型徽标 + 挂载点 + 行内操作按钮）
- [x] 单个挂载：挂载、取消挂载、卸载、编辑、删除
- [x] 批量操作：全部挂载、全部卸载
- [x] 搜索（名称/类型/地址/挂载点）与筛选（全部/已挂载/未挂载）
- [x] 挂载状态实时刷新，挂载意外退出时自动回到「未挂载」
- [x] 新增/编辑表单（WebDAV URL 组件拆分：协议/主机/端口/路径）
- [x] **只读查看模式**：编辑已挂载的连接时进入查看模式，所有输入框只读；「卸载并编辑」后才允许修改
- [x] **后台上传可见/可取消**：显示 VFS 写缓存中待上传的文件数与大小，可一键取消（结束进程 + 丢弃未上传缓存 + 自动重挂）
- [x] 删除二次确认（运行中的连接会提示先卸载）
- [x] 按类型校验必填项（WebDAV/FTP/SFTP/S3），兼容旧版仅保存完整 URL 的配置

### 设置页（SettingsPage）
- [x] 开机自启动开关（Windows 启动项）
- [x] 静默启动（开机自启时最小化到托盘）
- [x] 代理设置（http/https/socks5 + 可选认证 + TCP 连通性测试）
- [x] **rclone 缓存管理**：缓存目录、缓存上限、占用统计、打开目录、一键清理、恢复默认
- [x] 配置导入/导出（JSON，跳过同名连接，单条无效不影响其他）
- [x] 程序数据/程序位置快捷打开

### rclone 管理页（RcloneConfigPage）
- [x] 依赖状态（WinFsp / rclone），按依赖分别跳转安装页
- [x] rclone 版本、可执行文件路径、配置文件路径、当前缓存目录（只读，修改请到设置页）
- [x] 读取并解析 rclone.conf，显示 remote 列表与挂载状态
- [x] 卸载选中 remote（优先走挂载服务，能清理进程与 WinFsp 残留）
- [x] 删除 remote（受应用管理的连接会同时删除 mounts.json 中的配置）
- [x] 页面显示时自动刷新（依赖探测/读配置都在后台线程）

### 核心服务
- [x] MountService：挂载 CRUD、rclone config create/delete、进程管理、状态恢复
- [x] **按后端类型正确生成 rclone 配置**：WebDAV 用 url/vendor，FTP/SFTP 用 host/port，S3 用 provider/access_key_id 等
- [x] 挂载时显式传入 `--cache-dir`，可选 `--vfs-cache-max-size`
- [x] 挂载前连通性预检，避免创建必然失败的挂载
- [x] ISystemService：平台抽象（Windows/Linux/MacOS 实现）
- [x] FileLoggerService：文件日志（7 天滚动）
- [x] NotificationService：应用内 Toast
- [x] DialogService：确认对话框 + 文件夹选择器
- [x] MountInfo.Validate()：统一的配置校验（模型层，带单元测试）

### UI 基础
- [x] 主题颜色改为 `ThemeDictionaries`（Default=深色 / Light=浅色），代码里不再维护两份颜色表
- [x] 修复「记住浅色主题但启动后仍是深色」的问题（启动时立即应用主题）
- [x] 补齐缺失的画刷资源（ButtonBrush / ButtonForegroundBrush 等），按钮样式统一为主/次/幽灵/图标/筛选 chip
- [x] 导航改为矢量图标 + 选中指示条，Toast 移到右下角，窗口尺寸/最小尺寸约束
- [x] 启用编译期绑定（`AvaloniaUseCompiledBindingsByDefault=true`），绑定写错会在构建时报错，AOT/裁剪友好

---

## 📋 待办事项

### 🟡 P1 - 功能完善

- [ ] **Windows 原生 Toast 通知**
  - 当前使用应用内 Toast，托盘状态下用户看不到
  - 使用 WinRT API 或社区库，支持操作按钮（如「重新挂载」）

- [ ] **rclone 版本自动检查更新**
  - 当前仅有跳转下载页面的按钮
  - 调用 GitHub/rclone.org 获取最新版本并提示

- [ ] **挂载历史记录**
  - 记录时间、操作类型、成功/失败、耗时
  - 新增历史查看面板

### 🟢 P2 - 体验提升

- [ ] **编辑对话框键盘支持**：Esc 关闭、Enter 保存
- [ ] **挂载点自定义输入**：当前从预置列表选择，Linux 下希望支持手输路径
- [ ] **多语言支持（i18n）**
  - 当前硬编码中文字符串
  - 可使用 .resx 资源文件支持中英文切换

### 🔵 P3 - 扩展功能

- [ ] **自动更新机制**
- [ ] **Linux/macOS 实际测试与修复**
  - 跨平台实现已完成，但未在真实环境测试
- [ ] **日志等级控制**
  - Debug/Info/Warning/Error 分级，用户可配置

---

## 🐛 已知问题

| 问题 | 说明 | 严重程度 |
|------|------|----------|
| ~~空 catch 块~~ | ~~多处 catch { } 吞掉错误~~ | ✅ 已修复 |
| ~~密码明文存储~~ | ~~mounts.json 中密码未加密~~ | ✅ 已修复 |
| ~~挂载异常退出无通知~~ | ~~进程退出仅更新状态，用户不知情~~ | ✅ 已修复 |
| ~~S3 无法新增挂载~~ | ~~AddMount 强制校验 URL，S3 没有 URL~~ | ✅ 已修复 |
| ~~FTP/SFTP/S3 写入 rclone 配置失败~~ | ~~只传 url，rclone 缺少 host/provider 等字段~~ | ✅ 已修复 |
| ~~浅色主题启动不生效~~ | ~~只在切换时应用主题~~ | ✅ 已修复 |
| ~~rclone 管理页信息长期显示「正在获取」~~ | ~~页面显示时未触发刷新~~ | ✅ 已修复 |
| 挂载超时固定 30 秒 | 慢速网络下可能误判，可考虑按类型放宽 | 🟢 低 |

---

## 🧱 本次重构记录（2026-11）

- **UI**：主页/设置页/rclone 管理页整体重排为卡片式布局 + 统一间距与排版；编辑对话框改为分区表单，按类型分别显示字段（不再用固定行号堆叠，避免字段重叠）
- **布局细节**：设置页/Rclone 页去掉占空间的页内标题（标题已在窗口标题栏显示），Rclone 页按钮移到底部操作栏；版本号并入左上角应用名，左下角信息块删除，设置固定在左下角；滚动条改为占用独立空间，不再盖在表单上
- **只读查看模式**：编辑已挂载连接不再强制先卸载，而是进入只读查看；「卸载并编辑」显式切换
- **后台上传可取消**：rclone 写缓存模式下复制大文件会立即返回，实际上传在后台；现在显示待上传文件/大小并可取消（直接结束进程 + 丢弃 VFS 待上传缓存 + 自动重挂）
- **rclone 缓存**：设置入口统一到「设置 → rclone 缓存」，支持位置、上限、占用统计与清理；挂载参数显式传 `--cache-dir`
- **正确性**：按后端类型生成 rclone 配置；`MountInfo.Validate()` 统一校验；挂载失败立即报错（不再只写日志）
- **性能/可维护性**：主题颜色表移除代码重复；绑定改为编译期检查；列表筛选原位更新（不重建集合、不丢选中项）；页面数据惰性加载放后台线程

---

## 📝 配置文件位置

```
%APPDATA%\RcloneHelper\
├── mounts.json      # 挂载配置（密码 DPAPI 加密）
├── settings.json    # 应用设置（主题、代理、缓存目录、缓存上限）
└── log\             # 运行日志（app_yyyy-MM-dd.log，保留 7 天）
```

rclone 自身配置：`%APPDATA%\rclone\rclone.conf`
rclone 默认缓存（本程序默认也用它）：`%APPDATA%\rclone\cache`

---

## 构建命令

```bash
# 还原依赖
dotnet restore

# 构建（开发时使用 Release 避免文件锁定）
dotnet build -c Release

# 运行
dotnet run -c Release --no-build

# 测试
dotnet test -c Release

# 发布独立应用
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
