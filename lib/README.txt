Place a complete HIDMaestro 2.x release HIDMaestro.Core.dll here.
It is not included in this source package. The current inspected upstream API targets .NET 10.
Obtain it from https://github.com/hifihedgehog/HIDMaestro/releases
Use the complete official build carrying the embedded Windows driver payloads.
The project copies this assembly beside the executable when building/publishing.
For a different location pass -p:HidMaestroDll=C:\full\path\HIDMaestro.Core.dll
For protocol tests without the DLL, build with -p:MockOnly=true and run with --mock.
