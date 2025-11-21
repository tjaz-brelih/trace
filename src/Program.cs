using System.Text;
using System.Numerics;
using System.Diagnostics;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;


Option<bool> dnsOption = new("--no-dns")
{
    Description = "Do not resolve addresses to hostnames."
};

Option<int> hopsOption = new("--hops")
{
    HelpName = "hops",
    Description = "Maximum number of hops to trace.",
    DefaultValueFactory = _ => 30
};

Option<int> timeoutOption = new("--timeout", "-t")
{
    HelpName = "timeout",
    Description = "Timeout in milliseconds to wait for each reply.",
    DefaultValueFactory = _ => 1000
};

Argument<string> addressArgument = new("address")
{
    Description = "The destination address to trace.",
    DefaultValueFactory = _ => "1.1.1.1"
};


var root = new RootCommand("A faster but not necessarily better trace util.");
root.Options.Add(dnsOption);
root.Options.Add(hopsOption);
root.Options.Add(timeoutOption);
root.Arguments.Add(addressArgument);


hopsOption.Validators.Add(GreaterThanZeroValidator(hopsOption));
timeoutOption.Validators.Add(GreaterThanZeroValidator(timeoutOption));


root.SetAction(x =>
{
    var address = x.GetValue(addressArgument)!;
    var timeout = x.GetValue(timeoutOption);
    var maxHops = x.GetValue(hopsOption);
    var resolveHostnames = !x.GetValue(dnsOption);

    Trace(address, timeout, maxHops, resolveHostnames);
});

return root.Parse(args).Invoke();




static void Trace(string address, int timeout, int maxHops, bool resolveHostnames)
{
    var (hostname, ipAddress) = ResolveHostname(address);

    var ipAddressString = $"[{ipAddress}]";
    var hostString = hostname is not null ? $"{hostname} {ipAddressString}" : ipAddressString;

    Console.WriteLine("Tracing route to " + hostString);
    Console.WriteLine($"over a maximum of {maxHops} hops:");
    Console.WriteLine();


    var buffer = CreateBuffer(32);

    var stopwatch = new Stopwatch();

    var ping = new Ping();
    var opt = new PingOptions(ttl: 1, dontFragment: false);


    while (true)
    {
        stopwatch.Restart();
        var reply = ping.Send(ipAddress, timeout, buffer, opt);
        stopwatch.Stop();

        string elapsed = "*";
        string replyAddress = "Request timed out";

        if (reply.Status != IPStatus.TimedOut)
        {
            elapsed = $"{stopwatch.ElapsedMilliseconds} ms";
            replyAddress = reply.Address.ToString();

            if (resolveHostnames && opt.Ttl > 1)
            {
                try { replyAddress = $"{Dns.GetHostEntry(replyAddress).HostName} [{replyAddress}]"; }
                catch (SocketException) { }
            }
        }

        Console.WriteLine($"{opt.Ttl}\t{elapsed}\t\t{replyAddress}");
        opt.Ttl++;

        if (reply.Status == IPStatus.Success) { break; }
        if (opt.Ttl > maxHops)
        {
            Console.WriteLine("Maximum hops reached.");
            break;
        }
    }
}


static (string? Hostname, IPAddress IpAddress) ResolveHostname(string hostnameOrIpAddress)
{
    if (IPAddress.TryParse(hostnameOrIpAddress, out var ipAddress))
    {
        // We got an IP address.
        return (null, ipAddress);
    }

    try
    {
        var hostEntry = Dns.GetHostEntry(hostnameOrIpAddress);
        return (hostEntry.HostName, hostEntry.AddressList[0]);
    }
    catch (SocketException)
    {
        Console.WriteLine($"Unable to resolve hostname {hostnameOrIpAddress}.");
        Environment.Exit(1);
        return (null, IPAddress.None);
    }
}

static byte[] CreateBuffer(int size)
{
    char[] payload = ['a'];
    var asciiBytes = Encoding.ASCII.GetBytes(payload)[0];

    var buffer = new byte[size];

    for (int i = 0; i < size; i++)
    {
        buffer[i] = asciiBytes;
    }

    return buffer;
}



static Action<OptionResult> GreaterThanZeroValidator<T>(Option<T> opt) where T : INumber<T> => result =>
{
    if (result.GetValue(opt)! <= T.Zero)
    {
        result.AddError($"{opt.HelpName} must be greater than 0");
    }
};