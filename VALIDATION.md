# Validation record — October 30, 2026

Executed in the authoring environment:

- Python unittest suite: **3 tests passed** — independent OSC golden vector, boolean/string encoding, all string padding alignments.
- Python syntax compilation: osc_client.py and verify_dinput.py passed.
- appsettings.json parsed successfully; project XML parsed successfully.
- All C# source files passed a basic lexical delimiter check. This is not C# syntax/type checking or compilation.

Not executed (environment has no dotnet executable, Windows, or HIDMaestro.Core.dll):

- C# compilation, NuGet restore, and SDK binary/API compatibility.
- Included C# --self-test suite for protocol routing, control read/write conventions, state isolation, button 128, axis 8, hat 4, persistence, rename/delete and raw report offsets.
- UDP integration against the actual C# process. The --smoke test is provided for Windows.
- Windows Service installation/lifecycle and LocalSystem operation.
- Actual virtual controller creation, driver installation, DirectInput capability counts and report delivery, console profile behavior, or hot-plug rename behavior.

Run the Windows validation commands in README.md. The included verify_dinput.py checks enumerated DirectInput button/axis/POV counts independently of bridge configuration. Do not treat this source package as a tested binary release.
