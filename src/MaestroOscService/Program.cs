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

using GEXMaestroBridgeService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Security.Authentication.ExtendedProtection;
using System.Security.Principal;

const string ServiceName = "GEXMaestroBridgeService";



if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    SelfTests.Run();
    return;
}
if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a == "-h"))
{
    Console.WriteLine(StartupArguments.Usage);
    return;
}
StartupArguments startup;
try { startup = StartupArguments.Parse(args); }
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(StartupArguments.Usage);
    Environment.ExitCode = 2;
    return;
}
var builder = Host.CreateApplicationBuilder(startup.HostArguments);
// An installed service starts in System32. Resolve configuration beside the executable.
builder.Configuration.SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "MAESTRO_")
    .AddCommandLine(startup.HostArguments);
builder.Services.AddWindowsService(o => o.ServiceName = ServiceName);
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(180));
var options = builder.Configuration.GetSection("Osc").Get<OscOptions>() ?? new();
if (startup.Mock) options.Mock = true;
if (string.IsNullOrWhiteSpace(options.ListenAddress))
    options.ListenAddress = NetworkDefaults.ResolveHostIPv4().ToString();
try
{
    var ports = PortPair.Resolve(builder.Configuration, startup);
    options.ReceivePort = ports.Receive;
    options.SendPort = ports.Output;
    options.Validate();
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(StartupArguments.Usage);
    Environment.ExitCode = 2;
    return;
}
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IMaestroBackend>(_ =>
{
    if (options.Mock) return new MockBackend();
#if MOCK_ONLY
    throw new InvalidOperationException("This build has no HIDMaestro SDK. Use --mock or rebuild with the real DLL.");
#else
    return new HidMaestroBackend();
#endif
});
builder.Services.AddHostedService<OscWorker>();
await builder.Build().RunAsync();