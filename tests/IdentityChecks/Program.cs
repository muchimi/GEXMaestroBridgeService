using GEXMaestroBridgeService;

try
{
    Console.WriteLine($"PASS: {GexDeviceIdentityTests.Run()} ordered GEX identity, restoration and SDK checks (no HID hardware created)");
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}
