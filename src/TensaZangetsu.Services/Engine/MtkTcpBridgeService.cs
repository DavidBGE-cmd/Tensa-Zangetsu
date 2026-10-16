using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TensaZangetsu.Protocol.Mtk.Abstractions;

namespace TensaZangetsu.Services.Engine;

/// <summary>
/// Modelo de petición JSON para el puente TCP/IP MTK.
/// </summary>
public sealed record MtkTcpRequest(
    string Action,
    uint Address,
    int Length = 4,
    bool Registers = true,
    string? DataHex = null,
    uint? Value = null
);

/// <summary>
/// Modelo de respuesta JSON para el puente TCP/IP MTK.
/// </summary>
public sealed record MtkTcpResponse(
    string Status,
    uint Address,
    uint TranslatedAddress,
    bool Registers,
    int Length,
    string Data,
    uint? Value = null,
    string? Error = null
);

/// <summary>
/// Cliente de sockets TCP/IP asíncrono para interactuar con servidores de registros MTK
/// o scripts auxiliares (ej. mtk_tcpip_server.py o MtkTcpBridgeServer local).
/// </summary>
public sealed class MtkTcpBridgeClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MtkTcpBridgeClient(string host = "127.0.0.1", int port = 31337)
    {
        _host = host;
        _port = port;
    }

    public async Task<uint> Read32Async(uint address, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await ReadAsync(address, 4, registers: true, cancellationToken);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public async Task<uint[]> Read32ArrayAsync(uint address, int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        byte[] bytes = await ReadAsync(address, count * 4, registers: true, cancellationToken);
        var result = new uint[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4, 4));
        }
        return result;
    }

    public Task Write32Async(uint address, uint value, CancellationToken cancellationToken = default)
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        return WriteAsync(address, data, registers: true, cancellationToken);
    }

    public async Task<byte[]> ReadAsync(uint address, int length, bool registers = true, CancellationToken cancellationToken = default)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));

        var req = new JsonObject
        {
            ["action"] = "read",
            ["address"] = address,
            ["length"] = length,
            ["registers"] = registers
        };

        var resp = await SendRequestAsync(req, cancellationToken);
        string? dataHex = resp["data"]?.GetValue<string>();
        if (string.IsNullOrEmpty(dataHex))
            throw new InvalidOperationException("Respuesta vacía o sin datos desde el puente TCP.");

        return Convert.FromHexString(dataHex);
    }

    public async Task WriteAsync(uint address, ReadOnlyMemory<byte> data, bool registers = true, CancellationToken cancellationToken = default)
    {
        var req = new JsonObject
        {
            ["action"] = "write",
            ["address"] = address,
            ["data"] = Convert.ToHexString(data.Span),
            ["registers"] = registers
        };

        await SendRequestAsync(req, cancellationToken);
    }

    private async Task<JsonObject> SendRequestAsync(JsonObject request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, cancellationToken);

            using var stream = client.GetStream();
            string jsonLine = request.ToJsonString() + "\n";
            byte[] txBytes = Encoding.UTF8.GetBytes(jsonLine);
            await stream.WriteAsync(txBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
                throw new IOException("El servidor cerró la conexión sin responder.");

            var doc = JsonNode.Parse(line) as JsonObject
                ?? throw new JsonException("La respuesta del servidor no es un objeto JSON válido.");

            string? status = doc["status"]?.GetValue<string>();
            if (status != "ok")
            {
                string error = doc["error"]?.GetValue<string>() ?? "Error desconocido en el puente TCP.";
                throw new InvalidOperationException($"Error desde puente TCP: {error}");
            }

            return doc;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}

/// <summary>
/// Servidor de puente TCP/IP en C# que expone el acceso a registros y memoria
/// compatible con scripts de diagnóstico Python y utilidades de terceros.
/// </summary>
public sealed class MtkTcpBridgeServer : IDisposable
{
    public const uint CanonicalClockBase = 0x14000000;
    public const uint CanonicalSsrBase = 0x14003000;
    public const uint CanonicalCccBase = 0x14005000;
    public const uint MmioWindowSize = 0x1000;

    private readonly IMtkHardwareAccess? _hardwareAccess;
    private readonly string _host;
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public uint? ChipClockBase { get; set; }
    public uint? ChipSsrBase { get; set; }

    public bool IsRunning => _listener != null && (_cts != null && !_cts.IsCancellationRequested);

    public MtkTcpBridgeServer(IMtkHardwareAccess? hardwareAccess = null, string host = "127.0.0.1", int port = 31337)
    {
        _hardwareAccess = hardwareAccess;
        _host = host;
        _port = port;
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Parse(_host), _port);
        _listener.Start();

        _serverTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
        {
            while (!ct.IsCancellationRequested && client.Connected)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(ct);
                }
                catch
                {
                    break;
                }

                if (string.IsNullOrEmpty(line)) break;

                string responseJson;
                try
                {
                    var req = JsonNode.Parse(line) as JsonObject
                        ?? throw new JsonException("La trama recibida no es un objeto JSON.");

                    responseJson = ProcessCommand(req);
                }
                catch (Exception ex)
                {
                    var errObj = new JsonObject
                    {
                        ["status"] = "error",
                        ["error"] = ex.Message
                    };
                    responseJson = errObj.ToJsonString();
                }

                await writer.WriteLineAsync(responseJson.AsMemory(), ct);
            }
        }
    }

    private string ProcessCommand(JsonObject req)
    {
        string action = req["action"]?.GetValue<string>() ?? "read";
        uint address = ParseUInt(req["address"], "address");
        bool registers = req["registers"]?.GetValue<bool>() ?? true;

        uint translated = TranslateAddress(address);

        if (action.Equals("read", StringComparison.OrdinalIgnoreCase))
        {
            int length = req["length"]?.GetValue<int>() ?? 4;
            if (length <= 0) throw new ArgumentOutOfRangeException("length", "Debe ser > 0.");

            byte[] data = new byte[length];
            if (_hardwareAccess != null)
            {
                Span<byte> temp = stackalloc byte[4];
                for (int i = 0; i < length; i += 4)
                {
                    uint val = _hardwareAccess.Read32(translated + (uint)i);
                    int writeLen = Math.Min(4, length - i);
                    BinaryPrimitives.WriteUInt32LittleEndian(temp, val);
                    temp[..writeLen].CopyTo(data.AsSpan(i, writeLen));
                }
            }

            var resp = new JsonObject
            {
                ["status"] = "ok",
                ["address"] = address,
                ["translated_address"] = translated,
                ["registers"] = registers,
                ["length"] = length,
                ["data"] = Convert.ToHexString(data)
            };

            if (length == 4)
            {
                resp["value"] = BinaryPrimitives.ReadUInt32LittleEndian(data);
            }

            return resp.ToJsonString();
        }
        else if (action.Equals("write", StringComparison.OrdinalIgnoreCase))
        {
            byte[] writeBytes = ExtractWriteBytes(req);

            if (_hardwareAccess != null)
            {
                Span<byte> chunk = stackalloc byte[4];
                for (int i = 0; i < writeBytes.Length; i += 4)
                {
                    chunk.Clear();
                    int rem = Math.Min(4, writeBytes.Length - i);
                    writeBytes.AsSpan(i, rem).CopyTo(chunk);
                    uint val = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
                    _hardwareAccess.Write32(translated + (uint)i, val);
                }
            }

            var resp = new JsonObject
            {
                ["status"] = "ok",
                ["address"] = address,
                ["translated_address"] = translated,
                ["registers"] = registers,
                ["length"] = writeBytes.Length,
                ["data"] = Convert.ToHexString(writeBytes)
            };

            return resp.ToJsonString();
        }

        throw new NotSupportedException($"Acción no soportada: {action}");
    }

    private static byte[] ExtractWriteBytes(JsonObject req)
    {
        if (req.ContainsKey("data"))
        {
            string hex = req["data"]!.GetValue<string>().Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex[2..];
            if (hex.Length % 2 != 0) hex = "0" + hex;
            return Convert.FromHexString(hex);
        }

        if (req.ContainsKey("value"))
        {
            uint val = ParseUInt(req["value"], "value");
            int len = req["length"]?.GetValue<int>() ?? 4;
            byte[] b = new byte[len];
            BinaryPrimitives.WriteUInt32LittleEndian(b, val);
            return b;
        }

        throw new ArgumentException("La petición requiere 'data' en hex o 'value'.");
    }

    private static uint ParseUInt(JsonNode? node, string name)
    {
        if (node == null) throw new ArgumentNullException(name);
        if (node is JsonValue jv)
        {
            if (jv.TryGetValue<uint>(out uint u)) return u;
            if (jv.TryGetValue<int>(out int i)) return (uint)i;
            if (jv.TryGetValue<string>(out string? s) && !string.IsNullOrWhiteSpace(s))
            {
                s = s.Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToUInt32(s[2..], 16);
                return uint.Parse(s);
            }
        }
        throw new FormatException($"No se pudo parsear el entero '{name}'.");
    }

    private uint TranslateAddress(uint address)
    {
        if (ChipClockBase.HasValue && address >= CanonicalClockBase && address < CanonicalClockBase + MmioWindowSize)
        {
            return ChipClockBase.Value + (address - CanonicalClockBase);
        }

        if (ChipSsrBase.HasValue && address >= CanonicalSsrBase && address < CanonicalSsrBase + MmioWindowSize)
        {
            return ChipSsrBase.Value + 0x3000 + (address - CanonicalSsrBase);
        }

        if (ChipSsrBase.HasValue && address >= CanonicalCccBase && address < CanonicalCccBase + MmioWindowSize)
        {
            return ChipSsrBase.Value + 0x5000 + (address - CanonicalCccBase);
        }

        return address;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
