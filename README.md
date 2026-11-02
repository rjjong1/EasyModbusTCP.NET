# EasyModbusTCP.NET - <a href="EasyModbusTCP.NET">www.EasyModbusTCP.NET</a>

## .NET 10 fork

Build the current library with the .NET 10 SDK:

```powershell
dotnet build EasyModbus/EasyModbus.csproj --configuration Release
dotnet test EasyModbus.NET.slnx --configuration Release
```

`EasyModbus/EasyModbus.csproj` compiles the main library sources for `net10.0`,
including the TCP, UDP, and RTU client/server APIs. It keeps the `EasyModbus`
assembly and namespace and uses `System.IO.Ports` 10.0.11 for serial support.
Server shutdown uses cooperative termination instead of the unsupported
`Thread.Abort` API. The .NET 10 tests exercise TCP reads/writes, numeric and string
conversions, TCP restart, and idle UDP shutdown on loopback. Serial hardware is
not needed for these tests.

Use `EasyModbus.NET.slnx` for modern development. `EasyModbus.sln` and the
older .NET Framework, .NET 5, and .NET 6 projects remain available for historical
examples. The repository's `Directory.Build.props` and `Directory.Build.targets`
keep a consuming repository's build settings from changing the library.

The following documentation and license are from the upstream library.

Modbus TCP, Modbus UDP and Modbus RTU client/server library for .NET<br>
Industry approved!!<br>
Fast and secure access from PC or Embedded Systems to many PLC-Systems and other components for industry automation. <br>
Only a few lines of codes are needed to read or write data from or to a PLC. 

<h2><a href="http://www.easymodbustcp.net">Implementation Guide and Codesamples: www.easymodbustcp.net</a></h2>

Additional Software tools e.g. Modbus Server Simulator, makes software development fast and easy. 

Download Library (*.DLL) from NuGet or from: <a href="https://sourceforge.net/projects/easymodbustcp/files/latest/download" rel="nofollow"><img alt="Download EasyModbusTCP/UDP/RTU .NET" src="https://a.fsdn.com/con/app/sf-download-button"></a>



Supported Function Codes:

- Read Coils (FC1)
- Read Discrete Inputs (FC2)
- Read Holding Registers (FC3)
- Read Input Registers (FC4)
- Write Single Coil (FC5)
- Write Single Register (FC6)
- Write Multiple Coils (FC15)
- Write Multiple Registers (FC16)
- Read/Write Multiple Registers (FC23)


Modbus TCP, Modbus UDP and Modbus RTU client/server library


Copyright (c) 2018-2020 Rossmann-Engineering
Permission is hereby granted, free of charge, 
to any person obtaining a copy of this software
and associated documentation files (the "Software"),
to deal in the Software without restriction, 
including without limitation the rights to use, 
copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit 
persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission 
notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, 
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, 
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE 
OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
