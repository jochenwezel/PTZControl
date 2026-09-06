using System.Net;

namespace PTZControlServer;

public sealed class ServerOptions
{
    public const int DefaultPort = 7070;
    public const string TokenHeaderName = "X-PTZControl-Token";

    public List<string> ListenUrls { get; } = [];
    public List<IpNetworkRule> AllowedIps { get; } = [];
    public string? Token { get; private set; }
    public bool SwaggerEnabled { get; private set; } = true;
    public bool ShowHelp { get; private set; }

    public static ServerOptions Parse(string[] args)
    {
        var options = new ServerOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--listen":
                    options.ListenUrls.Add(RequireValue(args, ref index, argument));
                    break;
                case "--allow-ip":
                    options.AllowedIps.Add(IpNetworkRule.Parse(RequireValue(args, ref index, argument)));
                    break;
                case "--token":
                    options.Token = RequireValue(args, ref index, argument);
                    break;
                case "--no-swagger":
                    options.SwaggerEnabled = false;
                    break;
                case "--help":
                case "-h":
                case "-?":
                    options.ShowHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{argument}'.");
            }
        }

        if (options.ListenUrls.Count == 0)
        {
            options.ListenUrls.Add($"http://127.0.0.1:{DefaultPort}");
            options.ListenUrls.Add($"http://[::1]:{DefaultPort}");
        }

        foreach (var url in options.ListenUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || uri.Port <= 0)
                throw new ArgumentException($"Invalid HTTP listen URL '{url}'. Example: http://127.0.0.1:{DefaultPort}");
        }

        return options;
    }

    public bool IsRemoteAddressAllowed(IPAddress address) =>
        AllowedIps.Count == 0 || AllowedIps.Any(rule => rule.Matches(address));

    public static string HelpText =>
        """
        PTZControlServer

        Usage:
          PTZControlServer [--listen URL]... [--allow-ip ADDRESS|WILDCARD|CIDR]... [--token SECRET] [--no-swagger]

        Options:
          --listen URL       HTTP address to bind. May be specified multiple times.
                             Default: http://127.0.0.1:7070 and http://[::1]:7070
          --allow-ip RULE    Allow a client IP. Supports exact IP, 192.168.1.*, and CIDR.
                             May be specified multiple times. If omitted, all clients that
                             can reach a configured listen address are allowed.
          --token SECRET     Require X-PTZControl-Token on API and action requests.
          --no-swagger       Disable Swagger JSON and Swagger UI.
          -h, --help, -?     Display this help.

        Swagger UI: /swagger
        OpenAPI JSON: /swagger/v1/swagger.json
        """;

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || args[index].StartsWith('-'))
            throw new ArgumentException($"Option '{option}' requires a value.");
        return args[index];
    }
}

public sealed class IpNetworkRule
{
    private readonly byte[] _network;
    private readonly int _prefixLength;

    private IpNetworkRule(byte[] network, int prefixLength, string source)
    {
        _network = network;
        _prefixLength = prefixLength;
        Source = source;
    }

    public string Source { get; }

    public static IpNetworkRule Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("IP allowlist rule cannot be empty.");

        if (value.Contains('*'))
        {
            var parts = value.Split('.');
            if (parts.Length != 4)
                throw new ArgumentException($"Invalid IPv4 wildcard rule '{value}'.");

            var firstWildcard = Array.IndexOf(parts, "*");
            if (firstWildcard < 0 || parts.Skip(firstWildcard).Any(part => part != "*"))
                throw new ArgumentException($"Wildcards in '{value}' must cover complete trailing IPv4 octets.");

            var addressParts = parts.Select(part => part == "*" ? "0" : part);
            return ParseCidr(string.Join('.', addressParts), firstWildcard * 8, value);
        }

        var slashIndex = value.IndexOf('/');
        if (slashIndex >= 0)
        {
            var addressText = value[..slashIndex];
            if (!int.TryParse(value[(slashIndex + 1)..], out var prefixLength))
                throw new ArgumentException($"Invalid CIDR prefix in '{value}'.");
            return ParseCidr(addressText, prefixLength, value);
        }

        if (!IPAddress.TryParse(value, out var address))
            throw new ArgumentException($"Invalid IP address '{value}'.");
        address = Normalize(address);
        return new IpNetworkRule(address.GetAddressBytes(), address.GetAddressBytes().Length * 8, value);
    }

    public bool Matches(IPAddress address)
    {
        var candidate = Normalize(address).GetAddressBytes();
        if (candidate.Length != _network.Length)
            return false;

        var completeBytes = _prefixLength / 8;
        var remainingBits = _prefixLength % 8;
        for (var index = 0; index < completeBytes; index++)
        {
            if (candidate[index] != _network[index])
                return false;
        }

        if (remainingBits == 0)
            return true;
        var mask = (byte)(0xff << (8 - remainingBits));
        return (candidate[completeBytes] & mask) == (_network[completeBytes] & mask);
    }

    private static IpNetworkRule ParseCidr(string addressText, int prefixLength, string source)
    {
        if (!IPAddress.TryParse(addressText, out var address))
            throw new ArgumentException($"Invalid IP address in '{source}'.");
        address = Normalize(address);
        var bytes = address.GetAddressBytes();
        if (prefixLength < 0 || prefixLength > bytes.Length * 8)
            throw new ArgumentException($"Invalid CIDR prefix in '{source}'.");
        return new IpNetworkRule(bytes, prefixLength, source);
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
