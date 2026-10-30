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
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GEXMaestroBridgeService;

public sealed class OscOptions
{
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int ReceivePort { get; set; } = 8010;
    public int SendPort { get; set; } = 8011;
    public string ReplyAddress { get; set; } = "";
    public string[] AllowedSenders { get; set; } = ["127.0.0.1"];
    public int ReceiveBufferBytes { get; set; } = 1048576;
    public int MaxDevices { get; set; } = 16;
    public bool InstallDriverOnStart { get; set; } = true;
    public bool Mock { get; set; }
    public string DefinitionsPath { get; set; } = "";
    public void Validate()
    {
        if (!IPAddress.TryParse(ListenAddress, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("ListenAddress must be an IPv4 literal");
        if (ReceivePort is < 1 or > 65535 || SendPort is < 1 or > 65535 || ReceivePort == SendPort)
            throw new ArgumentException("Receive/send ports must be different and in 1..65535");
        if (ReplyAddress.Length > 0 && (!IPAddress.TryParse(ReplyAddress, out var reply) || reply.AddressFamily != AddressFamily.InterNetwork))
            throw new ArgumentException("ReplyAddress must be blank or an IPv4 literal");
        if (AllowedSenders.Length == 0 || AllowedSenders.Any(a => !IPAddress.TryParse(a, out var sender) || sender.AddressFamily != AddressFamily.InterNetwork))
            throw new ArgumentException("AllowedSenders must contain at least one IPv4 literal");
        if (ReceiveBufferBytes is < 65536 or > 16777216 || MaxDevices is < 1 or > 256)
            throw new ArgumentException("Invalid receive buffer size or device limit");
    }
}

public sealed class OscWorker(IMaestroBackend backend, OscOptions options, ILogger<OscWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Parse(options.ListenAddress), options.ReceivePort));
        receiver.Client.ReceiveBufferSize = options.ReceiveBufferBytes;
        // Replies go TO port 8011; this socket's source port is ephemeral.
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        var allowed = options.AllowedSenders.Select(IPAddress.Parse).ToHashSet();
        string definitionsPath = options.DefinitionsPath.Length > 0 ? Path.GetFullPath(options.DefinitionsPath) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MaestroOscService",
                options.Mock ? "devices.mock.json" : "devices.json");
        backend.Initialize(options.InstallDriverOnStart);
        var catalog = new DefinitionCatalog(backend, options.MaxDevices, definitionsPath);
        catalog.Restore();
        var router = new CommandRouter(backend, options.MaxDevices, SystemDevices.Enumerate, catalog);
        logger.LogInformation("Maestro OSC listening at {Address}:{Port}; reply destination port {ReplyPort}; mock={Mock}",
            options.ListenAddress, options.ReceivePort, options.SendPort, options.Mock);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var datagram = await receiver.ReceiveAsync(stoppingToken);
                if (!allowed.Contains(datagram.RemoteEndPoint.Address)) continue;
                var destination = new IPEndPoint(options.ReplyAddress.Length == 0 ? datagram.RemoteEndPoint.Address
                    : IPAddress.Parse(options.ReplyAddress), options.SendPort);
                IReadOnlyList<OscMessage> commands;
                try { commands = OscCodec.Decode(datagram.Buffer); }
                catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                {
                    await Reply(CommandRouter.Error(-1, "", "bad_packet", ex.Message), destination, sender, stoppingToken);
                    continue;
                }
                // Decode the entire packet before dispatching. Malformed bundles have no side effects.
                // Keep SDK operations serialized so independent button/axis updates cannot overwrite one another.
                foreach (var command in commands)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    foreach (var reply in router.Handle(command, datagram.RemoteEndPoint.ToString()))
                        await Reply(reply, destination, sender, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            // Host DI disposes the backend after the worker stops, removing owned controllers.
            logger.LogInformation("Maestro OSC listener stopped");
        }
    }
    private async Task Reply(OscMessage message, IPEndPoint destination, UdpClient sender, CancellationToken token)
    {
        try { await sender.SendAsync(OscCodec.Encode(message).AsMemory(), destination, token); }
        catch (SocketException ex) { logger.LogWarning(ex, "Could not send OSC reply to {Destination}", destination); }
    }
}
