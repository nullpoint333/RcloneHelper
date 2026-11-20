using System;
using System.IO;

/// <summary>
/// 路径管理工具类
/// </summary>
public static class PathUtil
{
    private static readonly Lazy<string> _appDataDir = new(() =>
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "RcloneHelper");
        Directory.CreateDirectory(dir);
        return dir;
    });

    private static readonly Lazy<string> _rcloneConfigDir = new(() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "rclone"));

    private static readonly Lazy<string> _appLocation = new(() =>
    {
        // 直接使用 BaseDirectory：单文件/AOT 下 Assembly.Location 返回空，
        // 且对主程序而言两者指向同一目录（旧逻辑为空时本就回退到这里）
        return AppContext.BaseDirectory;
    });

    public static string AppDataDir => _appDataDir.Value;
    public static string AppLocation => _appLocation.Value;
    public static string MountsConfigPath => Path.Combine(AppDataDir, "mounts.json");
    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");
    public static string LogPath => Path.Combine(AppDataDir, "log");
    public static string RcloneConfigPath => Path.Combine(_rcloneConfigDir.Value, "rclone.conf");
    public static string RcloneCacheDir => Path.Combine(_rcloneConfigDir.Value, "cache");

    /// <summary>
    /// 默认 rclone 缓存目录（与 <see cref="RcloneCacheDir"/> 相同，保留别名语义）
    /// </summary>
    public static string DefaultRcloneCacheDir => RcloneCacheDir;

    /// <summary>
    /// 解析实际生效的 rclone 缓存目录。
    /// 自定义目录为空时回退到默认位置。
    /// </summary>
    /// <param name="customDir">用户自定义的缓存目录（可为空）</param>
    /// <returns>实际生效的缓存目录（已去除首尾空格）</returns>
    public static string GetEffectiveCacheDir(string? customDir)
    {
        if (!string.IsNullOrWhiteSpace(customDir))
            return customDir.Trim();
        return RcloneCacheDir;
    }

    /// <summary>
    /// 规范化目录路径：去除首尾空白和末尾分隔符，同时保留根路径（如 "C:\"）。
    /// </summary>
    public static string NormalizeDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return "";

        var trimmed = dir.Trim();
        try
        {
            trimmed = Path.GetFullPath(trimmed);
        }
        catch
        {
            // 非法路径时保留用户原始输入，交由后续校验处理
        }

        return Path.TrimEndingDirectorySeparator(trimmed);
    }

    /// <summary>
    /// 获取某个 rclone remote 的 VFS 缓存目录。
    /// rclone 把待上传/已缓存的文件放在 &lt;cache-dir&gt;/vfs/&lt;remote&gt;/ 下。
    /// </summary>
    public static string GetVfsCacheDir(string cacheDir, string remoteName)
        => Path.Combine(cacheDir, "vfs", remoteName);

    /// <summary>
    /// 获取某个 rclone remote 的 VFS 元数据目录（&lt;cache-dir&gt;/vfsMeta/&lt;remote&gt;）
    /// </summary>
    public static string GetVfsMetaDir(string cacheDir, string remoteName)
        => Path.Combine(cacheDir, "vfsMeta", remoteName);

    /// <summary>
    /// 统计目录中的文件数量与总大小（字节）。
    /// </summary>
    public static (int Files, long Bytes) GetDirectoryStats(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return (0, 0);

        var files = 0;
        long bytes = 0;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                ReturnSpecialDirectories = false
            };

            foreach (var file in Directory.EnumerateFiles(directory, "*", options))
            {
                try
                {
                    bytes += new FileInfo(file).Length;
                    files++;
                }
                catch
                {
                    // 单个文件不可读时跳过
                }
            }
        }
        catch
        {
            // 目录不可枚举时返回已统计到的部分
        }

        return (files, bytes);
    }

    /// <summary>
    /// 递归统计目录占用大小（字节）。目录不存在或不可访问时返回 0。
    /// </summary>
    public static long GetDirectorySize(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return 0;

        long total = 0;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                ReturnSpecialDirectories = false
            };

            foreach (var file in Directory.EnumerateFiles(directory, "*", options))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // 单个文件不可读时跳过，不影响整体统计
                }
            }
        }
        catch
        {
            // 目录不可枚举时返回已统计到的部分
        }

        return total;
    }

    /// <summary>
    /// 删除目录内的所有内容（保留目录本身），返回释放的字节数。
    /// 用于“清理 rclone 缓存”。
    /// </summary>
    public static long ClearDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return 0;

        var freed = 0L;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    freed += GetDirectorySize(entry);
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    var length = new FileInfo(entry).Length;
                    File.Delete(entry);
                    freed += length;
                }
            }
            catch
            {
                // 被占用的文件（例如仍在挂载）跳过
            }
        }

        return freed;
    }

    /// <summary>
    /// 将字节数格式化为易读的字符串（1024 进制）。
    /// </summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0)
            return "0 B";

        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }

    /// <summary>
    /// 原子写入文件内容
    /// 使用临时文件+重命名的方式确保写入失败不会损坏原文件
    /// </summary>
    /// <param name="filePath">目标文件路径</param>
    /// <param name="content">要写入的内容</param>
    public static void AtomicWriteAllText(string filePath, string content)
    {
        var directory = Path.GetDirectoryName(filePath) ?? throw new ArgumentException("无法获取文件目录", nameof(filePath));

        // 确保目录存在
        Directory.CreateDirectory(directory);

        // 使用随机文件名避免冲突
        var tempPath = Path.Combine(directory, $".tmp_{Guid.NewGuid():N}");

        try
        {
            // 写入临时文件
            File.WriteAllText(tempPath, content);

            // 验证临时文件写入成功
            if (!File.Exists(tempPath))
            {
                throw new IOException("临时文件创建失败");
            }

            // 使用 Replace 方法进行原子替换（在 Windows 上是原子的）
            // 如果目标文件不存在，直接移动
            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, null);
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        finally
        {
            // 清理可能残留的临时文件
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }
}