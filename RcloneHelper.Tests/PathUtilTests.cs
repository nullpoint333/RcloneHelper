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

    [Fact]
    public void GetEffectiveCacheDir_ShouldFallbackToDefaultWhenEmpty()
    {
        Assert.Equal(PathUtil.RcloneCacheDir, PathUtil.GetEffectiveCacheDir(null));
        Assert.Equal(PathUtil.RcloneCacheDir, PathUtil.GetEffectiveCacheDir(""));
        Assert.Equal(PathUtil.RcloneCacheDir, PathUtil.GetEffectiveCacheDir("   "));
    }

    [Fact]
    public void GetEffectiveCacheDir_ShouldTrimCustomDir()
    {
        Assert.Equal(@"D:\rclone-cache", PathUtil.GetEffectiveCacheDir(@"  D:\rclone-cache  "));
    }

    [Fact]
    public void NormalizeDir_ShouldRemoveTrailingSeparator()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rclonehelper-normalize-test");

        var normalized = PathUtil.NormalizeDir(dir + Path.DirectorySeparatorChar);

        Assert.Equal(Path.TrimEndingDirectorySeparator(dir), normalized);
    }

    [Fact]
    public void NormalizeDir_ShouldReturnEmptyForBlankInput()
    {
        Assert.Equal("", PathUtil.NormalizeDir(null));
        Assert.Equal("", PathUtil.NormalizeDir("  "));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void FormatSize_ShouldFormatReadable(long bytes, string expected)
    {
        Assert.Equal(expected, PathUtil.FormatSize(bytes));
    }

    [Fact]
    public void GetDirectorySize_And_ClearDirectory_ShouldWork()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var subDir = Path.Combine(tempDir, "vfs");
        Directory.CreateDirectory(subDir);
        try
        {
            File.WriteAllBytes(Path.Combine(tempDir, "a.bin"), new byte[100]);
            File.WriteAllBytes(Path.Combine(subDir, "b.bin"), new byte[200]);

            Assert.Equal(300, PathUtil.GetDirectorySize(tempDir));

            var freed = PathUtil.ClearDirectory(tempDir);

            Assert.Equal(300, freed);
            Assert.True(Directory.Exists(tempDir));
            Assert.Empty(Directory.GetFileSystemEntries(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetDirectorySize_ShouldReturnZeroForMissingDirectory()
    {
        Assert.Equal(0, PathUtil.GetDirectorySize(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
        Assert.Equal(0, PathUtil.GetDirectorySize(null));
    }

    [Fact]
    public void GetVfsCacheDir_ShouldComposeRemoteScopedPath()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "rh-cache");

        Assert.Equal(Path.Combine(cacheDir, "vfs", "nas"), PathUtil.GetVfsCacheDir(cacheDir, "nas"));
        Assert.Equal(Path.Combine(cacheDir, "vfsMeta", "nas"), PathUtil.GetVfsMetaDir(cacheDir, "nas"));
    }

    [Fact]
    public void GetDirectoryStats_ShouldCountFilesAndBytes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var subDir = Path.Combine(tempDir, "sub");
        Directory.CreateDirectory(subDir);
        try
        {
            File.WriteAllBytes(Path.Combine(tempDir, "a.bin"), new byte[10]);
            File.WriteAllBytes(Path.Combine(subDir, "b.bin"), new byte[20]);

            var (files, bytes) = PathUtil.GetDirectoryStats(tempDir);

            Assert.Equal(2, files);
            Assert.Equal(30, bytes);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetDirectoryStats_ShouldReturnZeroForMissingDirectory()
    {
        var (files, bytes) = PathUtil.GetDirectoryStats(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.Equal(0, files);
        Assert.Equal(0, bytes);
    }
}
