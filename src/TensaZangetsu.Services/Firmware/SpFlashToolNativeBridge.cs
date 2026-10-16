using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TensaZangetsu.Services.Firmware;

/// <summary>
/// Wrapper e interfaz de automatización para SP Flash Tool V5 y V6 (flash.dll, SLA_Challenge.dll, imageChecker.dll)
/// y orquestación en modo consola mediante plantillas XML (console_mode.xsd).
/// </summary>
public sealed class SpFlashToolNativeBridge
{
    private readonly string _spToolDirectory;

    public SpFlashToolNativeBridge(string? customSpToolDir = null)
    {
        _spToolDirectory = customSpToolDir ?? ResolveSpFlashToolDirectory();
    }

    public string SpToolDirectory => _spToolDirectory;

    private static string ResolveSpFlashToolDirectory()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = [
            Path.Combine(baseDir, "ExternalAssets", "SP_Flash_Tool_v6_v5", "SP_Flash_Tool_v6.2316_Win", "SP_Flash_Tool_v6.2316_Win"),
            Path.Combine(baseDir, "mt-katool-FutureAssets", "DA FILES MTK 2026 v22", "SP_Flash_Tool_v6_v5", "SP_Flash_Tool_v6.2316_Win", "SP_Flash_Tool_v6.2316_Win"),
            Path.Combine(baseDir, "..", "..", "..", "mt-katool-FutureAssets", "DA FILES MTK 2026 v22", "SP_Flash_Tool_v6_v5", "SP_Flash_Tool_v6.2316_Win", "SP_Flash_Tool_v6.2316_Win"),
            Path.Combine(baseDir, "..", "..", "..", "..", "mt-katool-FutureAssets", "DA FILES MTK 2026 v22", "SP_Flash_Tool_v6_v5", "SP_Flash_Tool_v6.2316_Win", "SP_Flash_Tool_v6.2316_Win")
        ];

        foreach (var c in candidates)
        {
            try
            {
                string full = Path.GetFullPath(c);
                if (Directory.Exists(full) && File.Exists(Path.Combine(full, "flash.dll")))
                    return full;
            }
            catch { }
        }

        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "mt-katool-FutureAssets", "DA FILES MTK 2026 v22", "SP_Flash_Tool_v6_v5", "SP_Flash_Tool_v6.2316_Win", "SP_Flash_Tool_v6.2316_Win");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "flash.dll")))
                return candidate;

            current = current.Parent;
        }

        return baseDir;
    }

    public string? GetExecutablePath()
    {
        string exe = Path.Combine(_spToolDirectory, "SPFlashToolV6.exe");
        if (File.Exists(exe)) return exe;

        string exe5 = Path.Combine(_spToolDirectory, "..", "SP_Flash_Tool_V5.2404_Win", "flash_tool.exe");
        if (File.Exists(exe5)) return Path.GetFullPath(exe5);

        return null;
    }

    public string? GetAuthFile(int svVersion = 6)
    {
        string subFolder = svVersion == 5 ? "auth_sv5" : "auth_sv6";
        string fileName = svVersion == 5 ? "auth_sv5.auth" : "auth_sv6.auth";

        string p1 = Path.Combine(_spToolDirectory, "..", "..", subFolder, fileName);
        if (File.Exists(p1)) return Path.GetFullPath(p1);

        string p2 = Path.Combine(_spToolDirectory, "..", subFolder, fileName);
        if (File.Exists(p2)) return Path.GetFullPath(p2);

        return null;
    }

    /// <summary>
    /// Genera un archivo XML de flasheo en modo consola conforme al esquema console_mode.xsd.
    /// </summary>
    public string GenerateConsoleModeXml(
        string scatterFilePath,
        string daFilePath,
        string? authFilePath = null,
        string? certFilePath = null,
        string connectionType = "USB",
        string comPort = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<flashtool-config version=\"2.0\">");
        sb.AppendLine("  <general>");
        sb.AppendLine($"    <connection type=\"{connectionType}\" com-port=\"{comPort}\" high-speed=\"true\" />");
        sb.AppendLine($"    <download-agent path=\"{daFilePath}\" />");
        sb.AppendLine($"    <scatter path=\"{scatterFilePath}\" />");
        if (!string.IsNullOrEmpty(authFilePath))
            sb.AppendLine($"    <auth-file path=\"{authFilePath}\" />");
        if (!string.IsNullOrEmpty(certFilePath))
            sb.AppendLine($"    <cert-file path=\"{certFilePath}\" />");
        sb.AppendLine("  </general>");
        sb.AppendLine("  <commands>");
        sb.AppendLine("    <command type=\"download-only\" />");
        sb.AppendLine("  </commands>");
        sb.AppendLine("</flashtool-config>");

        return sb.ToString();
    }

    /// <summary>
    /// Ejecuta SP Flash Tool V6 en segundo plano usando un script XML de consola.
    /// </summary>
    public async Task<int> ExecuteConsoleModeAsync(
        string consoleXmlPath,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        string? exe = GetExecutablePath();
        if (exe == null)
        {
            onLog?.Invoke("❌ [SP Flash Tool] No se encontró el ejecutable SPFlashToolV6.exe ni flash_tool.exe.");
            return -1;
        }

        onLog?.Invoke($"▶ [SP Flash Tool] Ejecutando: {Path.GetFileName(exe)} -c \"{consoleXmlPath}\"");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"-c \"{consoleXmlPath}\"",
            WorkingDirectory = _spToolDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) onLog?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) onLog?.Invoke($"[ERR] {e.Data}");
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            onLog?.Invoke("🛑 [SP Flash Tool] Operación cancelada por el usuario.");
            return -99;
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"❌ [SP Flash Tool] Error: {ex.Message}");
            return -2;
        }
    }

    #region P/Invoke Signatures (flash.dll & SLA_Challenge.dll)

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    public bool VerifyNativeLibrariesAvailable(out string statusMessage)
    {
        string flashDll = Path.Combine(_spToolDirectory, "flash.dll");
        string slaDll = Path.Combine(_spToolDirectory, "SLA_Challenge.dll");
        string imgChecker = Path.Combine(_spToolDirectory, "imageChecker.dll");

        bool hasFlash = File.Exists(flashDll);
        bool hasSla = File.Exists(slaDll);
        bool hasImg = File.Exists(imgChecker);

        if (hasFlash && hasSla && hasImg)
        {
            statusMessage = $"Librerías nativas verificadas en: {_spToolDirectory}";
            return true;
        }

        statusMessage = $"Faltan librerías: flash.dll={hasFlash}, SLA_Challenge.dll={hasSla}, imageChecker.dll={hasImg}";
        return false;
    }

    #endregion
}
