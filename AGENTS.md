# Especificación de Colaboración de Desarrollo - GeekFlashCore

## Información Básica del Proyecto

- Proyecto: `GeekFlashCore`
- Stack tecnológico: C#, .NET 8 / .NET 10, `ITransport` síncrono, `Span<T>`/`Memory<T>`, `ArrayPool<T>`, Serilog.
- Objetivo actual: Construir un framework de desarrollo de flasheo universal reutilizable e independiente, que proporcione conexión estable de dispositivos, Loaders, protocolos, almacenamiento, imágenes y compatibilidad entre fabricantes para CLI, herramientas de escritorio, servicios y otros hosts.
- Enfoque actual: Sahara y Firehose en Qualcomm EDL, Android Sparse, Digest estándar, VIP, Oplus Digest y estrategias de fabricantes como Xiaomi, OnePlus, Nothing, etc.
- Implementaciones de referencia: `D:\Code\CSharp\GeekFlashTool`, `D:\Code\CSharp\QnQcLIB`, `D:\Code\CPlusPlus\qdl`. Los proyectos de referencia se utilizan para confirmar el comportamiento del hardware; no se deben heredar reintentos ilimitados, bucles asíncronos de puerto serie, asignaciones de memoria redundantes o captura silenciosa de excepciones.
- Idioma de comunicación con el usuario: Español. Las convenciones de Git commits, nombres de tipos, campos de protocolo y APIs públicas se mantienen en inglés.

## Estructura del Repositorio

- `src/GeekFlashCore.Protocol.Qcom`: Implementación del protocolo Qualcomm, estado de sesión, Sahara, Firehose, estrategias de fabricantes y operaciones de almacenamiento.
- `src/GeekFlashCore.Protocol.Qcom.Abstractions`: Contratos públicos estables, configuraciones, modelos, interfaces de recursos y tipos de excepciones.
- `src/GeekFlashCore.Protocol.Qcom.MessagePipe`: Capa opcional de adaptación de recursos MessagePipe, sin contaminar el núcleo del protocolo.
- `src/GeekFlashCore.Protocol.Mtk.Abstractions`: Contratos estables MTK, comandos/sesiones BROM, recursos y capacidades de seguridad.
- `src/GeekFlashCore.Protocol.Mtk`: LibUsb BROM/Preloader síncrono, Legacy/XFlash/XML, DA/EMI y almacenamiento en streaming.
- `src/GeekFlashCore.Protocol.Mtk.Extensions`: Extensiones DA cargadas opcionales, SEJ, RPMB, seccfg; el núcleo no depende inversamente de ellas.
- `src/GeekFlashCore.Protocol.Sprd.Abstractions`: Contratos estables SPRD BSL, FDL/profiles explícitos, particiones y propiedad de recursos.
- `src/GeekFlashCore.Protocol.Sprd`: BootROM/FDL1/FDL2 síncrono, tramas con presupuesto acotado, operaciones de streaming en particiones con nombre y vistas algebraicas de sesión.
- `src/GeekFlashCore.Protocol.Abstractions`: Abstracciones públicas compartidas entre protocolos.
- `src/GeekFlashCore.Android.Sparse`: Parser de Android Sparse, planificación y acceso a regiones en streaming.
- `src/GeekFlashCore.Transport.*`: SerialPort, LibUsb y abstracciones de transporte.
- `src/GeekFlashCore.BlockDevice.*`, `src/GeekFlashCore.Gpt*`: Dispositivos de bloques y capacidades GPT.
- `docs/plans`: Diseños, planes de implementación, progreso y riesgos pendientes; base para recuperar el trabajo entre sesiones.
- `.tests`: Proyectos y fixtures de pruebas locales; por convención del proyecto son ignorados por Git y no deben commitearse.

## Flujo de Inicio de Tareas

### Modificaciones Menores

Leer primero este documento, el código relevante y las pruebas existentes; tras confirmar los límites del comportamiento, modificar directamente. Al iniciar, indicar en la sesión el objetivo, los archivos afectados y el plan de validación.

### Funcionalidades Mayores o Modificaciones Multi-Módulo

Se debe formular primero una propuesta de diseño antes de comenzar la implementación. La propuesta debe incluir al menos:

1. Objetivos, no-objetivos y requisitos de compatibilidad.
2. Implementación actual, comportamiento en proyectos de referencia y orden de secuencias de cable que deben preservarse.
3. Contratos públicos, máquina de estados, límites síncronos/asíncronos y propiedad de recursos.
4. Estrategias de seguridad, rendimiento, logging, cancelación, reconexión y recuperación ante fallos.
5. Alcance de archivos, matriz de pruebas, comandos de validación, división de commits y riesgos pendientes.

Tras confirmar el diseño, dividir la tarea en pasos pequeños verificables independientemente. Registrar las propuestas de diseño y el progreso de implementación en `docs/plans`; sincronizar inmediatamente la documentación ante cambios en requisitos, secuencias de protocolo o riesgos.

## Registro y Reportes de Sesión

En cada sesión se debe mantener un contexto recuperable:

- Inicio: Indicar objetivo actual, progreso conocido, estado del workspace, tareas a abordar y plan de validación.
- Durante la ejecución: Al leer contexto, descubrir hechos críticos, alterar planes, encontrar bloqueos o finalizar fases, reportar brevemente conclusiones y siguientes pasos; indicar los comandos en ejecución antes de operaciones largas.
- Fin: Indicar código y documentación completados, resultados de pruebas y compilación, hash de commit, estado del workspace, riesgos pendientes y desde dónde continuar en la próxima sesión.
- No registrar suposiciones como hechos. El código de referencia, los transportes simulados y el comportamiento en dispositivos reales deben diferenciarse con precisión; sin evidencia de hardware, marcar explícitamente como riesgo pendiente de verificación.
- El registro de progreso debe incluir fecha, identificador de tarea, conclusiones de comportamiento, evidencia de validación y riesgos; no limitarse a "completado".

## Restricciones Arquitectónicas de Qualcomm

- `QcomProtocol` es responsable de la fachada pública, obtención asíncrona de recursos, orquestación de conexión, cancelación y ciclo de vida de la sesión.
- Sahara, Firehose, Raw, ACK/NAK XML, expansión de Sparse, lectura/escritura fragmentada y secuencias de fabricantes deben mantener I/O síncrono; la capa síncrona del protocolo no debe bloquear esperando recursos asíncronos.
- La asincronía se reserva exclusivamente para proveedores de recursos, orquestación del host y límites de cancelación. Las solicitudes de recursos deben contar con políticas de cancelación, timeout y liberación de resultados tardíos.
- Las abstracciones públicas no deben exponer tipos de paquetes NuGet de terceros; los resultados de terceros se adaptan internamente a modelos de dominio estables.
- Las sesiones deben ser serializadas. Tras una cancelación en el transporte base, error de protocolo, invalidación de recursos o NAK irrecuperable, la sesión entra en estado inválido explícito y requiere reconexión.
- Los dispositivos de bloques antiguos, leases y handles se validan mediante álgebra de generaciones de sesión; no se debe operar sobre vistas antiguas tras desconexión, reconexión o invalidación de sesión.
- Las secuencias de Digest estándar, VIP, Oplus Digest, etc., deben ser mutuamente excluyentes y tener un orden de inicio riguroso. Las estrategias de fabricantes no deben contaminarse mediante estado global implícito.

## Requisitos de Seguridad y Rendimiento

MTK utiliza LibUsb; BROM, los tres tipos de DA y las extensiones comparten un gate serializado, presupuesto acotado y álgebra de sesiones. El acceso BROM se abre únicamente tras el Probe y antes de la ejecución del DA; tras el callback o salto, el canal queda invalidado. La autenticación estándar la provee el host mediante credenciales y firmas legítimas. Conforme a las autorizaciones, en la parte de exploits se provee el framework `IMtkExploitStrategy` y etapas/contexto/resultado, junto a clases placeholder sin ejecución para LineCode, Carbonara, HeapBait y Unfused; estas clases únicamente declaran descriptores, validan parámetros/cancelación y devuelven NotApplicable sin leer contexto del dispositivo ni ejecutar I/O. Los callbacks del host inyectados explícitamente se invocan en la fase de conexión; no se debe añadir lógica concreta de exploits, registro predeterminado en el núcleo, algoritmos de explotación, generación de parches ni payloads de ataque. La CLI inyecta explícitamente las opciones, permitiendo que NotApplicable continúe con la siguiente estrategia coincidente y, tras probar todas, verifique credenciales DAA. Las extensiones deben verificar ACK/contexto; RPMB no se expone como dispositivo de bloques genérico, y las escrituras en seccfg requieren backup, alineación mínima de escritura y readback, sin reintentar ante resultados desconocidos.

Se permite la inclusión de dependencias utilitarias offline para análisis en MTK: DA windowing offline, búsqueda de bytes y hashes en streaming, metadatos verificados, manifiestos y análisis de arquitectura (`GeekFlashCore.Protocol.Mtk.Analysis` para Arch, IArchAnalyzer, Analyzer y decodificadores de instrucciones ARM/AArch64/Thumb2 para análisis heurístico offline). Los siete binarios de `penumbra-main/core/payloads` se encuentran embebidos como recursos con verificación de longitud y SHA-256 en `MtkExploitResourceStore.FromEmbeddedResources()`, sin distribución de algoritmos de explotación automáticos ni descarga no supervisada.

- Todas las longitudes externas, rangos de sectores, payloads, tamaños XML, contadores y conversiones enteras se validan antes de la asignación o I/O, empleando lógica `checked` o acotada.
- Archivos y recursos grandes emplean `IDataSource`, streaming, ventanas y buffers de pool; no se debe materializar la totalidad de imágenes Raw, Sparse o Digest en memoria.
- Propiedad de Streams explícita: el protocolo únicamente libera los streams abiertos por él mismo; los recursos provistos por el llamador, payloads sensibles y respuestas tardías se liberan o limpian a cero según el contrato de la interfaz.
- XML emplea parsing estructurado y listas permitidas; se prohíbe la concatenación de cadenas que eluda la validación de protocolo, DTD, entidades externas e inyección de comandos múltiples.
- Tokens, firmas, challenges, digests completos, hashes privados, respuestas de autenticación y XMLs personalizados íntegros no deben registrarse en los logs.
- El texto plano del dispositivo sólo se registra en el nivel requerido, estructurado por origen y con límite de longitud; un mismo evento lo registra la capa que posee el contexto para evitar duplicación.
- Encabezados de paquetes, longitudes brutas, progreso por paquete y tramas de protocolo se reservan para Debug/Verbose; conexión, configuración, autenticación, flush e I/O utilizan Information; fallbacks recuperables usan Warning; fallos definitivos e invalidación de sesión usan Error.
- Excepciones visibles para el usuario y plantillas de log utilizan los archivos de recursos `Localization/Strings.resx` y `Strings.en.resx` correspondientes, pasando argumentos mediante métodos de formateo de recursos.

## Restricciones de Pruebas

- Los proyectos de prueba residen estrictamente en `.tests`, no se añaden a Git, artefactos de release ni paquetes NuGet oficiales.
- Se prioriza el transporte simulado para cubrir secuencias de cable, ACK/NAK, cancelación, timeouts, fragmentación, detección de primer paquete, invalidación de recursos y estados de sesión; la validación en hardware real es evidencia complementaria y no reemplaza las pruebas de límites.
- El nuevo comportamiento sigue Test-Driven Development: escribir primero pruebas que reproduzcan defectos o definan comportamiento, y luego implementar la corrección mínima; las pruebas deben verificar resultados de protocolo, bytes escritos, secuencias de cable, estado y liberación de recursos.
- Cada tarea ejecuta al menos las pruebas objetivo, compilación Release y `git diff --check`. Cambios grandes ejecutan la suite completa y verificaciones de memoria/rendimiento.
- Comandos recomendados:

```powershell
dotnet test .tests/GeekFlashCore.Protocol.Qcom.Tests/GeekFlashCore.Protocol.Qcom.Tests.csproj -c Release --no-restore
dotnet build GeekFlashCore.slnx -c Release --no-restore
git diff --check
git status --short --ignored
```

- Fixtures de prueba, logs temporales, benchmarks, `bin/obj` y `temp` deben mantenerse en `.gitignore`; verificar antes de cada commit que no estén rastreados.

## Reglas de Edición y Commits

- Leer antes de modificar; mantener el alcance dentro de los módulos involucrados en la tarea actual, sin refactorizaciones colaterales innecesarias.
- Editar mediante parches conservando modificaciones previas del usuario; no utilizar operaciones destructivas de Git que sobrescriban contenido de origen desconocido en el workspace.
- Cada funcionalidad independiente utiliza commits breves en inglés que describan el comportamiento (ej. `fix(qcom): align legacy digest recovery`).
- Revisar el diff completo antes de commitear, incluyendo correspondencia de claves de recursos, rutas de excepciones, logs sensibles y estado de archivos ignorados.
- El commit solo contiene código de producción y documentación necesaria para la tarea actual; las pruebas locales en `.tests` verifican pero no se commitean.

## Lista de Comprobación de Entrega

Antes de finalizar una tarea importante, verificar:

1. Documentos de diseño, planes de implementación, progreso y riesgos pendientes sincronizados.
2. Comportamiento sin diferencias inexplicadas entre secuencias nuevas y antiguas, o accesos síncronos y asíncronos.
3. Pruebas que cubran nuevos comportamientos y rutas de fallo, pasando tests, compilación Release y comprobación de diffs.
4. Logs limpios de información sensible, textos visibles para el usuario extraídos a recursos, ciclo de vida claro de Streams y recursos.
5. Rutas de rendimiento sin materializaciones adicionales que escalen con el tamaño de imagen ni asignaciones excesivas en memoria; documentar riesgos si no pueden confirmarse localmente.
6. `git status --short` reflejando únicamente los cambios esperados, sin rastrear `.tests`, archivos temporales o compilados.
7. Reporte final detallando archivos modificados, cambios de comportamiento, resultados de verificación, hashes de commit, estado del workspace y riesgos posteriores.

## Fuentes de Progreso Actual

Antes de comenzar trabajos con SPRD, consultar `docs/plans/2026-10-07-sprd-protocol-design.md`, `docs/plans/2026-10-07-sprd-protocol-implementation.md` y `docs/sprd.md`, revisando luego el workspace y commits recientes. Referencias: `D:\Code\CSharp\SPRDClientCore-Xia` y `D:\Code\CPlusPlus\SPD_Flash_Tool_Source_Code`. Direcciones de Loader, unidades de capacidad, layouts de 64 bits, desactivación de transcodificación y relleno impar deben confirmarse explícitamente sin reenvío automático de comandos de escritura o sondeo ciego de capacidad. Pruebas ubicadas en el directorio ignorado `.tests/GeekFlashCore.Protocol.Sprd.Tests`.

Las fuentes de progreso para Qualcomm y MTK se encuentran detalladas en sus respectivos documentos en `docs/` y planes históricos en `docs/plans/`.
