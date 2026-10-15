using System.Diagnostics;
using System.Text;

namespace TensaZangetsu.Services.Operations;

/// <summary>
/// Servicio para la orquestación y ejecución de scripts Python de bypass, exploit y utilidades
/// (bypass_utility, mtkclient, lkpatcher, kamakiri, amonet) con redirección de salida en tiempo real.
/// </summary>
public sealed class PythonScriptRunnerService
{
    private readonly string _scriptsBaseDir;

    public PythonScriptRunnerService(string? customScriptsDir = null)
    {
        _scriptsBaseDir = customScriptsDir ?? ResolveScriptsDirectory();
    }

    public string ScriptsBaseDirectory => _scriptsBaseDir;

    private static string ResolveScriptsDirectory()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        string[] candidates = [
            Path.Combine(baseDir, "Assets", "Scripts", "Python"),
            Path.Combine(baseDir, "ExternalAssets", "Scripts", "Python"),
            Path.Combine(baseDir, "Assets", "Scripts"),
            Path.Combine(baseDir, "ExternalAssets", "Scripts"),
            Path.Combine(baseDir, "mt-katool-FutureAssets")
        ];

        foreach (var c in candidates)
        {
            if (Directory.Exists(c)) return c;
        }

        // Buscar en la raíz del repositorio
        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            string ext = Path.Combine(current.FullName, "ExternalAssets", "Scripts", "Python");
            if (Directory.Exists(ext)) return ext;

            string assets = Path.Combine(current.FullName, "src", "TensaZangetsu.App", "Assets", "Scripts", "Python");
            if (Directory.Exists(assets)) return assets;

            string future = Path.Combine(current.FullName, "mt-katool-FutureAssets");
            if (Directory.Exists(future)) return future;

            current = current.Parent;
        }

        return Path.Combine(baseDir, "Assets", "Scripts", "Python");
    }

    /// <summary>
    /// Localiza el ejecutable de Python en el sistema operativo.
    /// </summary>
    public static string? FindPythonExecutable()
    {
        // 1. Probar comando 'python'
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            proc.WaitForExit(1500);
            if (proc.ExitCode == 0) return "python";
        }
        catch { }

        // 2. Probar 'python3'
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            proc.WaitForExit(1500);
            if (proc.ExitCode == 0) return "python3";
        }
        catch { }

        // 3. Probar 'py' (Windows Python Launcher)
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "py",
                    Arguments = "-3 --version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            proc.WaitForExit(1500);
            if (proc.ExitCode == 0) return "py";
        }
        catch { }

        // 4. Buscar en rutas estándar en Windows
        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pyDir = Path.Combine(localAppData, "Programs", "Python");
            if (Directory.Exists(pyDir))
            {
                var exes = Directory.GetFiles(pyDir, "python.exe", SearchOption.AllDirectories);
                if (exes.Length > 0) return exes[0];
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfPy = Directory.GetDirectories(programFiles, "Python*");
            foreach (var d in pfPy)
            {
                string exe = Path.Combine(d, "python.exe");
                if (File.Exists(exe)) return exe;
            }
        }

        return null;
    }

    /// <summary>
    /// Ejecuta un script Python asíncronamente con streaming de eventos a la consola.
    /// </summary>
    public async Task<int> RunScriptAsync(
        string scriptRelativeOrFullPath,
        string arguments,
        Action<string>? onLog,
        CancellationToken cancellationToken = default)
    {
        string? pythonExe = FindPythonExecutable();
        if (string.IsNullOrEmpty(pythonExe))
        {
            onLog?.Invoke("⚠️ [Python] No se detectó un intérprete de Python instalado en el sistema.");
            onLog?.Invoke("ℹ️ Puedes instalarlo con: winget install Python.Python.3.11");
            onLog?.Invoke("⚡ Recuerda que TensaZangetsu cuenta con motor nativo C# para BROM, SLA/DAA, DA y GPT sin necesidad de Python.");
            return -1;
        }

        string fullScriptPath = File.Exists(scriptRelativeOrFullPath)
            ? scriptRelativeOrFullPath
            : Path.Combine(_scriptsBaseDir, scriptRelativeOrFullPath);

        if (!File.Exists(fullScriptPath))
        {
            // Intentar buscar recursivamente por nombre de archivo
            string fileName = Path.GetFileName(scriptRelativeOrFullPath);
            var found = Directory.Exists(_scriptsBaseDir)
                ? Directory.GetFiles(_scriptsBaseDir, fileName, SearchOption.AllDirectories).FirstOrDefault()
                : null;

            if (found != null)
            {
                fullScriptPath = found;
            }
            else
            {
                onLog?.Invoke($"❌ [Python] No se encontró el script: {scriptRelativeOrFullPath}");
                return -2;
            }
        }

        string workingDir = Path.GetDirectoryName(fullScriptPath) ?? _scriptsBaseDir;

        onLog?.Invoke($"▶ [Python] Ejecutando: {Path.GetFileName(fullScriptPath)} {arguments}");

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"\"{fullScriptPath}\" {arguments}".Trim(),
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Asegurar que los scripts de Python puedan resolver módulos locales sin ModuleNotFoundError
        string pythonPath = $"{workingDir};{_scriptsBaseDir};{Path.Combine(_scriptsBaseDir, "lkpatcher")}";
        psi.EnvironmentVariables["PYTHONPATH"] = pythonPath;

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                onLog?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                onLog?.Invoke($"[ERR] {e.Data}");
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            int exitCode = process.ExitCode;
            onLog?.Invoke(exitCode == 0 ? "✔ [Python] Script finalizado con éxito (Código 0)" : $"⚠️ [Python] Script finalizó con código {exitCode}");
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            onLog?.Invoke("🛑 [Python] Ejecución cancelada por el usuario.");
            return -99;
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"❌ [Python] Error al ejecutar proceso: {ex.Message}");
            return -3;
        }
    }

    /// <summary>
    /// Ejecuta el Bypass de SLA/DAA usando bypass_utility.
    /// </summary>
    public Task<int> RunBypassUtilityAsync(string? port = null, Action<string>? onLog = null, CancellationToken ct = default)
    {
        string script = Path.Combine("bypass_utility-master", "main.py");
        // bypass_utility utiliza autodetección USB/PyUSB; no pasar --serialport para evitar que argparse aborte
        return RunScriptAsync(script, "", onLog, ct);
    }

    /// <summary>
    /// Ejecuta mtkclient con el comando solicitado (printgpt, gettargetconfig, r, w, e).
    /// </summary>
    public Task<int> RunMtkClientCommandAsync(string command, string? port = null, Action<string>? onLog = null, CancellationToken ct = default)
    {
        string script = "mtk.py";
        string args = string.IsNullOrEmpty(port) ? command : $"{command} --serialport {port}";
        return RunScriptAsync(script, args, onLog, ct);
    }

    /// <summary>
    /// Ejecuta el parcheo de Little Kernel (LK) mediante lkpatcher.
    /// </summary>
    public Task<int> RunLkPatcherAsync(string lkImagePath, string outputImagePath, string? options = null, Action<string>? onLog = null, CancellationToken ct = default)
    {
        string script = Path.Combine("lkpatcher", "lkpatcher", "__main__.py");
        string opts = options ?? "--patch-policies --cert-bypass";
        string args = $"\"{lkImagePath}\" -o \"{outputImagePath}\" {opts}";
        return RunScriptAsync(script, args, onLog, ct);
    }
}
