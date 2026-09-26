using System.Net;
using Microsoft.Extensions.Logging;
using PTZControlServer;
using Xunit;

namespace PTZControlServer.Tests;

public sealed class ServerOptionsTests
{
    [Fact]
    public void Parse_UsesBothLoopbackDefaults()
    {
        var options = ServerOptions.Parse([]);
        Assert.Equal(["http://127.0.0.1:7070", "http://[::1]:7070"], options.ListenUrls);
        Assert.True(options.SwaggerEnabled);
        Assert.Equal(LogLevel.None, options.FileLogLevel);
    }

    [Theory]
    [InlineData("information", LogLevel.Information)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("off", LogLevel.None)]
    public void Parse_AcceptsFileLogLevels(string value, LogLevel expected)
    {
        var options = ServerOptions.Parse(["--log-level", value]);
        Assert.Equal(expected, options.FileLogLevel);
    }

    [Fact]
    public void Parse_AcceptsExplicitLogDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ptzcontrol-test-logs");
        var options = ServerOptions.Parse(["--log-directory", path]);
        Assert.Equal(Path.GetFullPath(path), options.LogDirectory);
    }

    [Fact]
    public void Parse_AcceptsRepeatedOptions()
    {
        var options = ServerOptions.Parse(["--listen", "http://0.0.0.0:7070", "--listen", "http://[::]:7070", "--allow-ip", "192.168.1.*", "--allow-ip", "2001:db8::/32", "--token", "secret", "--no-swagger"]);
        Assert.Equal(2, options.ListenUrls.Count);
        Assert.Equal(2, options.AllowedIps.Count);
        Assert.Equal("secret", options.Token);
        Assert.False(options.SwaggerEnabled);
    }

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1", true)]
    [InlineData("192.168.1.*", "192.168.1.200", true)]
    [InlineData("192.168.1.*", "192.168.2.1", false)]
    [InlineData("10.10.0.0/16", "10.10.22.8", true)]
    [InlineData("10.10.0.0/16", "10.11.0.1", false)]
    [InlineData("2001:db8::/32", "2001:db8::1234", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    public void IpNetworkRule_MatchesExpectedAddresses(string rule, string address, bool expected) =>
        Assert.Equal(expected, IpNetworkRule.Parse(rule).Matches(IPAddress.Parse(address)));

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--listen")]
    [InlineData("--allow-ip")]
    [InlineData("--log-level")]
    public void Parse_RejectsInvalidArguments(string argument) =>
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse([argument]));

    [Fact]
    public void Parse_RejectsUnknownLogLevel() =>
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["--log-level", "verbose"]));
}
