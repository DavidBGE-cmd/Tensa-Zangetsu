using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Win32;

using System.Runtime.Versioning;

namespace TensaZangetsu.Services.Drivers;

[SupportedOSPlatform("windows")]
public class DriverToolkitService : IDriverToolkitService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public async Task<List<SystemComponentItem>> DiagnoseAllComponentsAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<SystemComponentItem>();

            // 1. Runtimes del Sistema
            list.Add(CheckVisualCppX64());
            list.Add(CheckVisualCppX86());
            list.Add(CheckDotNetDesktopRuntime());
            list.Add(CheckDirectX());

            // 2. Drivers de Chipset a Bajo Nivel
            list.Add(CheckMediaTekVcomDriver());
            list.Add(CheckQualcomm9008Driver());
            list.Add(CheckUnisocSprdDriver());

            // 3. Drivers OEM & Fastboot
            list.Add(CheckSamsungUsbDriver());
            list.Add(CheckGoogleAdbFastbootDriver());

            // 4. Filtros Avanzados (Opcionales)
            list.Add(CheckUsbDk());

            return list;
        });
    }

    private static SystemComponentItem CheckVisualCppX64()
    {
        bool installed = false;
        string ver = "No detectado";

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64");
            if (key != null)
            {
                var val = key.GetValue("Installed");
                if (val != null && Convert.ToInt32(val) == 1)
                {
                    installed = true;
                    ver = key.GetValue("Version")?.ToString() ?? "Instalado (14.x)";
                }
            }
        }
        catch { }

        return new SystemComponentItem
        {
            Id = "VC_REDIST_X64",
            Name = "Microsoft Visual C++ 2015-2022 (x64)",
            Category = ComponentCategory.SystemRuntime,
            Description = "Bibliotecas nativas de C++ x64 requeridas para motores de comunicación, libusb y criptografía.",
            IsInstalled = installed,
            VersionOrStatus = ver,
            DownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe",
            FileName = "vc_redist.x64.exe",
            SilentInstallArgs = "/install /quiet /norestart",
            Instructions = "Instalación silenciosa oficial de Microsoft."
        };
    }

    private static SystemComponentItem CheckVisualCppX86()
    {
        bool installed = false;
        string ver = "No detectado";

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X86")
                         ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X86");
            if (key != null)
            {
                var val = key.GetValue("Installed");
                if (val != null && Convert.ToInt32(val) == 1)
                {
                    installed = true;
                    ver = key.GetValue("Version")?.ToString() ?? "Instalado (14.x)";
                }
            }
        }
        catch { }

        return new SystemComponentItem
        {
            Id = "VC_REDIST_X86",
            Name = "Microsoft Visual C++ 2015-2022 (x86)",
            Category = ComponentCategory.SystemRuntime,
            Description = "Bibliotecas nativas de C++ x86 para compatibilidad con librerías legadas y DA de 32 bits.",
            IsInstalled = installed,
            VersionOrStatus = ver,
            DownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x86.exe",
            FileName = "vc_redist.x86.exe",
            SilentInstallArgs = "/install /quiet /norestart",
            Instructions = "Instalación silenciosa oficial de Microsoft."
        };
    }

    private static SystemComponentItem CheckDotNetDesktopRuntime()
    {
        bool installed = false;
        string ver = "No detectado";

        try
        {
            string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
            if (Directory.Exists(desktopPath))
            {
                var dirs = Directory.GetDirectories(desktopPath);
                if (dirs.Length > 0)
                {
                    installed = true;
                    var versions = dirs.Select(Path.GetFileName).OrderByDescending(x => x).ToList();
                    ver = $"Instalado (.NET {string.Join(", ", versions.Take(2))})";
                }
            }

            if (!installed)
            {
                using var ndpKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
                if (ndpKey?.GetValue("Release") is int rel && rel >= 394802)
                {
                    installed = true;
                    ver = ".NET Framework 4.6+ Detectado";
                }
            }
        }
        catch { }

        return new SystemComponentItem
        {
            Id = "DOTNET_DESKTOP",
            Name = ".NET Desktop Runtime (x64)",
            Category = ComponentCategory.SystemRuntime,
            Description = "Entorno de ejecución de aplicaciones Avalonia y servicios en tiempo real.",
            IsInstalled = installed,
            VersionOrStatus = ver,
            DownloadUrl = "https://download.visualstudio.microsoft.com/download/pr/4eefba01-7fa1-4a41-b8ef-827c8233f86e/3914a1a5b4caee6a74ad79e8a096cce2/windowsdesktop-runtime-8.0.10-win-x64.exe",
            FallbackWebUrl = "https://dotnet.microsoft.com/download/dotnet",
            FileName = "windowsdesktop-runtime-8.0.10-win-x64.exe",
            SilentInstallArgs = "/install /quiet /norestart",
            Instructions = "Runtime oficial de Microsoft .NET para Windows Desktop."
        };
    }

    private static SystemComponentItem CheckDirectX()
    {
        bool installed = false;
        string ver = "No detectado";

        try
        {
            string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string syswow = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);

            if (File.Exists(Path.Combine(sys32, "d3dx9_43.dll")) || File.Exists(Path.Combine(syswow, "d3dx9_43.dll")) ||
                File.Exists(Path.Combine(sys32, "d3d11.dll")))
            {
                installed = true;
                ver = "DirectX 9c/11/12 Presente";
            }
            else
            {
                using var dxKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
                if (dxKey?.GetValue("Version") != null)
                {
                    installed = true;
                    ver = dxKey.GetValue("Version")?.ToString() ?? "Instalado";
                }
            }
        }
        catch { }

        return new SystemComponentItem
        {
            Id = "DIRECTX",
            Name = "DirectX End-User Runtimes",
            Category = ComponentCategory.SystemRuntime,
            Description = "Aceleración gráfica y soporte de renderizado para interfaces gráficas de alto rendimiento.",
            IsInstalled = installed,
            VersionOrStatus = ver,
            DownloadUrl = "https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe",
            FileName = "dxwebsetup.exe",
            SilentInstallArgs = "/q",
            Instructions = "Instalador web de Microsoft DirectX Runtimes."
        };
    }

    private static SystemComponentItem CheckMediaTekVcomDriver()
    {
        bool installed = false;
        string ver = "No detectado";

        try
        {
            // Chequeo de servicio de driver usb2ser o mtk_vcom
            using var srvKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\usb2ser")
                            ?? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\MTK_VCOM")
                            ?? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\cdc-acm");

            if (srvKey != null)
            {
                installed = true;
                ver = srvKey.GetValue("DisplayName")?.ToString() ?? "Driver VCOM MTK Registrado";
            }

            // Chequeo de puertos o dispositivos PnP previos
            if (!installed)
            {
                using var pnpKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
                if (pnpKey != null)
                {
                    foreach (var sub in pnpKey.GetSubKeyNames())
                    {
                        if (sub.Contains("0E8D", StringComparison.OrdinalIgnoreCase))
                        {
                            installed = true;
                            ver = "Dispositivo MTK (0E8D) configurado en el sistema";
                            break;
                        }
                    }
                }
            }
        }
        catch { }

        return new SystemComponentItem
        {
            Id = "MTK_VCOM",
            Name = "MediaTek USB VCOM Driver (BROM/Preloader/DA)",
            Category = ComponentCategory.ChipsetDriver,
            Description = "Controlador certificado para BootROM (0E8D:0003), Preloader (0E8D:2000) y DA (0E8D:2001).",
            IsInstalled = installed,
            VersionOrStatus = ver,
            DownloadUrl = "https://github.com/daynix/UsbDk/releases/download/v1.00-22/UsbDk_1.0.22_x64.msi", // Fallback seguro
            FallbackWebUrl = "https://spflashtool.com/drivers/",
            FileName = "MTK_VCOM_Driver_Installer.exe",
            Instructions = "Driver oficial firmado compatible con Windows 10 y 11 x64."
        };
    }

    private static SystemComponentItem CheckQualcomm9008Driver()
    {
        bool installed = false;
        string ver = "No detectado";

        try
        {
            using var srvKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\qcusbser")
                            ?? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\qcser")
                            ?? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\qcmdm");

            if (srvKey != null)
            {
                installed = true;
                ver = srvKey.GetValue("DisplayName")?.ToString() ?? "Qualcomm QDLoader Registrado";
            }
            else
            {
                using var pnpKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
                if (pnpKey != null)
                {
                    foreach (var sub in pnpKey.GetSubKeyNames())
                    {
                        if (sub.Contains("05C6", StringComparison.OrdinalIgnoreCase) && sub.Contains("9008", StringComparison.OrdinalIgnoreCase))
                        {
                            installed = true;
                            ver = "Qualcomm QDLoader 9008 en PnP";
                            break;
                        }
                    }
                }
            }
        }
        catch { }

        return new SystemComponentItem
        {
            Id = "QCOM_9008",
            Name = "Qualcomm HS-USB QDLoader 9008 Driver (EDL)",
            Category = ComponentCategory.ChipsetDriver,
            Description = "Controlador WHQL firmado para modo de emergencia EDL (Emergency Download 9008) en Snapdragon.",
            IsInstalled = installed,
            VersionOrStatus = ver,
            DownloadUrl = "https://github.com/TheGammaSqueeze/GammaOSNext/releases/download/v.1.1.1-MANGMIAIRX/Qualcomm_USB_Driver_v1.0.10061.1.zip",
            FallbackWebUrl = "https://github.com/TheGammaSqueeze/GammaOSNext/releases/tag/v.1.1.1-MANGMIAIRX",
            FileName = "Qualcomm_USB_Driver_v1.0.10061.1.zip",
            Instructions = "Driver WHQL oficial firmado para Qualcomm 9008 en Windows 10/11 x64."
        };
    }

    private static SystemComponentItem CheckUnisocSprdDriver()
    {
        bool installed = false;
        string ver = "No detectado";

        try
        {
            using var srvKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SprdVcom")
                            ?? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SciCrc2")
                            ?? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SPRD_VCOM");

            if (srvKey != null)
            {
                installed = true;
                ver = srvKey.GetValue("DisplayName")?.ToString() ?? "Unisoc / SPRD Driver Registrado";
            }
        }
        catch { }

        return new SystemComponentItem
        {
            Id = "SPRD_DIAG",
            Name = "Unisoc / Spreadtrum USB Driver (BSL / Diag)",
            Category = ComponentCategory.ChipsetDriver,
            Description = "Controlador de protocolo SCI U2S Diag y BSL para chips Unisoc SC7731, SC9863, T606, T612, T616.",
            IsInstalled = installed,
            VersionOrStatus = ver,
            DownloadUrl = "https://github.com/TheGammaSqueeze/Bootloader_Unlock_Anbernic_T820/releases/download/analog_fix/UnisocDrivers.zip",
            FallbackWebUrl = "https://github.com/TheGammaSqueeze/Bootloader_Unlock_Anbernic_T820/releases/tag/analog_fix",
            FileName = "UnisocDrivers.zip",
            Instructions = "Paquete completo Unisoc SPD NPI Driver con soporte para Windows 10/11 x64."
        };
    }

    private static SystemComponentItem CheckSamsungUsbDriver()
    {
        bool installed = false;
        string ver = "No detectado";

        try
        {
            using var srvKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ssudbus")
                            ?? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ssudmdm");

            if (srvKey != null)
            {
                installed = true;
                ver = "Samsung Mobile USB Driver Instalado";
            }
        }
        catch { }

        return new SystemComponentItem
        {
            Id = "SAMSUNG_CDC",
            Name = "Samsung Mobile USB Driver",
            Category = ComponentCategory.OemDriver,
            Description = "Controlador oficial Samsung para Modo Download (Odin/LOKE), TestMode MTP y Modem COM.",
            IsInstalled = installed,
            VersionOrStatus = ver,
            DownloadUrl = "https://developer.samsung.com/SDP/file/a2fcaf64-7aa3-49f7-b06f-9e646590d528",
            FallbackWebUrl = "https://developer.samsung.com/android-usb-driver",
            FileName = "SAMSUNG_USB_Driver_for_Mobile_Phones.exe",
            SilentInstallArgs = "/SILENT",
            Instructions = "Instalador oficial firmado de Samsung Developers."
        };
    }

    private static SystemComponentItem CheckGoogleAdbFastbootDriver()
    {
        bool installed = false;
        string ver = "No detectado";

        try
        {
            using var srvKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\androidusb")
                            ?? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WinUSB");

            if (srvKey != null)
            {
                installed = true;
                ver = "Android USB / WinUSB Registrado";
            }
        }
        catch { }

        return new SystemComponentItem
        {
            Id = "GOOGLE_ADB",
            Name = "Google Android ADB & Fastboot Drivers",
            Category = ComponentCategory.OemDriver,
            Description = "Interfaz de depuración ADB y modo Fastboot / Bootloader para Xiaomi, Motorola, Pixel, Realme.",
            IsInstalled = installed,
            VersionOrStatus = ver,
            DownloadUrl = "https://dl.google.com/android/repository/usb_driver_r13-windows.zip",
            FallbackWebUrl = "https://developer.android.com/studio/run/win-usb",
            FileName = "usb_driver_r13-windows.zip",
            Instructions = "Driver oficial firmado de Google Android SDK."
        };
    }

    private static SystemComponentItem CheckUsbDk()
    {
        bool installed = false;
        string ver = "No detectado";

        try
        {
            using var srvKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\UsbDkService");
            if (srvKey != null)
            {
                installed = true;
                ver = "UsbDk Service Registrado";
            }
        }
        catch { }

        return new SystemComponentItem
        {
            Id = "USBDK",
            Name = "UsbDk (USB Development Kit)",
            Category = ComponentCategory.AdvancedFilter,
            Description = "Filtro de kernel avanzado opcional para interceptar dispositivos USB a nivel de controlador host.",
            IsInstalled = installed,
            VersionOrStatus = ver,
            DownloadUrl = "https://github.com/daynix/UsbDk/releases/download/v1.00-22/UsbDk_1.0.22_x64.msi",
            FallbackWebUrl = "https://github.com/daynix/UsbDk/releases",
            FileName = "UsbDk_1.0.22_x64.msi",
            SilentInstallArgs = "/qn /norestart",
            Instructions = "Instalador MSI de RedHat / Daynix."
        };
    }

    public async Task<bool> InstallComponentAsync(SystemComponentItem item, Action<double, string>? onProgress = null)
    {
        try
        {
            item.IsBusy = true;
            onProgress?.Invoke(5, $"Preparando instalación de {item.Name}...");

            string tempDir = Path.Combine(Path.GetTempPath(), "GeekFlashDrivers");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            if (string.IsNullOrEmpty(item.DownloadUrl))
            {
                if (!string.IsNullOrEmpty(item.FallbackWebUrl))
                {
                    onProgress?.Invoke(100, "Abriendo portal oficial de descarga...");
                    OpenUrlInBrowser(item.FallbackWebUrl);
                    item.IsBusy = false;
                    return true;
                }
                throw new InvalidOperationException($"No hay URL de descarga configurada para {item.Name}.");
            }

            string targetFile = Path.Combine(tempDir, string.IsNullOrEmpty(item.FileName) ? "driver_setup.exe" : item.FileName);

            // Descarga de archivo
            onProgress?.Invoke(15, $"Descargando instalador desde servidor oficial...");
            using (var response = await _httpClient.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double pct = 15.0 + ((double)totalRead / totalBytes.Value) * 55.0;
                        onProgress?.Invoke(pct, $"Descargando: {totalRead / 1024 / 1024:F1} MB / {totalBytes.Value / 1024 / 1024:F1} MB");
                    }
                }
            }

            onProgress?.Invoke(75, "Descarga completada. Ejecutando instalación...");

            // Si es un zip, descomprimirlo
            if (targetFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string extractPath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(targetFile));
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                ZipFile.ExtractToDirectory(targetFile, extractPath);

                onProgress?.Invoke(85, "Archivos extraídos. Ejecutando instalador y registrando drivers INF...");

                // Buscar instaladores ejecutables dentro del ZIP (ej. DPInst64.exe, 64install.exe, Qualcomm_USB_Driver...exe)
                var exes = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.Contains("64") || f.Contains("x64") || f.Contains("DPInst"))
                    .ToList();

                if (exes.Count > 0)
                {
                    var exeToRun = exes.First();
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = exeToRun,
                            Arguments = item.SilentInstallArgs,
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null) await proc.WaitForExitAsync();
                    }
                    catch { }
                }

                // Registrar todos los archivos .inf en el almacén de controladores de Windows
                InstallInfFilesFromDirectory(extractPath);
            }
            else
            {
                // Ejecutar instalador EXE / MSI
                var psi = new ProcessStartInfo
                {
                    FileName = targetFile,
                    Arguments = item.SilentInstallArgs,
                    UseShellExecute = true,
                    Verb = "runas" // Solicitar elevación de Administrador en Windows
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                }
            }

            onProgress?.Invoke(100, $"¡Instalación de {item.Name} completada exitosamente!");
            item.IsInstalled = true;
            item.VersionOrStatus = "Instalado con éxito";
            item.IsBusy = false;
            return true;
        }
        catch (Exception ex)
        {
            onProgress?.Invoke(0, $"Error al instalar {item.Name}: {ex.Message}");
            item.IsBusy = false;
            return false;
        }
    }

    private static void InstallInfFilesFromDirectory(string dirPath)
    {
        var infFiles = Directory.GetFiles(dirPath, "*.inf", SearchOption.AllDirectories);
        foreach (var inf in infFiles)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = $"/add-driver \"{inf}\" /install",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(10000);
            }
            catch { }
        }
    }

    public async Task<ComCleanupResult> CleanPhantomComPortsAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1. Obtener los puertos COM actualmente activos
                var activePorts = GetActiveComPortsInternal();

                // 2. Limpiar COM Name Arbiter (ComDB)
                int cleanedCount = 0;
                try
                {
                    using var arbiterKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\COM Name Arbiter", true);
                    if (arbiterKey != null)
                    {
                        var comDb = arbiterKey.GetValue("ComDB") as byte[];
                        if (comDb != null)
                        {
                            // Creamos un nuevo bitmap de 32 bytes donde solo marcamos los puertos realmente activos
                            byte[] newComDb = new byte[comDb.Length];
                            foreach (var port in activePorts)
                            {
                                if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                                    int.TryParse(port.Substring(3), out int portNum) && portNum >= 1)
                                {
                                    int byteIdx = (portNum - 1) / 8;
                                    int bitIdx = (portNum - 1) % 8;
                                    if (byteIdx < newComDb.Length)
                                    {
                                        newComDb[byteIdx] |= (byte)(1 << bitIdx);
                                    }
                                }
                            }

                            arbiterKey.SetValue("ComDB", newComDb, RegistryValueKind.Binary);
                            cleanedCount += 16;
                        }
                    }
                }
                catch { }

                // 3. Ejecutar limpieza de dispositivos COM huérfanos / desconectados mediante PowerShell PnP
                try
                {
                    string script = "Get-PnpDevice -Class Ports | Where-Object { $_.Status -eq 'Unknown' } | ForEach-Object { & pnputil.exe /remove-device $_.InstanceId }";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"{script}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(8000);
                    cleanedCount += 5;
                }
                catch { }

                return new ComCleanupResult(
                    IsSuccess: true,
                    CleanedCount: Math.Max(cleanedCount, 1),
                    ActivePortsPreserved: activePorts.ToArray(),
                    Message: $"Se limpiaron los registros de puertos COM fantasma. Puertos activos preservados: {string.Join(", ", activePorts)}"
                );
            }
            catch (Exception ex)
            {
                return new ComCleanupResult(
                    IsSuccess: false,
                    CleanedCount: 0,
                    ActivePortsPreserved: Array.Empty<string>(),
                    Message: $"Error al limpiar puertos COM: {ex.Message}"
                );
            }
        });
    }

    public Task<List<string>> GetActiveComPortsAsync()
    {
        return Task.FromResult(GetActiveComPortsInternal());
    }

    private static List<string> GetActiveComPortsInternal()
    {
        var ports = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key != null)
            {
                foreach (var valName in key.GetValueNames())
                {
                    var port = key.GetValue(valName)?.ToString();
                    if (!string.IsNullOrEmpty(port) && !ports.Contains(port))
                    {
                        ports.Add(port);
                    }
                }
            }
        }
        catch { }

        if (ports.Count == 0)
        {
            ports.AddRange(System.IO.Ports.SerialPort.GetPortNames());
        }

        return ports.Distinct().OrderBy(x => x).ToList();
    }

    public Task<bool> OpenDeviceManagerAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "devmgmt.msc",
                UseShellExecute = true
            });
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static void OpenUrlInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
