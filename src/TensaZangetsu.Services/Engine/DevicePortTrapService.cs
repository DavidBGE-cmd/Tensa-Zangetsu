using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using TensaZangetsu.UsbWatcher;
using TensaZangetsu.UsbWatcher.Abstractions;
using TensaZangetsu.UsbWatcher.Extensions;

namespace TensaZangetsu.Services.Engine;

/// <summary>
/// Información de un puerto USB / COM atrapado durante la escucha activa.
/// </summary>
public sealed record TrappedDevicePort(
    string PortName,
    string Platform, // "MTK", "QCOM", "SPRD"
    string Description,
    int? VendorId = null,
    int? ProductId = null,
    bool IsBromOrBootstrap = true
);

/// <summary>
/// Motor de escucha activa y atrapamiento automático de puertos USB/COM en caliente
/// para dispositivos MediaTek (BROM / Preloader), Qualcomm (EDL 9008) y UNISOC (BootROM / Diag).
/// </summary>
public sealed class DevicePortTrapService
{
    private static readonly Regex VidPidRegex = new(@"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.Compiled);

    /// <summary>
    /// Escanea los puertos actualmente conectados e identifica si alguno corresponde a un dispositivo en modo servicio.
    /// </summary>
    public List<TrappedDevicePort> DetectConnectedServicingPorts()
    {
        var result = new List<TrappedDevicePort>();
        if (!OperatingSystem.IsWindows()) return result;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%' OR DeviceID LIKE '%USB%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? string.Empty;
                string pnp = obj["PNPDeviceID"]?.ToString() ?? (obj["DeviceID"]?.ToString() ?? string.Empty);
                string? port = ExtractComPort(name);

                var (vid, pid) = ExtractVidPid(pnp);

                // 1. MediaTek BROM / Preloader
                if (vid == 0x0E8D ||
                    name.Contains("MediaTek", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("PreLoader", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("MTK USB Port", StringComparison.OrdinalIgnoreCase))
                {
                    string actualPort = port ?? "USB_NATIVE";
                    bool isBrom = pid == 0x0003 || name.Contains("USB Port", StringComparison.OrdinalIgnoreCase);
                    result.Add(new TrappedDevicePort(actualPort, "MTK", name, vid ?? 0x0E8D, pid ?? 0x0003, isBrom));
                    continue;
                }

                // 2. Qualcomm HS-USB QDLoader 9008 / EDL
                if ((vid == 0x05C6 && pid == 0x9008) ||
                    name.Contains("9008", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("QDLoader", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Qualcomm HS-USB", StringComparison.OrdinalIgnoreCase))
                {
                    string actualPort = port ?? "COM9008";
                    result.Add(new TrappedDevicePort(actualPort, "QCOM", name, vid ?? 0x05C6, pid ?? 0x9008, true));
                    continue;
                }

                // 3. UNISOC / Spreadtrum BootROM / Diag
                if (vid == 0x1782 ||
                    name.Contains("SPRD", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Spreadtrum", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("UNISOC", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("SciCrcPort", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("U2S Diag", StringComparison.OrdinalIgnoreCase))
                {
                    string actualPort = port ?? "COM_SPRD";
                    result.Add(new TrappedDevicePort(actualPort, "SPRD", name, vid ?? 0x1782, pid ?? 0x4D00, true));
                    continue;
                }
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Entra en estado de escucha activa ("Waiting for device...") hasta atrapar un puerto
    /// o hasta que la operación sea cancelada mediante el CancellationToken.
    /// </summary>
    public async Task<TrappedDevicePort> WaitForDevicePortAsync(
        string? preferredPlatform,
        CancellationToken ct,
        Action<string, string>? logCallback = null)
    {
        // 1. Snapshot inicial de puertos serie ya existentes para no atrapar puertos fijos o del sistema (ej. COM1 interno)
        var baselineComPorts = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);

        logCallback?.Invoke("Waiting for device...", "#FFD600");
        logCallback?.Invoke("▶ [Escucha Activa] Monitoreando bus USB para atrapar BROM / Preloader / EDL 9008 / UNISOC Diag...", "#E0FFE8");
        logCallback?.Invoke("💡 Conecta el teléfono apagado manteniendo presionadas las teclas de volumen (o TestPoint en placa)...", "#80FF9E");

        // 2. Verificar si ya hay un puerto de servicio válido activo en este momento
        var existing = DetectConnectedServicingPorts();
        if (existing.Count > 0)
        {
            var match = FindBestMatch(existing, preferredPlatform);
            if (match != null)
            {
                logCallback?.Invoke($"✔ Puerto de servicio detectado inmediatamente: {match.Description} ({match.PortName}) [{match.Platform}]", "#00FF66");
                return match;
            }
        }

        // 3. Suscribirse a eventos de conexión de hardware mediante IUsbDeviceMonitor
        IUsbDeviceMonitor? monitor = null;
        TrappedDevicePort? eventTrapped = null;
        var eventLock = new object();

        try
        {
            monitor = UsbDeviceMonitorFactory.Create();
            monitor.DeviceAdded += (s, e) =>
            {
                var dev = e.Device;
                string? pName = dev.ExtractPortName();
                int vid = dev.VendorId ?? 0;
                int pid = dev.ProductId ?? 0;
                string fname = dev.FriendlyName ?? string.Empty;

                if (vid == 0x0E8D || fname.Contains("MediaTek", StringComparison.OrdinalIgnoreCase) || fname.Contains("PreLoader", StringComparison.OrdinalIgnoreCase))
                {
                    lock (eventLock)
                    {
                        eventTrapped ??= new TrappedDevicePort(pName ?? "MTK_PORT", "MTK", fname, vid, pid, true);
                    }
                }
                else if ((vid == 0x05C6 && pid == 0x9008) || fname.Contains("9008", StringComparison.OrdinalIgnoreCase) || fname.Contains("QDLoader", StringComparison.OrdinalIgnoreCase))
                {
                    lock (eventLock)
                    {
                        eventTrapped ??= new TrappedDevicePort(pName ?? "QCOM_PORT", "QCOM", fname, vid, pid, true);
                    }
                }
                else if (vid == 0x1782 || fname.Contains("SPRD", StringComparison.OrdinalIgnoreCase) || fname.Contains("UNISOC", StringComparison.OrdinalIgnoreCase))
                {
                    lock (eventLock)
                    {
                        eventTrapped ??= new TrappedDevicePort(pName ?? "SPRD_PORT", "SPRD", fname, vid, pid, true);
                    }
                }
            };
            monitor.StartMonitoring();
        }
        catch { }

        // 4. Bucle reactivo de alta frecuencia (polling cada 80ms) combinado con los eventos de monitor
        int pollCount = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                pollCount++;

                // Verificar si el monitor por eventos capturó un dispositivo
                lock (eventLock)
                {
                    if (eventTrapped != null)
                    {
                        logCallback?.Invoke($"✔ Dispositivo atrapado por evento USB: {eventTrapped.Description} ({eventTrapped.PortName})", "#00FF66");
                        return eventTrapped;
                    }
                }

                // Polling de PnP Entities y SerialPort.GetPortNames
                var currentServicing = DetectConnectedServicingPorts();
                if (currentServicing.Count > 0)
                {
                    // Si apareció un nuevo puerto que coincide
                    var newlyAppeared = currentServicing.FirstOrDefault(p =>
                        !baselineComPorts.Contains(p.PortName) ||
                        p.VendorId is 0x0E8D or 0x05C6 or 0x1782);

                    if (newlyAppeared != null)
                    {
                        // Esperar 120ms para estabilización del descriptor USB en el kernel
                        await Task.Delay(120, ct);
                        logCallback?.Invoke($"✔ Puerto atrapado en bus USB: {newlyAppeared.Description} [{newlyAppeared.PortName}] ({newlyAppeared.Platform})", "#00FF66");
                        return newlyAppeared;
                    }
                }

                // Verificación de nuevos puertos serie genéricos (ej. PreLoader VCOM sin WMI name)
                var currentSerialPorts = SerialPort.GetPortNames();
                foreach (var p in currentSerialPorts)
                {
                    if (!baselineComPorts.Contains(p))
                    {
                        await Task.Delay(100, ct);
                        string plat = preferredPlatform?.ToUpperInvariant() switch
                        {
                            "QUALCOMM" => "QCOM",
                            "UNISOC" => "SPRD",
                            _ => "MTK"
                        };
                        var trappedSerial = new TrappedDevicePort(p, plat, $"Nuevo Puerto Serial Detectado ({p})", null, null, true);
                        logCallback?.Invoke($"✔ Nuevo puerto COM atrapado: {p} (Plataforma asignada: {plat})", "#00FF66");
                        return trappedSerial;
                    }
                }

                // Heartbeat informativo cada 15 segundos
                if (pollCount % 180 == 0)
                {
                    logCallback?.Invoke("⏳ Esperando conexión... Verifica el cable USB Tipo C y las teclas de volumen.", "#4D7A58");
                }

                await Task.Delay(80, ct);
            }
        }
        finally
        {
            try
            {
                monitor?.StopMonitoring();
                (monitor as IDisposable)?.Dispose();
            }
            catch { }
        }

        ct.ThrowIfCancellationRequested();
        throw new OperationCanceledException(ct);
    }

    private static TrappedDevicePort? FindBestMatch(List<TrappedDevicePort> ports, string? preferredPlatform)
    {
        if (string.IsNullOrWhiteSpace(preferredPlatform) || preferredPlatform.Equals("Todas", StringComparison.OrdinalIgnoreCase))
            return ports.FirstOrDefault();

        string norm = preferredPlatform.ToUpperInvariant();
        if (norm.Contains("MEDIA") || norm.Contains("MTK"))
            return ports.FirstOrDefault(p => p.Platform == "MTK") ?? ports.FirstOrDefault();
        if (norm.Contains("QUAL") || norm.Contains("QCOM"))
            return ports.FirstOrDefault(p => p.Platform == "QCOM") ?? ports.FirstOrDefault();
        if (norm.Contains("UNI") || norm.Contains("SPRD"))
            return ports.FirstOrDefault(p => p.Platform == "SPRD") ?? ports.FirstOrDefault();

        return ports.FirstOrDefault();
    }

    private static string? ExtractComPort(string text)
    {
        int start = text.IndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        int end = text.IndexOf(')', start);
        return end > start ? text.Substring(start + 1, end - start - 1) : null;
    }

    private static (int? Vid, int? Pid) ExtractVidPid(string pnp)
    {
        var m = VidPidRegex.Match(pnp);
        if (m.Success &&
            int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out int vid) &&
            int.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out int pid))
        {
            return (vid, pid);
        }
        return (null, null);
    }
}
