using TensaZangetsu.Protocol.Abstractions;
using TensaZangetsu.Protocol.Mtk.Exploits;
using TensaZangetsu.Protocol.Mtk.Exploits.Penumbra;

namespace TensaZangetsu.Services.Loaders;

/// <summary>
/// Fábrica para inicializar el almacén de recursos de explotación Penumbra (MtkExploitResourceStore).
/// Utiliza los recursos embebidos o los carga desde la carpeta Assets/Payloads/Penumbra.
/// </summary>
public static class PenumbraResourceStoreFactory
{
    /// <summary>
    /// Crea una instancia de MtkExploitResourceStore.
    /// Si la carpeta física contiene los binarios, los carga usando PenumbraBytesSource;
    /// de lo contrario, utiliza los recursos embebidos verificados del ensamblado.
    /// </summary>
    public static MtkExploitResourceStore Create(string? assetsDirectory = null)
    {
        string baseDir = assetsDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Payloads", "Penumbra");

        if (Directory.Exists(baseDir))
        {
            var dict = new Dictionary<MtkExploitResourceKind, IDataSource>();

            foreach (var kind in Enum.GetValues<MtkExploitResourceKind>())
            {
                string fileName = $"{kind}.bin";
                string fullPath = Path.Combine(baseDir, fileName);

                if (File.Exists(fullPath))
                {
                    byte[] bytes = File.ReadAllBytes(fullPath);
                    dict[kind] = new PenumbraBytesSource(bytes);
                }
            }

            if (dict.Count == Enum.GetValues<MtkExploitResourceKind>().Length)
            {
                return new MtkExploitResourceStore(dict);
            }
        }

        // Usar los recursos embebidos oficiales de TensaZangetsu.Protocol.Mtk
        return MtkExploitResourceStore.FromEmbeddedResources();
    }
}
