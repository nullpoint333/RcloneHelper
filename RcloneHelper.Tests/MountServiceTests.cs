using RcloneHelper.Models;
using RcloneHelper.Services;

namespace RcloneHelper.Tests;

/// <summary>
/// 挂载参数构建的单元测试（rclone 命令行的关键部分）
/// </summary>
public class MountServiceTests
{
    #region BuildRemotePath

    [Fact]
    public void BuildRemotePath_WebDav_ShouldNotAppendPath()
    {
        // WebDAV 的路径已经在配置文件 url 中，remote 部分不能再拼一次
        var mount = new MountInfo { Type = "webdav", Host = "example.com", Path = "dav" };

        Assert.Equal("nas:", MountService.BuildRemotePath("nas", mount));
    }

    [Fact]
    public void BuildRemotePath_WebDav_ShouldAppendRemotePath()
    {
        var mount = new MountInfo { Type = "webdav", Host = "example.com", RemotePath = "/team" };

        Assert.Equal("nas:/team", MountService.BuildRemotePath("nas", mount));
    }

    [Theory]
    [InlineData("ftp", "/pub", "nas:/pub")]
    [InlineData("ftp", "pub", "nas:/pub")]
    [InlineData("sftp", "/home/user/files", "nas:/home/user/files")]
    public void BuildRemotePath_FtpSftp_ShouldAppendConfiguredPath(string type, string path, string expected)
    {
        var mount = new MountInfo { Type = type, Host = "example.com", Path = path };

        Assert.Equal(expected, MountService.BuildRemotePath("nas", mount));
    }

    [Fact]
    public void BuildRemotePath_Ftp_ShouldUseRemotePathWhenPathEmpty()
    {
        var mount = new MountInfo { Type = "ftp", Host = "example.com", RemotePath = "pub" };

        Assert.Equal("nas:/pub", MountService.BuildRemotePath("nas", mount));
    }

    [Fact]
    public void BuildRemotePath_FtpWithoutSubPath_ShouldEndWithColon()
    {
        var mount = new MountInfo { Type = "ftp", Host = "example.com" };

        Assert.Equal("nas:", MountService.BuildRemotePath("nas", mount));
    }

    [Fact]
    public void BuildRemotePath_S3_ShouldUseBucketAsPath()
    {
        var mount = new MountInfo { Type = "s3", RemotePath = "/bucket/photos" };

        Assert.Equal("nas:bucket/photos", MountService.BuildRemotePath("nas", mount));
    }

    [Fact]
    public void BuildRemotePath_S3WithoutBucket_ShouldEndWithColon()
    {
        var mount = new MountInfo { Type = "s3", RemotePath = "" };

        Assert.Equal("nas:", MountService.BuildRemotePath("nas", mount));
    }

    #endregion

    #region BuildConfigCreateArgs

    [Fact]
    public void BuildConfigCreateArgs_WebDav_ShouldUseUrlAndVendor()
    {
        var mount = new MountInfo
        {
            Type = "webdav",
            Url = "https://example.com/dav",
            User = "admin",
            Password = "secret",
            Vendor = "nextcloud"
        };

        var args = MountService.BuildConfigCreateArgs("nas", mount);

        Assert.StartsWith("config create nas webdav ", args);
        Assert.Contains("url \"https://example.com/dav\"", args);
        Assert.Contains("user \"admin\"", args);
        Assert.Contains("pass \"secret\"", args);
        Assert.Contains("vendor \"nextcloud\"", args);
        Assert.DoesNotContain("host", args);
        Assert.Contains("--obscure", args);
        Assert.Contains("--non-interactive", args);
    }

    [Fact]
    public void BuildConfigCreateArgs_Ftp_ShouldUseHostAndPortInsteadOfUrl()
    {
        var mount = new MountInfo
        {
            Type = "ftp",
            Host = "ftp.example.com",
            Port = 21,
            Path = "/pub",
            User = "user",
            Password = "pass"
        };

        var args = MountService.BuildConfigCreateArgs("nas", mount);

        Assert.Contains("host \"ftp.example.com\"", args);
        Assert.Contains("port \"21\"", args);
        Assert.Contains("user \"user\"", args);
        Assert.Contains("pass \"pass\"", args);
        Assert.DoesNotContain("url ", args);
    }

    [Fact]
    public void BuildConfigCreateArgs_S3_ShouldPassProviderAndKeys()
    {
        var mount = new MountInfo
        {
            Type = "s3",
            S3Provider = "AWS",
            AccessKeyId = "AKID",
            SecretAccessKey = "SECRET",
            Region = "us-east-1",
            Endpoint = "s3.example.com"
        };

        var args = MountService.BuildConfigCreateArgs("nas", mount);

        Assert.StartsWith("config create nas s3 ", args);
        Assert.Contains("provider \"AWS\"", args);
        Assert.Contains("access_key_id \"AKID\"", args);
        Assert.Contains("secret_access_key \"SECRET\"", args);
        Assert.Contains("region \"us-east-1\"", args);
        Assert.Contains("endpoint \"s3.example.com\"", args);
        Assert.DoesNotContain("url ", args);
    }

    [Fact]
    public void BuildConfigCreateArgs_S3Google_ShouldMapToGcsProvider()
    {
        var mount = new MountInfo
        {
            Type = "s3",
            S3Provider = "Google",
            AccessKeyId = "AKID",
            SecretAccessKey = "SECRET"
        };

        var args = MountService.BuildConfigCreateArgs("nas", mount);

        Assert.Contains("provider \"GCS\"", args);
    }

    [Fact]
    public void BuildConfigCreateArgs_ShouldEscapeQuotes()
    {
        var mount = new MountInfo
        {
            Type = "webdav",
            Url = "https://example.com/dav",
            User = "a\"b"
        };

        var args = MountService.BuildConfigCreateArgs("nas", mount);

        Assert.Contains("user \"a\\\"b\"", args);
    }

    #endregion
}
