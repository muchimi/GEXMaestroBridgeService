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
using System.ServiceProcess;
using System.Security.Authentication.ExtendedProtection;
using System.Security.Principal;

const string ServiceName = "GEXMaestroBridgeService";

static bool HasSwitch(string[] argv, string name, string? shortName = null) => argv.Any(a =>
{
    if (a.Length == 0) return false;
    string token = a;
    if (token.StartsWith("--", StringComparison.Ordinal)) token = token[2..];
    else if (token.StartsWith("-", StringComparison.Ordinal) || token.StartsWith("/", StringComparison.Ordinal)) token = token[1..];
    if (token.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
    return shortName is not null && token.Equals(shortName, StringComparison.OrdinalIgnoreCase);
});



if (HasSwitch(args, "version", "v"))
{
    Console.WriteLine(typeof(OscWorker).Assembly.GetName().Version?.ToString(3) ?? "unknown");
    return;
}
if (HasSwitch(args, "install"))
{
    try
    {
        string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Could not resolve executable path");
        RunSc($"create {ServiceName} start= auto DisplayName= \"{ServiceName}\" binPath= \"\"{exePath}\"\"");
        Console.WriteLine($"Service '{ServiceName}' installed.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}
if (HasSwitch(args, "uninstall"))
{
    try
    {
        TryStopService(ServiceName);
        RunSc($"delete {ServiceName}");
        Console.WriteLine($"Service '{ServiceName}' removed.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}
if (HasSwitch(args, "start"))
{
    try
    {
        RunSc($"start {ServiceName}");
        Console.WriteLine($"Service '{ServiceName}' start requested.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}
if (HasSwitch(args, "stop"))
{
    try
    {
        if (TryStopService(ServiceName)) Console.WriteLine($"Service '{ServiceName}' stopped.");
        else Console.WriteLine($"Service '{ServiceName}' is not running.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}
if (HasSwitch(args, "test"))
{
    SelfTests.Run();
    return;
}
if (HasSwitch(args, "help", "h"))
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

static void RunSc(string arguments)
{
    var start = new ProcessStartInfo("sc.exe", arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start sc.exe");
    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine(stdout.TrimEnd());
    if (process.ExitCode != 0)
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? $"sc.exe failed with exit code {process.ExitCode}." : stderr.Trim());
}

static bool TryStopService(string serviceName)
{
    using var controller = new ServiceController(serviceName);
    try { _ = controller.Status; }
    catch (InvalidOperationException) { return false; }
    if (controller.Status == ServiceControllerStatus.Stopped) return false;
    if (controller.Status == ServiceControllerStatus.StopPending)
    {
        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        return true;
    }
    controller.Stop();
    controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
    return true;
}
