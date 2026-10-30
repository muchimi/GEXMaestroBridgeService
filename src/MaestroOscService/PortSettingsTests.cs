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

using Microsoft.Extensions.Configuration;

namespace GEXMaestroBridgeService;

public static class PortSettingsTests
{
    public static int Run()
    {
        int checks = 0;
        using var empty = (ConfigurationRoot)new ConfigurationBuilder().Build();
        using var oldSettings = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Osc:ReceivePort"] = "8010", ["Osc:SendPort"] = "8011" }).Build();
        void Expect(string[] args, int receive, int output, IConfiguration? config = null)
        {
            var result = PortPair.Resolve(config ?? empty, StartupArguments.Parse(args));
            if (result != new PortPair(receive, output)) throw new Exception("FAIL: ports for " + string.Join(' ', args));
            checks++;
        }
        void Reject(string[] args)
        {
            try { PortPair.Resolve(empty, StartupArguments.Parse(args)); }
            catch (ArgumentException) { checks++; return; }
            throw new Exception("FAIL: accepted invalid ports: " + string.Join(' ', args));
        }
        Expect([], 8010, 8011);
        Expect(["9000"], 9000, 9001);
        Expect(["9000", "9100"], 9000, 9100);
        Expect(["--port", "9000"], 9000, 9001);
        Expect(["--port=9000", "--output-port=9100"], 9000, 9100);
        Expect(["--receive-port", "9000", "--send-port", "9100"], 9000, 9100);
        Expect(["--Osc:ReceivePort", "9000"], 9000, 9001);
        Expect(["--Osc:ReceivePort=9000", "--Osc:SendPort=9100"], 9000, 9100);
        Expect(["--output-port", "9100"], 8010, 9100);
        Expect(["--mock", "9000"], 9000, 9001);
        Expect(["65534"], 65534, 65535);
        Expect(["65535", "8011"], 65535, 8011);
        // Receive-only CLI must override the 8011 in an older appsettings.json.
        Expect(["9000"], 9000, 9001, oldSettings);
        Expect(["--port", "9000", "--output-port", "9200"], 9000, 9200, oldSettings);
        using var oneSetting = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Osc:ReceivePort"] = "9500" }).Build();
        Expect([], 9500, 9501, oneSetting);
        using var pairSettings = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Osc:ReceivePort"] = "9500", ["Osc:SendPort"] = "9600" }).Build();
        Expect([], 9500, 9600, pairSettings);
        using var higherReceive = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Osc:ReceivePort"] = "8010", ["Osc:SendPort"] = "8011" })
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Osc:ReceivePort"] = "9700" }).Build();
        Expect([], 9700, 9701, higherReceive);
        var parsed = StartupArguments.Parse(["--mock", "9000", "--Osc:MaxDevices", "8", "--Osc:ReplyAddress=127.0.0.1"]);
        if (!parsed.Mock || !parsed.HostArguments.SequenceEqual(new[] { "--Osc:MaxDevices", "8", "--Osc:ReplyAddress=127.0.0.1" }))
            throw new Exception("FAIL: Host switches were not preserved");
        checks++;
        foreach (string[] args in new string[][] {
            ["0"], ["-1"], ["65536"], ["65535"], ["9000", "9000"], ["9000", "0"],
            ["9000", "9100", "9200"], ["--port"], ["--port=abc"], ["--output-port=0"],
            ["--port", "9000", "--port", "9100"], ["--port", "--mock"], ["1.5"], ["text"]
        }) Reject(args);
        return checks;
    }
}
