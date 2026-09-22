namespace RcloneHelper.Tests;

public class PathUtilTests
{
    [Fact]
    public void AppDataDir_ShouldReturnValidPath()
    {
        var path = PathUtil.AppDataDir;

        Assert.False(string.IsNullOrEmpty(path));
        Assert.Contains("RcloneHelper", path);
    }

    [Fact]
    public void AppDataDir_ShouldEndWithRcloneHelper()
    {
        var path = PathUtil.AppDataDir;

        Assert.EndsWith("RcloneHelper", path);
    }

    [Fact]
    public void MountsConfigPath_ShouldBeInAppDataDir()
    {
        var configPath = PathUtil.MountsConfigPath;

        Assert.StartsWith(PathUtil.AppDataDir, configPath);
        Assert.EndsWith("mounts.json", configPath);
    }

    [Fact]
    public void SettingsPath_ShouldBeInAppDataDir()
    {
        var settingsPath = PathUtil.SettingsPath;

        Assert.StartsWith(PathUtil.AppDataDir, settingsPath);
        Assert.EndsWith("settings.json", settingsPath);
    }

    [Fact]
    public void LogPath_ShouldBeInAppDataDir()
    {
        var logPath = PathUtil.LogPath;

        Assert.StartsWith(PathUtil.AppDataDir, logPath);
        Assert.EndsWith("log", logPath);
    }

    [Fact]
    public void RcloneConfigPath_ShouldContainRclone()
    {
        var path = PathUtil.RcloneConfigPath;

        Assert.Contains("rclone", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("rclone.conf", path);
    }

    [Fact]
    public void AtomicWriteAllText_ShouldCreateFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var filePath = Path.Combine(tempDir, "test.txt");
            var content = "Hello, World! 你好世界";

            PathUtil.AtomicWriteAllText(filePath, content);

            Assert.True(File.Exists(filePath));
            Assert.Equal(content, File.ReadAllText(filePath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AtomicWriteAllText_ShouldOverwriteExistingFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "old content");

            PathUtil.AtomicWriteAllText(filePath, "new content");

            Assert.Equal("new content", File.ReadAllText(filePath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AtomicWriteAllText_ShouldCreateDirectoryIfMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var subDir = Path.Combine(tempDir, "sub1", "sub2");
        try
        {
            var filePath = Path.Combine(subDir, "test.txt");

            PathUtil.AtomicWriteAllText(filePath, "content");

            Assert.True(File.Exists(filePath));
            Assert.Equal("content", File.ReadAllText(filePath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
