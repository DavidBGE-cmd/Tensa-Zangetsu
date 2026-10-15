# Procedencia de la implementación MediaTek

Los aspectos del formato Legacy eMMC disk PT/MPT v1.0 fueron inspeccionados el 05-10-2026 desde
[JacksonOnly/MtkPt README](https://github.com/JacksonOnly/MtkPt/blob/main/README.md): ubicaciones finales USER,
bloques de 4096 bytes, firmas de versión y encabezado/cola, cuarenta entradas de 88 bytes y la secuencia de bytes bajos.
El parser acotado en C# está implementado de forma independiente; no se copia código de MtkPt. Las estructuras de READ_PMT en tiempo de ejecución permanecen separadas.

La licencia de GeekFlashCore sigue siendo GNU Affero General Public License v3 en `LICENSE`. La implementación de MTK es una reescritura síncrona acotada en C#, conservando las siguientes referencias de protocolo/formato y las atribuciones originales. Los trabajos de referencia históricos a continuación no distribuían binarios. A fecha de 06-10-2026, siete recursos de payloads de Penumbra sin modificar se encuentran embebidos según se detalla más adelante; no se añaden algoritmos concretos de exploit del host, claves privadas ni servicios de autenticación.

| Referencia | Revisión inspeccionada | Atribución original / Licencia | Comportamiento referenciado |
| --- | --- | --- | --- |
| bkerler/mtkclient | `e9fcf97` | B. Kerler, 2018–2024, GPLv3 | Eco/status/checksum BROM, metadatos DA D8/DC, EMI, Legacy/XFlash, layouts seccfg y formato público AES por software; checkpoints del host pre-DA y post-autenticación |
| Shomy/penumbra | `ce13391` | Shomy, 2025–2026, AGPL-3.0-or-later | Enmarcado XFlash y orden de comandos, ciclo de vida de comandos/archivos XML, ACK/contexto de extensiones, ABI del host para memoria/registros/SEJ/RPMB; únicamente metadatos de estrategia, DA devuelto y ubicación de etapas |
| Shomy/mtk-payloads | `e34d980` (histórico) | Avisos por archivo: Shomy, 2025–2026; AGPL/GPL | Trabajos anteriores referenciaron únicamente el contexto de extensión DA y la ABI de comandos; la copia binaria posterior se documenta por separado |
| shomykohai/acon | snapshot main 07-10-2026 | Shomy, AGPL-3.0-or-later | Tabla SoC MMIO (bases uart0/toprgu/hacc/tzcc/ssr y mapeo hwcode) adoptada para el catálogo de chips, incluyendo SoCs modernos ausentes en mtkclient |
| GeekFlashTool.MtkClient | `67ced05` | Los avisos del proyecto existente se mantienen | Verificación cruzada de campos big-endian FC, layout DA antiguo y comportamiento de comandos XML; el código de aplicación/proveedor/cuenta no se copia |

Rutas relevantes de mtkclient: `Library/mtk_preloader.py`, `config/brom_config.py`, `config/mtk_config.py` (nombres de chips estándar, alias DA y registros watchdog únicamente), `Library/DA/mtk_da_handler.py::configure_da`, `Library/DA/daconfig.py`, `Library/DA/legacy/dalegacy_lib.py`, `Library/DA/legacy/dalegacy_flash_param.py`, `Library/DA/xflash/xflash_lib.py`, `Library/DA/xml/xml_lib.py`, `Library/Hardware/seccfg.py`, `Library/Hardware/hwcrypto_sej.py`.

Rutas relevantes de penumbra: `core/src/da/dafile.rs`, `core/src/da/xflash/{cmds,xflash_lib,da_protocol,exts}.rs`, `core/src/da/xml/{cmds,xml_lib,da_protocol,flash,storage,exts}.rs`, y las declaraciones de framework en `core/src/exploit/mod.rs` y `core/src/macros.rs`. No se copian estrategias concretas ni algoritmos de modificación de DA. Rutas relevantes de payloads: definiciones de ABI para `da_x`, `da_xml` y `libsej`. Los archivos de C# correspondientes conservan los avisos de origen. El texto de GPLv3 está incluido en `licenses/MTK-GPL-3.0.txt`.

Algunos árboles de trabajo de referencia inspeccionados ya contenían modificaciones locales. Las revisiones identifican sus bases, no una aseveración de que cada archivo coincidiera estrictamente con su commit. Los árboles de referencia fueron de sólo lectura y no se modificaron.

La compleción estándar del 05-10-2026 también referencia `core/src/core/bootctrl.rs` de penumbra (Shomy, 2026, AGPL-3.0-or-later) para los campos de control de boot de Android y CRC, y mtkclient `Library/DA/{legacy/dalegacy_flash_param,xflash/xflash_lib,xml/xml_cmd,xml/xml_lib}.py` para consultas estándar de sólo lectura, metadatos de particiones PMT/XML, comandos de registros y geometría NAND/NOR/SDMMC. Los metadatos de DA `m_start_offset` se conservan como límite de longitud/firma, sin añadirse a `m_start_addr`. No se agregaron algoritmos de explotación ni payloads para el dispositivo.

El 09-10-2026, la corrección del límite de región v6 verificó `penumbra-main/core/src/da/xml/protocol.rs` y su revisión bloqueada de `hacc` `e5a68124e7d795465804aaf2d11cef43cf8b7267`, `src/da.rs`: la longitud del código se calcula a partir de `length - sig_len`, las ventanas de archivo usan `offset` y la ejecución utiliza `addr`, independientemente del campo de cuarta región en bruto. Un DA de fabricante offline también almacena ese campo como el offset absoluto del archivo para la firma. GeekFlashCore conserva los valores relativos y valida adicionalmente este límite absoluto exacto para XML/v6 sin reescribir metadatos ni alterar autenticación, direcciones, bytes de subida ni el orden de comandos del dispositivo. No se copió código de referencia.

Ese mismo día, la corrección de compatibilidad para consultas opcionales XML verificó `send_cmd`/`lifetime_ack` de Penumbra contra una captura de dispositivo con respuesta exacta `ERR!UNSUPPORTED` y `CMD:END` llevando `result=ERR!UNSUPPORTED` más un mensaje legible. Únicamente este rechazo opcional explícito se acepta tras un END estructuralmente válido y su ACK; los errores desconocidos, timeouts y fallos en comandos obligatorios siguen siendo terminales. La evidencia de consulta SLA permanece en `Unsupported`, no `Authenticated`, y la autenticación DA2 continúa verificándose de forma independiente.

## Port de EXP y PatchDA (07-10-2026)

Tras autorización explícita el 07-10-2026, las cuatro estrategias de exploit de Penumbra y ambos parchadores de DA fueron portados a implementaciones acotadas en C# bajo `src/GeekFlashCore.Protocol.Mtk/Exploits/Penumbra/`. El port preserva la atribución de los autores originales y las obligaciones de licencia:

- `utils/patching.rs` (Shomy, 2026, AGPL-3.0-or-later) → `PenumbraPatching`.
- `exploit/mod.rs` DaEntryExt hash-slot/arch detection y `get_v6_payload` (Shomy, 2025–2026, AGPL-3.0-or-later) → `PenumbraDaMetadata`, `PenumbraPayloadFormat`.
- `exploit/linecode.rs` (Shomy, 2025–2026, AGPL-3.0-or-later; créditos originales del exploit: Chimera Tool team; detalles de implementación estudiados de R0rt1z2/kamakiri mt8516-cupcake, trabajo original chaosmaster (k4y0z) y xyzz bajo MIT; inspirado en el patcher genérico de bkerler en mtkclient) → `PenumbraLinecodeTable`, `PenumbraLinecodeTrigger`.
- `exploit/carbonara.rs` (Shomy, 2025–2026, AGPL-3.0-or-later; patrones de protección tomados de mtkclient) → Estrategia Carbonara y `PenumbraDaChannelSupport`.
- `exploit/heapbait.rs` (Shomy y R0rt1z2, 2026, AGPL-3.0-or-later; créditos originales del exploit al equipo de Chimera Tool) → `PenumbraHeapBaitRunner`.
- `da/xflash/patch.rs`, `da/xml/patch.rs` (Shomy, 2025–2026, AGPL-3.0-or-later; cadenas de análisis originadas en mtkclient, B. Kerler, GPLv3) → `PenumbraXFlashDaPatcher`, `PenumbraXmlDaPatcher`.

Desviaciones de la referencia (documentadas por transparencia): la macro `exploit!` de referencia continúa silenciosamente tras una estrategia fallida, mientras que este port mantiene la semántica de fallo terminal del framework una vez iniciado el I/O del dispositivo; las asignaciones ilimitadas y permisivas de la referencia (sled de 50 MiB, buffers de diff de DA completos) se gestionan en streaming o con límites acotados; el fallo tras el I/O del dispositivo invalida la sesión en lugar de continuar. Estos ports son únicamente código estático: ninguna ejecución en hardware ha validado ninguna estrategia, y un resultado Completed nunca prueba que un dispositivo haya aceptado un parche.

Las modificaciones respecto a las referencias incluyen recuentos/longitudes/XML acotados, comprobaciones explícitas de endianness y estado final, generaciones serializadas, propiedad de streams prestados, limpieza tardía de autenticación, Raw/Sparse en streaming, prerrequisitos estrictos de extensiones y planificación/lectura verificada de seccfg. Se excluyen firmas ficticias, considerar timeout como éxito, fallos silenciosos de checksum, XML arbitrario y reintentos automáticos tras escrituras desconocidas.

## Referencias de funciones normales actualizadas (05-10-2026)

La referencia principal actual es el archivo `D:\Code\Rust\penumbra-main` del usuario, sin metadatos de Git. Sus archivos normales `core/src/da/{protocol,scatter,types}.rs`, `core/src/da/{xflash,xml}/{protocol,flash,cmd,exts}.rs` y `tui/src/cli/commands/device/keys.rs` definen el comportamiento de Scatter/particiones nativas/eFuse/recursos y la ABI de extensiones de Penumbra2. Huellas digitales de archivos (SHA256) que identifican las entradas inspeccionadas:

| Ruta en el archivo | SHA256 |
| --- | --- |
| `Cargo.lock` | `BF661EB3C6AC20F62059751168DF2F3A8F195A235243FFDDD96CADC1C3ED5806` |
| `core/src/da/scatter.rs` | `D789C65C764DAC24D29BE4682A03F6EDEC4462E530495EDECCB8039CC541ECE4` |
| `core/src/da/xflash/exts.rs` | `0CD543F08A394E13D7CEB4223C5D2793F7BA6290EBE9E21E28BCEAA7A6A1BF1D` |
| `core/src/da/xml/exts.rs` | `E52BEBD172340109D59A62288F19B999B8916ACF88D3E2D0A8EFD64B1C8491E6` |
| `core/src/da/xflash/protocol.rs` | `CB17E5926F4D0483DC7DABA44792AAD23BD73DA6C02A316783EE518E3AC84E23` |
| `core/src/da/xml/protocol.rs` | `B9ED36BD0F956A26DB249EA19587A66457C77F3B98D50CE28FC0D1F1CE9F5408` |
| `core/src/macros.rs` | `1100C6949EC249F60DE6EEF3CF4FAFE59969928BED5005DC40A721AF0A718522` |

Los archivos actualizados de protocolo/macros fueron inspeccionados únicamente para la ubicación de callbacks del host y el orden habitual de autenticación.
No se portó ningún exploit concreto, construcción automática de estrategias, supresión de errores de exploit ni aseveración de estado parcheado o autenticado.

Referencias adicionales normales de mtkclient: `Library/DA/legacy/dalegacy_lib.py` (NAND DF e IoT DA3/CDC) y `Library/Hardware/hwcrypto_{sej,gcpu,dxcc}.py` (secuencias estándar de registros, reescrituras acotadas de polling, constantes públicas de formatos de imagen y derivación normal). Se excluyeron la desactivación de DAPC/seguridad, bypass de protección DMA, eliminación de listas negras, modificaciones de firmware/DA e inyección de payloads. Perfiles explícitos reemplazan direcciones hardcodeadas de DMA/clock.

El layout `SecRpmbInfo` para RPMB XML/UFS se verificó contra [hacc en e5a68124](https://github.com/shomykohai/hacc/blob/e5a68124e7d795465804aaf2d11cef43cf8b7267/src/common/rpmb.rs), la dependencia exacta fijada en Cargo.lock. Atribución: Copyright (c) 2026-present Shomy, rva3, MIT; el texto de la licencia se encuentra en `licenses/MTK-HACC-MIT.txt`. Solo se referencian especificaciones de layout/enum y comunicación ordinaria con el host; no se distribuyen claves, payloads ni código de dispositivo.

## Recursos de payloads embebidos (06-10-2026)

A solicitud explícita, los siete archivos .bin de `D:/Code/Rust/penumbra-main/core/payloads` fueron copiados sin alteraciones a `src/GeekFlashCore.Protocol.Mtk/Exploits/Resources/Payloads` y embebidos en el ensamblado del protocolo. Sus longitudes y huellas SHA-256 constan en [dependencies.json](src/GeekFlashCore.Protocol.Mtk/Exploits/Resources/dependencies.json).

El archivo identifica a [shomykohai/mtk-payloads](https://github.com/shomykohai/mtk-payloads) como proyecto de origen. Los avisos upstream identifican AGPL-3.0-or-later, Copyright (c) 2025–2026 Shomy para brom_defuse, extloader, SLA XML y extensiones DA, y Copyright (c) 2026 Shomy, R0rt1z2 para hakujoudai. El texto íntegro de AGPLv3 está en [licenses/MTK-PAYLOADS-AGPL-3.0.txt](licenses/MTK-PAYLOADS-AGPL-3.0.txt). Los avisos upstream para extensiones DA acreditan también GPLv3 libsej, Copyright (c) 2024 B.Kerler, 2025 Shomy; el texto de GPLv3 está en `licenses/MTK-GPL-3.0.txt`. Avisos adicionales de componentes (MIT, nanoprintf 0BSD/Unlicense, libc derivada de musl y SHA-256 de dominio público) se retienen en [licenses/MTK-PAYLOAD-NOTICES.txt](licenses/MTK-PAYLOAD-NOTICES.txt).

Esta copia únicamente añade acceso explícito a recursos offline y metadatos de integridad. La capa de recursos no implementa llenado de parámetros, modificación de recursos, parcheo de DA, direccionamiento objetivo, subida, saltos ni ejecución de exploits. Los cuatro métodos Execute de estrategia permanecen como placeholders no ejecutables; los valores predeterminados permanecen vacíos.

## Análisis de arquitectura offline genérico (06-10-2026)

`core/src/utils/analysis/{mod,arm,aarch64,thumb}.rs` del archivo Penumbra fue adaptado a C# bajo `GeekFlashCore.Protocol.Mtk.Analysis`. Atribución: Copyright (c) 2025–2026 Shomy (thumb: 2026), AGPL-3.0-or-later; los avisos por archivo permanecen en los analizadores adaptados. El texto completo de AGPLv3 está en `LICENSE`.

Esta adición implementa análisis offline genérico seleccionado por el llamador con ventanas acotadas de origen, cancelación y propiedad explícita de streams. No copia búsquedas de símbolos específicas de exploits, transformaciones de DA ni ejecución en dispositivos. La propiedad original del buffer completo fue sustituida por un IDataSource prestado y caché fija, aplicando correcciones documentadas de límites y bifurcaciones. LLVM se utilizó únicamente para ensamblar/desensamblar instrucciones sintéticas de prueba; no es una dependencia en runtime y sus binarios no se distribuyen.
