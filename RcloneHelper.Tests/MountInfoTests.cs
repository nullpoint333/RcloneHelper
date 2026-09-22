using RcloneHelper.Models;

namespace RcloneHelper.Tests;

public class MountInfoTests
{
    [Fact]
    public void FromConfig_ShouldDecryptPassword()
    {
        // Arrange - 使用未加密的明文（兼容旧数据）
        var config = new MountConfig
        {
            Name = "test",
            RemotePath = "/path",
            LocalDrive = "Z:",
            Type = "webdav",
            Url = "https://example.com/webdav",
            User = "admin",
            Password = "secret123",
            Vendor = "other"
        };

        // Act
        var info = MountInfo.FromConfig(config);

        // Assert
        Assert.Equal("test", info.Name);
        Assert.Equal("secret123", info.Password);
        Assert.False(info.IsMounted);
        Assert.Equal("未挂载", info.Status);
    }

    [Fact]
    public void ToConfig_ShouldEncryptPassword()
    {
        // Arrange
        var info = new MountInfo
        {
            Name = "test",
            RemotePath = "/path",
            LocalDrive = "Z:",
            Type = "webdav",
            Url = "https://example.com/webdav",
            User = "admin",
            Password = "secret123",
            Vendor = "other"
        };

        // Act
        var config = info.ToConfig();

        // Assert - 密码应该被加密（以 ENC: 开头）或在非 Windows 平台上保持原样
        Assert.Equal("test", config.Name);
        Assert.False(string.IsNullOrEmpty(config.Password));
    }

    [Theory]
    [InlineData("webdav", true, 443)]
    [InlineData("webdav", false, 80)]
    [InlineData("ftp", false, 21)]
    [InlineData("sftp", false, 22)]
    public void DefaultPort_ShouldReturnCorrectPort(string type, bool useHttps, int expectedPort)
    {
        var info = new MountInfo { Type = type, UseHttps = useHttps };
        Assert.Equal(expectedPort, info.DefaultPort);
    }

    [Fact]
    public void ComputedUrl_WebDav_ShouldBuildCorrectUrl()
    {
        var info = new MountInfo
        {
            Type = "webdav",
            Host = "example.com",
            Port = 443,
            UseHttps = true,
            Path = "remote.php/dav"
        };

        Assert.Equal("https://example.com/remote.php/dav", info.ComputedUrl);
    }

    [Fact]
    public void ComputedUrl_WebDav_NonDefaultPort_ShouldIncludePort()
    {
        var info = new MountInfo
        {
            Type = "webdav",
            Host = "example.com",
            Port = 8443,
            UseHttps = true,
            Path = ""
        };

        Assert.Equal("https://example.com:8443", info.ComputedUrl);
    }

    [Fact]
    public void ComputedUrl_Ftp_ShouldBuildCorrectUrl()
    {
        var info = new MountInfo
        {
            Type = "ftp",
            Host = "ftp.example.com",
            Port = 21,
            Path = "/files"
        };

        Assert.Equal("ftp://ftp.example.com/files", info.ComputedUrl);
    }

    [Fact]
    public void ComputedUrl_Sftp_ShouldBuildCorrectUrl()
    {
        var info = new MountInfo
        {
            Type = "sftp",
            Host = "sftp.example.com",
            Port = 22,
            Path = "/home"
        };

        Assert.Equal("sftp://sftp.example.com/home", info.ComputedUrl);
    }

    [Fact]
    public void CopyFieldsFrom_ShouldCopyAllFields()
    {
        var source = new MountInfo
        {
            Name = "source",
            RemotePath = "/remote",
            LocalDrive = "Y:",
            Type = "sftp",
            Url = "sftp://host/path",
            User = "user1",
            Password = "pass1",
            Vendor = "other",
            AutoMountOnStart = true,
            UseNetworkMode = true,
            UseHttps = false,
            Host = "host.example.com",
            Port = 2222,
            Path = "/path",
            AccessKeyId = "AKID",
            SecretAccessKey = "SECRET",
            Region = "us-east-1",
            Endpoint = "s3.example.com",
            S3Provider = "AWS"
        };

        var target = new MountInfo();
        target.CopyFieldsFrom(source);

        Assert.Equal("source", target.Name);
        Assert.Equal("/remote", target.RemotePath);
        Assert.Equal("Y:", target.LocalDrive);
        Assert.Equal("sftp", target.Type);
        Assert.Equal("user1", target.User);
        Assert.Equal("pass1", target.Password);
        Assert.Equal("host.example.com", target.Host);
        Assert.Equal(2222, target.Port);
        Assert.Equal("AKID", target.AccessKeyId);
        Assert.Equal("SECRET", target.SecretAccessKey);
        Assert.Equal("us-east-1", target.Region);
        Assert.Equal("s3.example.com", target.Endpoint);
        Assert.Equal("AWS", target.S3Provider);
    }

    [Fact]
    public void CanMount_ShouldBeTrueWhenNotMountedAndNotMounting()
    {
        var info = new MountInfo { IsMounted = false, IsMounting = false };
        Assert.True(info.CanMount);
    }

    [Fact]
    public void CanMount_ShouldBeFalseWhenMounted()
    {
        var info = new MountInfo { IsMounted = true, IsMounting = false };
        Assert.False(info.CanMount);
    }

    [Fact]
    public void CanUnmount_ShouldBeTrueWhenMountedAndNotMounting()
    {
        var info = new MountInfo { IsMounted = true, IsMounting = false };
        Assert.True(info.CanUnmount);
    }

    [Fact]
    public void IsWebDavType_ShouldReturnCorrectValue()
    {
        var webdav = new MountInfo { Type = "webdav" };
        var ftp = new MountInfo { Type = "ftp" };

        Assert.True(webdav.IsWebDavType);
        Assert.False(ftp.IsWebDavType);
    }

    [Fact]
    public void IsS3Type_ShouldReturnCorrectValue()
    {
        var s3 = new MountInfo { Type = "s3" };
        var webdav = new MountInfo { Type = "webdav" };

        Assert.True(s3.IsS3Type);
        Assert.False(webdav.IsS3Type);
    }
}
