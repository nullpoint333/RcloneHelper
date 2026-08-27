using RcloneHelper.Models;

namespace RcloneHelper.Tests;

public class AppConfigTests
{
    [Fact]
    public void DefaultValues_ShouldBeCorrect()
    {
        var config = new AppConfig();

        Assert.True(config.IsDarkMode);
        Assert.False(config.StartSilently);
        Assert.False(config.ProxyEnabled);
        Assert.Equal("http", config.ProxyProtocol);
        Assert.Equal("", config.ProxyHost);
        Assert.Equal(7890, config.ProxyPort);
        Assert.Equal("", config.ProxyUser);
        Assert.Equal("", config.ProxyPassword);
        Assert.Equal(1, config.ConfigVersion);
    }

    [Fact]
    public void GetProxyUrl_WhenDisabled_ShouldReturnEmpty()
    {
        var config = new AppConfig
        {
            ProxyEnabled = false,
            ProxyHost = "127.0.0.1",
            ProxyPort = 7890
        };

        Assert.Equal("", config.GetProxyUrl());
    }

    [Fact]
    public void GetProxyUrl_WhenEnabled_ShouldReturnFormattedUrl()
    {
        var config = new AppConfig
        {
            ProxyEnabled = true,
            ProxyProtocol = "http",
            ProxyHost = "127.0.0.1",
            ProxyPort = 7890
        };

        Assert.Equal("http://127.0.0.1:7890", config.GetProxyUrl());
    }

    [Fact]
    public void GetProxyUrl_WhenEmptyHost_ShouldReturnEmpty()
    {
        var config = new AppConfig
        {
            ProxyEnabled = true,
            ProxyHost = "",
            ProxyPort = 7890
        };

        Assert.Equal("", config.GetProxyUrl());
    }

    [Fact]
    public void GetProxyUrl_WithAuth_ShouldIncludeCredentials()
    {
        var config = new AppConfig
        {
            ProxyEnabled = true,
            ProxyProtocol = "http",
            ProxyHost = "127.0.0.1",
            ProxyPort = 7890,
            ProxyUser = "admin",
            ProxyPassword = "p@ss"
        };

        Assert.Equal("http://admin:p%40ss@127.0.0.1:7890", config.GetProxyUrl());
    }

    [Fact]
    public void GetProxyUrl_WithUserOnly_ShouldIncludeUser()
    {
        var config = new AppConfig
        {
            ProxyEnabled = true,
            ProxyProtocol = "socks5",
            ProxyHost = "127.0.0.1",
            ProxyPort = 1080,
            ProxyUser = "admin"
        };

        Assert.Equal("socks5://admin@127.0.0.1:1080", config.GetProxyUrl());
    }

    [Fact]
    public void ProxyProtocol_ShouldRejectInvalidValues()
    {
        var config = new AppConfig();

        config.ProxyProtocol = "invalid";

        Assert.Equal("http", config.ProxyProtocol);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    [InlineData("socks5")]
    public void ProxyProtocol_ShouldAcceptValidValues(string protocol)
    {
        var config = new AppConfig();

        config.ProxyProtocol = protocol;

        Assert.Equal(protocol, config.ProxyProtocol);
    }

    [Fact]
    public void ProxyProtocol_ShouldNormalizeToLowerCase()
    {
        var config = new AppConfig();

        config.ProxyProtocol = "HTTP";

        Assert.Equal("http", config.ProxyProtocol);
    }

    [Fact]
    public void ProxyPort_ShouldRejectInvalidValues()
    {
        var config = new AppConfig();

        config.ProxyPort = 0;
        Assert.Equal(7890, config.ProxyPort);

        config.ProxyPort = -1;
        Assert.Equal(7890, config.ProxyPort);

        config.ProxyPort = 70000;
        Assert.Equal(7890, config.ProxyPort);
    }

    [Fact]
    public void ProxyPort_ShouldAcceptValidValues()
    {
        var config = new AppConfig();

        config.ProxyPort = 1080;
        Assert.Equal(1080, config.ProxyPort);

        config.ProxyPort = 65535;
        Assert.Equal(65535, config.ProxyPort);
    }
}
