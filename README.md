# EasyModbusTCP.NET for .NET 10

A modernized fork of the [Rossmann Engineering EasyModbus library](https://github.com/rossmann-engineering/EasyModbusTCP.NET),
maintained for source integration with Texplorer Suite. The library targets
`net10.0` and supports Modbus TCP, UDP, and RTU clients and servers.

## Build and test

Install the .NET 10 SDK, then run from the repository root:

```powershell
dotnet build EasyModbus.slnx --configuration Release
dotnet test EasyModbus.slnx --configuration Release
dotnet run --project ConsoleAppStd --configuration Release
```

The console example starts a loopback server, reads its registers, and disposes
both endpoints. Pass a host and optional port to connect to an existing server:

```powershell
dotnet run --project ConsoleAppStd -- 192.168.0.10 502
```

GitHub Actions builds and tests the solution on Windows and Linux. Regression
tests exercise all supported function codes over TCP and UDP, fragmented and
batched TCP frames, signed values and address boundaries, malformed packets,
timeouts, cancellation, concurrent calls, shutdown, and register conversions.
RTU CRC framing is tested without hardware; serial driver behavior still needs
validation on the intended hardware.

## Client

```csharp
using EasyModbus;

await using var client = new ModbusClient("192.168.0.10", 502)
{
    ConnectionTimeout = 2000,
    UnitIdentifier = 1
};

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await client.ConnectAsync(timeout.Token);
int[] registers = await client.ReadHoldingRegistersAsync(0, 2, timeout.Token);
await client.WriteMultipleRegistersAsync(0, [100, 200], timeout.Token);
```

Every supported read/write method has an async counterpart accepting a
`CancellationToken`. Existing synchronous methods call the same protocol core.
Requests on a client are serialized, including mixed synchronous and async calls.
Configure the endpoint and transport while disconnected.

Set `UDPFlag = true` before connecting to use UDP. For RTU, create
`new ModbusClient("COM3")`, configure `Baudrate`, `Parity`, and `StopBits`, then
connect. `NumberOfRetries` controls RTU retries after a timeout or failed CRC.
TCP and UDP writes are not automatically replayed.

An interrupted in-flight request closes the transport so its delayed response
cannot be mistaken for a later request. Reconnect before making another request.
Canceling a request still queued behind another operation leaves that active
operation and connection intact. Dispose waits for the current operation to end;
cancel that operation's token first when prompt shutdown is needed.

`ReceivedData` and `SentData` expose the latest wire frames as read-only memory.
The legacy `receiveData`, `sendData`, and `calculateCRC` members remain as obsolete
source compatibility aliases. Data-change callbacks run after releasing the
client operation lock and must not throw exceptions. Callbacks can run on I/O
threads; UI applications should marshal updates to their UI thread.

This fork is intended for source integration and recompilation. Diagnostic
fields are now snapshot properties, and legacy register array views are
read-only properties exposing mutable arrays. Previously compiled assemblies
that accessed those fields need to be rebuilt against the current library.

## Server

```csharp
await using var server = new ModbusServer { Port = 502 };
server.holdingRegisters[1] = 123;
server.Listen();
// Serve requests until the application requests shutdown.
await server.StopListeningAsync();
```

`Listen()` binds before returning and reports startup errors directly to the
caller. `StopListening()` and `StopListeningAsync()` cancel pending I/O and close
accepted connections. A stopped server can be restarted. Configure its endpoint
and transport while stopped. `Port = 0` chooses an available port and updates
`Port` with the bound port, which is useful for local tests.

Wire addresses are **zero-based**. Server tables retain EasyModbus's **one-based**
local indexing: `holdingRegisters[1]` corresponds to wire address `0`.
The complete wire address range `0..65535` is supported. Register reads retain
signed 16-bit values; register writes accept signed or unsigned 16-bit values.
Use table indexers to synchronize local access with protocol requests. Legacy
`localArray` views expose the mutable backing arrays and bypass that locking.

Protocol logging retains the `ModbusProtocol` model. `ModbusLogData` returns a
snapshot of the latest 100 request/response records, with empty slots represented
by null. Optional `LogFileFilename` logging is process-wide and serialized.

## Function codes

| Code | Operation |
| --- | --- |
| 1 | Read coils |
| 2 | Read discrete inputs |
| 3 | Read holding registers |
| 4 | Read input registers |
| 5 | Write single coil |
| 6 | Write single register |
| 15 | Write multiple coils |
| 16 | Write multiple registers |
| 23 | Read/write multiple registers |

## Repository layout

`EasyModbus.slnx` is the current solution. The library lives in `EasyModbus`, tests
in `Tests/EasyModbus.Tests`, and the runnable example in `ConsoleAppStd`. The
library uses nullable reference types, C# 14 validated properties, cancellable
socket I/O, deterministic disposal, and explicit byte-order handling.

Historical upstream Windows Forms applications and the .NET 5 and .NET 6 project
copies are retained as reference material. They are outside
the current solution and are not supported build targets for this fork. Use the
current library project rather than the old projects that link individual source
files. Git retains the original upstream solution and source history.

When consumed as a Git submodule, repository-local `Directory.Build.props` and
`Directory.Build.targets` isolate the library from the host's product versioning
and deployment rules. Commit and push library changes before updating the host
repository's pinned submodule commit.

The original upstream copyright and MIT license are preserved in [LICENSE](LICENSE).
