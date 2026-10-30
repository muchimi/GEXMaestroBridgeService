using GEXMaestroBridgeService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Security.Principal;

const string ServiceName = "MaestroOscService";

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    SelfTests.Run();
    return 0;
}

if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
{
    return InstallService(args);
}

if (args.Contains("--remove", StringComparer.OrdinalIgnoreCase) || args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
{
    return RemoveService();
}

bool mockFlag = args.Contains("--mock", StringComparer.OrdinalIgnoreCase);
var builder = Host.CreateApplicationBuilder(args.Where(a => !a.Equals("--mock", StringComparison.OrdinalIgnoreCase)).ToArray());
// An installed service starts in System32. Resolve configuration beside the executable.
builder.Configuration.SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "MAESTRO_")
    .AddCommandLine(args.Where(a => !a.Equals("--mock", StringComparison.OrdinalIgnoreCase)).ToArray());
builder.Services.AddWindowsService(o => o.ServiceName = ServiceName);
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(180));
var options = builder.Configuration.GetSection("Osc").Get<OscOptions>() ?? new();
if (mockFlag) options.Mock = true;
options.Validate();
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

return 0;

static int InstallService(string[] args)
{
    if (!IsRunningAsAdministrator())
    {
        Console.Error.WriteLine("Installing the service requires an elevated prompt (Run as Administrator).");
        return 1;
    }

    string? exePath = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(exePath))
    {
        Console.Error.WriteLine("Unable to locate current executable path.");
        return 1;
    }

    var serviceArgs = args
        .Where(a => !a.Equals("--install", StringComparison.OrdinalIgnoreCase))
        .Where(a => !a.Equals("--remove", StringComparison.OrdinalIgnoreCase))
        .Where(a => !a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
        .Where(a => !a.Equals("--self-test", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    var binPath = serviceArgs.Length == 0
        ? $"\"{exePath}\""
        : $"\"{exePath}\" {string.Join(' ', serviceArgs)}";

    var create = RunSc($"create {ServiceName} start= auto binPath= \"{binPath}\" DisplayName= \"Maestro OSC Service\"");
    if (create.ExitCode != 0)
    {
        Console.Error.WriteLine(create.Output);
        return create.ExitCode;
    }

    _ = RunSc($"description {ServiceName} \"OSC bridge for Maestro controls\"");
    Console.WriteLine("Service installed.");
    return 0;
}

static int RemoveService()
{
    if (!IsRunningAsAdministrator())
    {
        Console.Error.WriteLine("Removing the service requires an elevated prompt (Run as Administrator).");
        return 1;
    }

    _ = RunSc($"stop {ServiceName}");
    var delete = RunSc($"delete {ServiceName}");
    if (delete.ExitCode != 0)
    {
        Console.Error.WriteLine(delete.Output);
        return delete.ExitCode;
    }

    Console.WriteLine("Service removed.");
    return 0;
}

static bool IsRunningAsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}

static (int ExitCode, string Output) RunSc(string arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "sc.exe",
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        return (-1, "Failed to launch sc.exe.");
    }

    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (!string.IsNullOrWhiteSpace(error))
    {
        output = string.IsNullOrWhiteSpace(output) ? error : $"{output}{Environment.NewLine}{error}";
    }

    return (process.ExitCode, output);
}
