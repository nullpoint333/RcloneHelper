using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RcloneHelper.Helpers;

/// <summary>
/// rclone 路径查找辅助类
/// </summary>
public static class RcloneLocator
{
    private const string WindowsExe = "rclone.exe";
    private const string UnixBinary = "rclone";

    /// <summary>
    /// 获取 rclone 可执行文件路径
    /// </summary>
    /// <returns>rclone 完整路径，如果未找到则返回 null</returns>
    public static string? GetRclonePath()
    {
        var exeName = GetExecutableName();

        // 1. 优先查找 %APPDATA%\RcloneHelper 目录
        var appDataRclone = Path.Combine(PathUtil.AppDataDir, exeName);
        if (File.Exists(appDataRclone))
            return appDataRclone;

        // 2. 查找程序目录（AppDomain.BaseDirectory）
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var localRclone = Path.Combine(appDir, exeName);
        if (File.Exists(localRclone))
            return localRclone;

        // 3. 查找当前进程可执行文件所在目录（覆盖 AOT 单文件场景）
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            var processDir = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrEmpty(processDir))
            {
                var processRclone = Path.Combine(processDir, exeName);
                if (File.Exists(processRclone))
                    return processRclone;
            }
        }

        // 4. 从 PATH 环境变量查找
        var pathRclone = FindInPath();
        if (pathRclone != null)
            return pathRclone;

        // 5. 未找到
        return null;
    }

    /// <summary>
    /// 在系统 PATH 中查找 rclone
    /// </summary>
    /// <returns>rclone 完整路径，如果未找到则返回 null</returns>
    public static string? FindInPath()
    {
        var exeName = GetExecutableName();
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var fullPath = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            catch
            {
                // 跳过无效路径
            }
        }

        return null;
    }

    /// <summary>
    /// 测试 rclone 是否可用
    /// </summary>
    /// <param name="rclonePath">rclone 路径（完整路径或可执行文件名）</param>
    /// <returns>是否可用</returns>
    public static bool TestExecution(string? rclonePath)
    {
        if (string.IsNullOrEmpty(rclonePath))
            return false;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = rclonePath,
                    Arguments = "version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && output.Contains("rclone", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetExecutableName()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsExe : UnixBinary;
    }
}
