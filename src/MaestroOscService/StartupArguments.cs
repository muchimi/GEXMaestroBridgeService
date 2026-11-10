/*
 
 Gremlin Ex and the Gremlin Ex Bridge Service for HIDMaestro is (C) EMCS 2026 under GPL 3.0


 HIDMaestro is under a MIT Open Source license and (C) 2026 HIDMaestro Contributors
 Project details: (https://github.com/hifihedgehog/HIDMaestro) and https://hidmaestro.org/

 Please note that GremlinEx does not distribute HIDMaestro components or any of its dependencies. 
 You must obtain the HIDMaestro SDK and distributables from the HIDMaestro project.
 

 This program is free software: you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation, either version 3 of the License, or
 (at your option) any later version.

 This program is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 GNU General Public License for more details.

 You should have received a copy of the GNU General Public License
 along with this program.  If not, see <http://www.gnu.org/licenses/>.
 
  
 */



using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace GEXMaestroBridgeService;

/// <summary>
/// Handles startup arguments
/// </summary>
/// <param name="ReceivePort">the UDP receive port, can be nul and defaults to 8010</param>
/// <param name="OutputPort">the UDP reply port, can be nul and defaults to receive port + 1</param>
/// <param name="Mock">if set, places the app in simulation mode</param>
/// <param name="HostArguments">arbitrary other start arguments</param>
public sealed record StartupArguments(int? ReceivePort, int? OutputPort, bool Mock, string[] HostArguments)
{
    public const string Usage = """
        GEXMaestroBridgeService.exe [receivePort [outputPort]] [--mock]
        GEXMaestroBridgeService.exe [--port receivePort] [--output-port outputPort] [--mock]
        GEXMaestroBridgeService.exe [runtime options] [--Osc:Setting value ...]

        Actions (print a result and exit; use one per invocation):
          --help, -h         Print this usage text.
          --version, -v      Print the application version.
          --test             Run built-in self-tests without creating HID hardware.
          --install          Register an automatic Windows service; does not start it.
          --uninstall        Stop and unregister the Windows service.
          --start            Start the registered Windows service.
          --stop             Stop the registered Windows service if running.

        Runtime options:
          --port <number>         Receive UDP port (default 8010).
            Aliases: --receive-port, --udp-port, --Osc:ReceivePort.
          --output-port <number>  Reply destination UDP port (default 8011).
            Aliases: --send-port, --Osc:SendPort.
          --mock                  Use the mock backend without creating HID hardware.

        Configuration overrides:
          --Osc:ListenAddress <IPv4>          Bind address; blank config uses a host IPv4.
          --Osc:ReplyAddress <IPv4>           Fixed reply IP; blank config uses sender IP.
          --Osc:AllowedSenders:<index> <IPv4> Allowed sender, indexed from 0.
          --Osc:DefinitionsPath <path>        Saved device definitions file.
          --Osc:MaxDevices <number>           Device limit, 1..256 (default 16).
          --Osc:ReceiveBufferBytes <number>   Buffer, 65536..16777216 (default 1048576).
          --Osc:InstallDriverOnStart <bool>   Request driver installation (default true).
          --Osc:Mock <bool>                   Select mock backend (default false).
          --Logging:LogLevel:Default <level>  Default application log level.
          --Logging:LogLevel:Microsoft.Hosting.Lifetime <level>
          --Logging:EventLog:LogLevel:Default <level>
        Other configuration keys accept --Section:Setting value or =value.
        Boolean configuration values must be true or false; --mock takes no value.

        Built-in switch prefixes: --, - or / (case-insensitive).
        Named port/configuration values accept a space or = before the value.
        Ports must be different integers in 1..65535. A receive-only override selects
        receive port + 1 for replies; receive port 65535 needs an explicit output port.
        No arguments explicitly selects ports 8010/8011, overriding configured ports.
        --install and --start do not save or forward runtime arguments to the service.
        See COMMAND-LINE.md for service IP/port configuration and examples in both shells.
        """;

    public static StartupArguments Parse(string[] args)
    {
        int? receive = null, output = null;
        bool mock = false;
        var host = new List<string>();
        // provide defaults
        if (args.Length == 0)
        {
            // provide defaults if no argument supplied and run on console mode
            // use default host IP and ports
            var hostIP = NetworkDefaults.ResolveHostIPv4().ToString();
            if (Environment.UserInteractive)
            {
                Console.WriteLine("No arguments supplied, using default ports 8010 and 8011.");
                Console.WriteLine("Use --help for usage information.");
            }
            return new(8010, 8011, false, new string[] { hostIP?.ToString() ?? "127.0.0.1" });
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            int equal = arg.IndexOf('=');
            string rawKey = equal >= 0 ? arg[..equal] : arg;
            string key = NormalizeSwitch(rawKey);
            if (key.Equals("--mock", StringComparison.OrdinalIgnoreCase)) { mock = true; continue; }
            bool isReceive = key is "--port" or "--receive-port" or "--udp-port" or "--osc:receiveport";
            bool isOutput = key is "--output-port" or "--send-port" or "--osc:sendport";
            if (isReceive || isOutput)
            {
                string value = equal >= 0 ? arg[(equal + 1)..] : ++i < args.Length ? args[i]
                    : throw new ArgumentException($"Missing value for {key}");
                int port = ParsePort(value, key);
                if (isReceive)
                {
                    if (receive.HasValue) throw new ArgumentException("Receive port was supplied more than once");
                    receive = port;
                }
                else
                {
                    if (output.HasValue) throw new ArgumentException("Output port was supplied more than once");
                    output = port;
                }
                continue;
            }
            if (IsSwitch(rawKey))
            {
                // Preserve ordinary Host configuration switches and their values.
                host.Add(arg);
                if (equal < 0)
                {
                    if (++i >= args.Length || IsSwitch(args[i]))
                        throw new ArgumentException($"Missing value for {arg}");
                    host.Add(args[i]);
                }
                continue;
            }
            int positional = ParsePort(arg, "positional port");
            if (!receive.HasValue) receive = positional;
            else if (!output.HasValue) output = positional;
            else throw new ArgumentException("Supply at most two positional ports");
        }
        return new(receive, output, mock, host.ToArray());
    }

    internal static int ParsePort(string text, string name)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value is < 1 or > 65535)
            throw new ArgumentException($"{name} must be an integer in 1..65535");
        return value;
    }

    private static bool IsSwitch(string value) => value.StartsWith("--", StringComparison.Ordinal) ||
        value.StartsWith("-", StringComparison.Ordinal) || value.StartsWith("/", StringComparison.Ordinal);

    private static string NormalizeSwitch(string value)
    {
        string key = value;
        if (key.StartsWith("--", StringComparison.Ordinal)) key = key[2..];
        else if (key.StartsWith("-", StringComparison.Ordinal) || key.StartsWith("/", StringComparison.Ordinal)) key = key[1..];
        return "--" + key.ToLowerInvariant();
    }
}

public readonly record struct PortPair(int Receive, int Output)
{
    public static PortPair Resolve(IConfiguration configuration, StartupArguments arguments)
    {
        var receiveSetting = LastSetting(configuration, "Osc:ReceivePort");
        var outputSetting = LastSetting(configuration, "Osc:SendPort");
        int receive = arguments.ReceivePort ?? receiveSetting.Port ?? 8010;
        // A receive-only override at a higher priority also overrides any older output port.
        int output = arguments.OutputPort ??
            (arguments.ReceivePort.HasValue || receiveSetting.Priority > outputSetting.Priority
                ? receive + 1 : outputSetting.Port ?? receive + 1);
        if (receive is < 1 or > 65535 || output is < 1 or > 65535 || receive == output)
            throw new ArgumentException("Receive/output ports must be different and in 1..65535. " +
                "Receive port 65535 requires an explicit output port.");
        return new(receive, output);
    }

    private static (int? Port, int Priority) LastSetting(IConfiguration configuration, string key)
    {
        if (configuration is IConfigurationRoot root)
        {
            var providers = root.Providers.ToArray();
            for (int i = providers.Length - 1; i >= 0; i--)
                if (providers[i].TryGet(key, out string? value) && value is not null)
                    return (StartupArguments.ParsePort(value, key), i);
            return (null, -1);
        }
        string? fallback = configuration[key];
        return fallback is null ? (null, -1) : (StartupArguments.ParsePort(fallback, key), 0);
    }
}
