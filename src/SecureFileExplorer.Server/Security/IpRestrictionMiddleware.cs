using System.Net;
using System.Net.Sockets;

namespace SecureFileExplorer.Server.Security;

public sealed class IpRestrictionOptions
{
    /// <summary>true の場合のみIP制限を有効化。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 許可するCIDR一覧（社内LAN/Wi-Fiのレンジ）。
    /// 例: ["10.0.0.0/8", "192.168.0.0/16", "127.0.0.1/32"]
    /// </summary>
    public List<string> AllowedCidrs { get; set; } = new() { "127.0.0.1/32", "::1/128" };
}

/// <summary>
/// 社内ネットワーク以外からのアクセスを拒否する。接続元IPが許可CIDRに含まれなければ403。
/// （多層防御の一層。実運用ではWindows Firewallでも接続元を絞ること。）
/// </summary>
public sealed class IpRestrictionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IpRestrictionOptions _opt;
    private readonly ILogger<IpRestrictionMiddleware> _log;
    private readonly List<(IPAddress network, int prefix)> _ranges;

    public IpRestrictionMiddleware(RequestDelegate next, IpRestrictionOptions opt, ILogger<IpRestrictionMiddleware> log)
    {
        _next = next;
        _opt = opt;
        _log = log;
        _ranges = opt.AllowedCidrs.Select(Parse).Where(x => x is not null).Select(x => x!.Value).ToList();
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!_opt.Enabled)
        {
            await _next(ctx);
            return;
        }

        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null || !_ranges.Any(r => InRange(remote, r.network, r.prefix)))
        {
            _log.LogWarning("Blocked request from disallowed IP: {Ip}", remote);
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsync("Access denied: outside allowed network.");
            return;
        }

        await _next(ctx);
    }

    private static (IPAddress network, int prefix)? Parse(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2) return null;
        if (!IPAddress.TryParse(parts[0], out var addr)) return null;
        if (!int.TryParse(parts[1], out var prefix)) return null;
        return (addr, prefix);
    }

    private static bool InRange(IPAddress address, IPAddress network, int prefixLength)
    {
        // IPv4射影されたIPv6 (::ffff:a.b.c.d) はIPv4へ正規化して比較。
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (network.IsIPv4MappedToIPv6) network = network.MapToIPv4();
        if (address.AddressFamily != network.AddressFamily) return false;

        var addrBytes = address.GetAddressBytes();
        var netBytes = network.GetAddressBytes();
        if (addrBytes.Length != netBytes.Length) return false;

        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        for (int i = 0; i < fullBytes; i++)
            if (addrBytes[i] != netBytes[i]) return false;

        if (remainingBits == 0) return true;
        int mask = (byte)~(0xFF >> remainingBits);
        return (addrBytes[fullBytes] & mask) == (netBytes[fullBytes] & mask);
    }
}

public static class IpRestrictionMiddlewareExtensions
{
    public static IApplicationBuilder UseIpRestriction(this IApplicationBuilder app, IpRestrictionOptions opt)
        => app.UseMiddleware<IpRestrictionMiddleware>(opt);
}
