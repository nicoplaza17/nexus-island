# Nexus Island

Isla flotante contextual para Windows, inspirada en Dynamic Island (C# · .NET 10 · WPF).

## Requisitos

- Windows 10 (19041+) o Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Compilar y ejecutar

```powershell
dotnet build NexusIsland.slnx -c Release
.\src\NexusIsland.App\bin\Release\net10.0-windows10.0.19041.0\NexusIsland.exe
```

Para salir: clic derecho en el ícono de la bandeja (junto al reloj) → **Salir**.

## Instalador

```powershell
.\tools\build-installer.ps1
```

Genera `artifacts\installer\NexusIsland-Setup-X.Y.Z.exe` (~59 MB, con .NET incluido). Requiere
[Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`). La versión sale de `Directory.Build.props`.

- Se instala por usuario en `%LocalAppData%\Programs\Nexus Island`, sin permisos de administrador.
- Para instalar sin preguntas: `NexusIsland-Setup-X.Y.Z.exe /VERYSILENT /SUPPRESSMSGBOXES`.
- Se desinstala desde Configuración → Aplicaciones. La configuración y los logs del usuario se conservan.

## Herramientas

| Script | Para qué |
|---|---|
| `tools\build-installer.ps1` | Publica con .NET incluido y arma el instalador |
| `tools\make-icon.ps1` | Regenera `assets\NexusIsland.ico` (todos los tamaños) |
| `tools\soak.ps1` | Prueba de resistencia: registra memoria, handles y objetos gráficos en un CSV. Para 24 h: `.\tools\soak.ps1 -Minutes 1440 -IntervalSeconds 300` |

## Estructura

```
src/
  NexusIsland.Core              Lógica pura sin Windows: estados de la isla, resortes, cálculo de ubicación, logging
  NexusIsland.Services.Windows  Integración con Windows: monitores, pantalla completa, atajo global, estilos de ventana
  NexusIsland.Modules.Media     Módulo Multimedia: ViewModel, plantillas visuales, barra de progreso
  NexusIsland.Modules.Tools     Herramientas: temporizador, cronómetro, recordatorios, portapapeles
  NexusIsland.Modules.Monitor   Monitoreo del sistema: panel (CPU, RAM, red, batería, temperatura aprox.) y alertas
  NexusIsland.App               Interfaz WPF: ventana de la isla, animador, ubicación/arrastre, ícono de bandeja
tests/
  NexusIsland.Core.Tests        Tests unitarios del Core (xUnit)
installer/                      Script de Inno Setup
tools/                          Instalador, ícono, prueba de resistencia
assets/                         Ícono de la app
```

Tests: `dotnet test tests/NexusIsland.Core.Tests`

Datos de la app:

- Logs: `%LocalAppData%\NexusIsland\logs\nexus-AAAAMMDD.log` (se guardan 7 días)
- Configuración: `%AppData%\NexusIsland\settings.json` (se puede editar a mano; si queda dañada se respalda como `.bad`)

## Variables de diagnóstico

| Variable | Efecto |
|---|---|
| `NEXUS_RENDER=gpu` | Fuerza el render por GPU (por defecto es por software, que ahorra unos 50 MB de RAM) |
| `NEXUS_LOG=debug` | Registra también eventos del mouse y cambios de estado de la isla |
| `NEXUS_BENCH=1` | Modo benchmark: la isla cicla sola entre estados (para medir CPU sin tocar el mouse) |
| `NEXUS_BENCH_MS=2000` | Intervalo del modo benchmark en ms (por defecto 700) |
| `NEXUS_START_EXPANDED=1` | Arranca con la isla expandida (para probar controles con UI Automation) |
| `NEXUS_DISABLE_MODULES=media` | Desactiva módulos por id, separados por coma |
| `NEXUS_OPEN_SETTINGS=1` | Abre la Configuración al arrancar (para pruebas con UI Automation) |
| `NEXUS_PIN_EXPANDED=1` | La isla no se contrae al irse el mouse (pruebas automáticas con la PC en uso) |
| `NEXUS_MONITOR_TEST=1` | Umbrales de alerta mínimos (CPU/RAM ≥ 1 % por 2 s) para probar las alertas sin forzar la PC |

## Cómo agregar un módulo

1. Crear un proyecto WPF `NexusIsland.Modules.X` que referencie solo a `NexusIsland.Core`.
2. Implementar `IIslandModule`: en `StartAsync` recibe un `IActivityHost` y publica una `IslandActivity`
   (clave, id de módulo, prioridad y ViewModel) cuando tenga algo para mostrar; la quita con `Remove`.
3. Incluir un `ResourceDictionary` con tres `DataTemplate`: `X.Compact` (148×32), `X.Peek` (248×40) y `X.Expanded` (398×182).
4. Registrarlo en `App.ConfigureServices` con `services.AddSingleton<IIslandModule, XModule>()`.

El **árbitro** (`ActivityArbiter`) decide qué se muestra: mayor prioridad gana (reloj 0, multimedia 50,
tareas activas 100, alertas 200); si empatan, la más reciente. `RequestAttention` dispara la notificación temporal.

---

## Bitácora de etapas

### Etapa 0 — Base y prueba de concepto ✅ (16/11/2026)

**Implementado**

- Solución de 3 proyectos con configuración común (`Directory.Build.props`).
- Ventana de la isla: transparente por píxel, sin borde, siempre encima, fuera de Alt+Tab y de la barra de tareas, **no roba el foco** al hacer clic.
- Lienzo fijo de 560×260 con la cápsula adentro. Los clics en la zona transparente pasan a la app de abajo.
- DPI por monitor (Per-Monitor V2): se ve nítida a 125 %, 150 %, etc.
- Instancia única: abrir la app dos veces no crea dos islas.
- Ícono de bandeja: clic izquierdo muestra u oculta la isla; menú con Mostrar/Ocultar, Abrir carpeta de logs y Salir.
- Logging a archivo y captura de errores no controlados.

**Verificado (Release, Windows 11 26200, escala 125 %)**

| Prueba | Resultado |
|---|---|
| Estilos Win32 (TOPMOST, LAYERED, TOOLWINDOW, NOACTIVATE) | ✅ |
| No queda como ventana activa al abrirse | ✅ |
| Clic en la cápsula → isla; clic en la zona transparente → app de abajo | ✅ |
| Segunda instancia se cierra sola | ✅ |
| CPU en reposo | 0,0 % |
| RAM (Administrador de tareas) | **~19 MB** con render por software (~70 MB con GPU) |

**Decisión técnica:** el render por GPU de WPF carga Direct3D y el driver de video (~50 MB extra). Para una ventana chica y transparente, el render por software es suficiente. Se revalida en la Etapa 1 midiendo la CPU durante las animaciones.

**Limitaciones conocidas (se resuelven en etapas siguientes)**

- Posición fija arriba al centro del monitor **principal** (multi-monitor: Etapas 1 y 3).
- Puede tapar elementos de la parte superior de apps maximizadas (posición configurable: Etapa 3).
- Otras apps "siempre encima" pueden quedar por encima (re-afirmación periódica: Etapa 1).
- No se oculta en pantalla completa (Etapa 1).
- El ícono de la bandeja es provisorio y se dibuja por código.

**Posibles errores**

- *"dotnet no se reconoce"*: cerrar y volver a abrir la terminal después de instalar el SDK.
- *La isla no aparece*: revisar si ya hay una instancia en la bandeja (incluso en la zona oculta ⌃) y leer el log del día.
- *El ícono queda "fantasma" en la bandeja tras un cierre forzado*: pasar el mouse por encima y desaparece (comportamiento de Windows).

### Etapa 1 — Shell, animaciones y comportamiento ✅ (16/11/2026)

**Implementado**

- **Tres estados** con máquina de estados pura en el Core (`IslandStateMachine`):
  - *Contraída* (150×34): punto + hora.
  - *Vistazo* (250×42): al dejar el mouse encima 140 ms. Se contrae 260 ms después de salir.
  - *Expandida* (400×170): al hacer clic. Se contrae sola 2,2 s después de que el mouse se va; otro clic la cierra.
- **Animaciones con resortes físicos** (`Spring`): tamaño y redondeo con un leve rebote, más fundido y escala del contenido. Se pueden interrumpir a mitad de camino sin saltos. Solo consumen CPU mientras se mueven.
- **"Reducir movimiento"**: si en Windows están desactivados los efectos de animación, los cambios son instantáneos.
- **Arrastre** con el mouse a cualquier lugar. Al soltar cerca de arriba al centro, un **imán** la vuelve a anclar. Si queda suelta, se ajusta para no quedar cortada por el borde.
- **Multi-monitor y escalas distintas**: se ancla arriba al centro del monitor donde se suelte y se re-ubica ante cambios de resolución, monitores, escala o barra de tareas.
- **Se aparta en pantalla completa** (videos, presentaciones, navegador con F11) en el monitor donde está, y vuelve al salir.
- **Atajo global `Ctrl+Alt+N`** para ocultar/mostrar (también desde la bandeja), con fundido.
- Re-afirma "siempre encima" cada vez que cambia la app activa.
- Hora y fecha reales en los tres estados (el contenido definitivo llega con los módulos).

**Verificado (Release, 2 monitores: 1920×1080 al 125 % + 1920×1080 al 100 %)**

| Prueba | Resultado |
|---|---|
| Tests unitarios del Core (estados, resortes, ubicación) | ✅ 20/20 |
| Hover → vistazo → contraer al salir | ✅ automático |
| Clic → expandida → clic → vistazo | ✅ con mouse real (log) |
| Expandida se contrae sola al irse el mouse | ✅ automático |
| Clic no roba el foco a la app activa | ✅ |
| Arrastre a otra posición / imán de vuelta al centro | ✅ automático y con mouse real |
| Mover al monitor de 100 %: se reescala y re-ancla en (2650, 0); al volver, (672, 0) | ✅ |
| `Ctrl+Alt+N` oculta y vuelve a mostrar | ✅ |
| App en pantalla completa → isla oculta; al cerrarla → visible | ✅ |
| RAM (Administrador de tareas) | 19,5 MB |
| CPU en reposo | 0 % (las animaciones solo corren mientras hay movimiento) |
| CPU animando sin parar (peor caso, benchmark) | 35 % de un núcleo; una transición real dura ~0,5 s |

**Decisiones técnicas**

- **Sin `DropShadowEffect`**: en render por software triplicaba la CPU de las animaciones (76 % → 28 % de un núcleo sin sombra). Se reemplazó por dos halos semitransparentes (35 %). La GPU no mejoraba la CPU (30 %) y subía la RAM a 115 MB, así que se mantiene el render por software.
- **Coordenadas en píxeles físicos** (Win32) para ubicar y arrastrar, en lugar de las unidades de WPF: así funciona bien entre monitores con escalas distintas.
- Fondo de la isla al 98 % de opacidad: al 95 % el texto blanco de la app de abajo se notaba demasiado.

**Limitaciones conocidas**

- La posición (anclada o suelta) todavía no se guarda: al reiniciar vuelve arriba al centro del monitor principal (Etapa 3).
- Si se la suelta en la parte baja de la pantalla, la isla igual se expande hacia abajo (queda dentro de la pantalla, pero se podría expandir hacia arriba: mejora futura).
- Los halos de la sombra (unos pocos píxeles alrededor) no dejan pasar el clic a la app de abajo.
- Los juegos en pantalla completa "exclusiva" tapan cualquier ventana; ahí la isla no se ve, pero tampoco molesta.
- Si otra app ya usa `Ctrl+Alt+N`, el atajo no se registra (queda una advertencia en el log). Se podrá cambiar en la Etapa 3.

**Posibles errores**

- *La isla no reacciona al pasar el mouse*: si una app está en pantalla completa en ese monitor, la isla está apartada a propósito.
- *El atajo no hace nada*: revisar el log; si dice "otra app ya lo usa", hay conflicto con otro programa.

### Etapa 2 — Módulo Multimedia + sistema de actividades ✅ (16–17/11/2026)

**Implementado**

- **Sistema de actividades** (Core): cada módulo publica una `IslandActivity` y el `ActivityArbiter` decide cuál se ve por prioridad. El reloj pasó a ser una actividad más (prioridad mínima), así que la isla siempre tiene algo para mostrar.
- **Notificación temporal**: al cambiar de canción la isla muestra el vistazo 3 s y vuelve sola. No interrumpe si estás encima con el mouse o si está expandida, y no se dispara al arrancar la app.
- **Servicio GSMTC** (`GsmtcMediaSessionService`): lee título, artista, álbum, carátula, estado, línea de tiempo y qué controles permite la app. Agrupa los avisos que llegan seguidos y lee la carátula solo cuando cambia la canción.
- **Módulo Multimedia** (proyecto aparte, solo depende del Core):
  - *Contraída*: carátula + indicador de reproducción (barras estáticas, sin animación continua para no gastar CPU).
  - *Vistazo*: carátula, título y artista.
  - *Expandida*: carátula grande, título, artista, app de origen, barra de progreso con tiempos y botones anterior / play-pausa / siguiente. Clic en la barra para adelantar (si la app lo permite).
- La posición se **estima** entre avisos, porque muchas apps no la informan en vivo.
- Botones con nombre accesible (Narrador / UI Automation) y deshabilitados cuando la app no permite la acción.
- La barra de progreso se actualiza **solo con la isla expandida** y una vez por segundo.
- Si la sesión se corta un instante (cambio de pista o de pestaña), se esperan 1,5 s antes de volver al reloj, para no parpadear.

**Verificado (Release, con música real desde una app web de Chrome)**

| Prueba | Resultado |
|---|---|
| Tests unitarios (estados, notificación temporal, árbitro, estimación de posición, nombres de apps) | ✅ 48/48 |
| Detecta la canción sonando: título, artista, carátula (27–53 KB), estado | ✅ |
| Cambio de canción → la isla se actualiza sola | ✅ (3 canciones distintas en las pruebas) |
| Vistas contraída / vistazo / expandida con datos reales | ✅ capturas |
| Botón Pausar (vía UI Automation, sin mouse) → la música se pausa y el progreso se congela | ✅ |
| Botón Reproducir → la música vuelve a sonar | ✅ |
| Sin música → la isla vuelve al reloj | ✅ |
| Al arrancar no aparece la notificación temporal | ✅ (corregido) |
| CPU sin música, contraída o expandida | 0,1–1,5 % de un núcleo (varía con la actividad de la PC) |
| CPU con música, contraída | 0,4 % |
| CPU con música, **expandida** | 0,12 % promedio en reposo (medido en tramos de 2 s; los picos coinciden con actividad real: mouse, cambios de sesión) |
| RAM | ~30 MB (antes 19,5 MB: suma el módulo multimedia, la carátula y las APIs de Windows) |

**Limitaciones conocidas (reales de la API de Windows)**

- Solo aparecen apps que publican en los controles multimedia de Windows. VLC 3.x de escritorio no publica.
- Se muestra la sesión que Windows considera "actual" (la de las teclas multimedia). Si suenan dos apps, elige Windows.
- Las **apps web instaladas** (PWA) se identifican como "App web (Chrome/Edge)". Windows no da su nombre real por esta vía; se podría resolver buscando su acceso directo (mejora futura).
- La carátula es la que manda la app: a veces falta, o es el ícono de la app (lo vimos con una PWA que mandó el logo de Chrome).
- Adelantar y siguiente/anterior dependen de cada app.
- **Cualquier app que reproduzca audio puede volverse la sesión actual**: en las pruebas, WhatsApp Desktop apareció como "(1) WhatsApp", sin artista ni carátula, y disparó notificaciones temporales. Solución prevista para la Etapa 3: lista de apps ignoradas en la configuración.

**Posibles errores**

- *La isla muestra el reloj aunque suena música*: esa app no publica en los controles multimedia de Windows. Se puede verificar si aparece en el control de volumen de Windows (Win+A → panel multimedia).
- *"No se pudo acceder a los controles multimedia" en el log*: ediciones "N" de Windows sin el Media Feature Pack.

### Etapa 3 — Configuración ✅ (17/11/2026) → **MVP completo (v0.3.0)**

**Implementado**

- **`settings.json`** con guardado seguro (archivo temporal + reemplazo atómico), valores por defecto para lo que falte, corrección de valores inválidos y respaldo `.bad` si el archivo está dañado.
- **Ventana de Configuración** (bandeja → *Configuración…* o doble clic en el ícono), con el tema Fluent de WPF: estilo Windows 11, claro u oscuro según el sistema. Los cambios se aplican y guardan al instante.
- **Posición recordada**: anclada en un monitor elegido o suelta donde la dejaste. Si el monitor ya no está, vuelve al principal. Si cambió el tamaño, se ajusta para entrar entera.
- **Elegir monitor** desde la configuración (con resolución y escala de cada uno).
- **Tamaño**: chica (85 %), normal o grande (115 %). Escala la isla y su lienzo.
- **Iniciar con Windows** (clave `Run` del usuario, sin permisos de administrador). Si Windows lo tiene desactivado desde el Administrador de tareas, se avisa y no se pisa. Si se mueve la carpeta, la ruta se actualiza sola al arrancar.
- **Ocultar en pantalla completa**: ahora se puede desactivar.
- **Atajo configurable**: se captura la combinación en la ventana. Si otra app ya lo usa, se avisa y se conserva el anterior. Se exige al menos un modificador.
- **Módulos activables**, con aviso y botón *Reiniciar ahora*.
- **Apps ignoradas** para multimedia. Si la sesión que elige Windows está ignorada, se busca otra que esté sonando (`MediaSessionSelector`).
- **Notificar al cambiar de canción**: activable.

**Verificado (Release, 2 monitores 125 % + 100 %, UI Automation sin mover el mouse)**

| Prueba | Resultado |
|---|---|
| Tests unitarios (configuración, ubicación guardada, elección de sesión + anteriores) | ✅ 63/63 |
| Primer arranque sin configuración → valores por defecto, anclada en monitor 1 | ✅ |
| Elegir Monitor 2 → la isla pasa a (2650, 0); "Grande" → 529×253 px; se guarda | ✅ |
| Reiniciar la app → vuelve al monitor 2 en tamaño grande | ✅ |
| Iniciar con Windows → se escribe y se borra la clave del Registro | ✅ |
| Ignorar "App web (Chrome)" → la isla vuelve al reloj; dejar de ignorar → vuelve la música | ✅ |
| Desactivar módulo → aparece *Reiniciar ahora* → proceso nuevo con el módulo desactivado | ✅ |
| Cambiar de monitor no le roba el foco a la app activa | ✅ |
| RAM sin abrir la configuración / con la configuración abierta | 22 MB / 32 MB |

**Limitaciones conocidas**

- La ventana de Configuración suma ~9 MB que .NET conserva después de cerrarla (recursos del tema ya cargados).
- El tema Fluent de WPF está marcado por Microsoft como *experimental* (aviso WPF0001). Funciona, pero puede cambiar en versiones futuras de .NET.
- Mientras se captura un atajo nuevo, si se presiona el atajo actual, Windows lo intercepta y oculta/muestra la isla en lugar de capturarlo.
- Los monitores se identifican por su nombre de Windows ("DISPLAY2"), que puede cambiar si se reconectan en otro orden. En ese caso la isla vuelve al principal.
- La isla tarda un instante en ubicarse al arrancar en un monitor con otra escala: primero se calcula con la escala del principal y después se corrige sola.
- Sin probar automáticamente (requiere teclado): el cambio de atajo y el aviso de atajo ocupado.

### Etapa 4 — Endurecimiento e instalador ✅ (17/11/2026, v0.4.0)

**Implementado**

- **Suspensión / hibernación**: al suspender, la isla se contrae. Al volver, a los 2 s se reubica, re-afirma "siempre encima", renueva el atajo y reconecta los controles multimedia de Windows (que pueden dejar de avisar tras dormir).
- **Reinicio del Explorador** (mensaje `TaskbarCreated`): la isla se reacomoda al área de trabajo nueva. El ícono de bandeja lo vuelve a agregar WinForms solo.
- **Resincronización multimedia cada 60 s**: si Windows pierde un aviso, la isla se corrige sola.
- **Reconexión multimedia** completa (`ReconnectAsync`), sin suscripciones duplicadas.
- **Ícono propio** (`assets\NexusIsland.ico`, 10 tamaños): en el .exe, la bandeja, la Configuración y el instalador.
- **Instalador Inno Setup** con .NET incluido (autocontenido, x64, precompilado ReadyToRun), por usuario, en español, con desinstalador. Al desinstalar también borra la entrada de "Iniciar con Windows".
- **`tools\soak.ps1`**: prueba de resistencia reutilizable (CSV + resumen de tendencia).

**Verificado**

| Prueba | Resultado |
|---|---|
| Tests unitarios | ✅ 63/63 |
| Suspensión + reanudación (mensajes de Windows simulados) → contraer, reubicar, atajo, reconexión multimedia | ✅ |
| Explorador reiniciado (mensaje `TaskbarCreated` simulado) → reubicación | ✅ |
| Prueba de resistencia acelerada: 8 min, ~3.200 animaciones | ✅ memoria 37→32 MB, GDI 31→31, USER 25→24; handles 485→535 (acompañan a hilos de trabajo de .NET: vigilar en 24 h) |
| Instalación silenciosa, por usuario, sin administrador | ✅ carpeta, acceso en Inicio, entrada en Aplicaciones "Nexus Island 0.4.0" |
| La versión instalada arranca | ✅ 21 MB de RAM |
| Reinstalar encima con la app abierta (actualización) | ✅ |
| Desinstalar con la app abierta y "Iniciar con Windows" activo | ✅ borra carpeta, acceso, entrada y clave de inicio; conserva la configuración |

**Tamaños**: instalador 59 MB; instalada 231 MB en disco (incluye .NET completo y código precompilado).

**Pendiente de prueba real (no se simula en una PC en uso)**

- Suspender la PC de verdad y volver (se probaron los mensajes de Windows, no la suspensión física).
- Reiniciar el Explorador de verdad (Administrador de tareas → Explorador de Windows → Reiniciar) y confirmar que el ícono de bandeja vuelve.
- Prueba de resistencia de 24 h en uso normal: `.\tools\soak.ps1 -Minutes 1440 -IntervalSeconds 300`.

**Limitaciones conocidas**

- **El instalador no está firmado digitalmente.** En otras PC, Windows SmartScreen va a mostrar "Windows protegió tu PC" (se sigue con *Más información → Ejecutar de todas formas*). Firmar requiere un certificado de firma de código o publicar en Microsoft Store: queda para la Etapa 9.
- El tamaño (231 MB instalada) es el precio de incluir .NET. Con "pedir .NET si falta" serían ~10 MB, a cambio de que el usuario instale el runtime de Microsoft.
- Si la app se cierra a la fuerza (por ejemplo, al actualizar), el ícono de la bandeja puede quedar "fantasma" hasta pasar el mouse por encima (comportamiento de Windows).

### Etapa 5 — Herramientas rápidas ✅ (17/11/2026, v0.5.0)

**Implementado**

- **Panel de herramientas**: botón ⊞ abajo a la derecha de la isla expandida. Lo arman los módulos que implementan `IQuickActionProvider` (cada botón es una `QuickAction`).
- **Páginas**: si hay varias actividades (temporizador + música + reloj), la isla expandida muestra puntitos abajo al centro. Se cambia con la rueda del mouse o con clic en un puntito. Al contraerse vuelve a lo más importante. Las alertas le ganan a la página elegida a mano.
- **Paneles** (prioridad 300): se cierran solos al contraer la isla.
- **Temporizador** (1 / 5 / 10 / 25 min): pausa, +1 min, cancelar. Al terminar pasa a alerta (prioridad urgente), con sonido de Windows y notificación temporal. *+1 min* desde la alerta lo reinicia.
- **Cronómetro**: pausa / seguir, vueltas (se muestran las últimas 3), cerrar.
- **Recordatorios**: se crean en una ventana aparte (la isla no toma el foco, así que no se puede escribir en ella), "en N minutos" o "a las HH:mm" (si ya pasó, es mañana). Se guardan en `%AppData%\NexusIsland\reminders.json`. Al vencer: alerta con sonido, *Listo* o *Posponer 10 min*. Si la PC estaba apagada, avisan al volver ("Vencía hoy a las…").
- **Portapapeles**: últimos 20 textos; clic para volver a copiar; *Borrar todo*.
  - **Solo en memoria**: nunca se guarda en disco y se borra al cerrar la isla. El contenido nunca va al log.
  - **Respeta a los gestores de contraseñas**: lo marcado con `ExcludeClipboardContentFromMonitorProcessing` o `CanIncludeInClipboardHistory = 0` no se registra (igual que Win+V).
  - Lee 60 ms después del aviso de Windows, para no chocar con la app que está copiando.
- Cada herramienta es un **módulo independiente** (activable desde la Configuración). Los cuatro comparten un mismo diccionario de plantillas, que se carga una sola vez.
- Las alertas se actualizan cuatro veces por segundo internamente, pero solo redibujan cuando cambia el texto (una vez por segundo).

**Verificado (Release, UI Automation)**

| Prueba | Resultado |
|---|---|
| Tests unitarios (temporizador, cronómetro, historial, recordatorios, ranking de actividades + anteriores) | ✅ 76/76 |
| Panel de herramientas con 7 botones; cada uno abre lo suyo | ✅ |
| Temporizador: corre, barra de progreso, puntitos de página (temporizador + reloj) | ✅ captura |
| Temporizador termina a los 60 s exactos → "¡Tiempo!" con notificación temporal | ✅ |
| Recordatorio creado desde la ventana ("te aviso a las 10:31") → alerta a las 10:31:39 | ✅ |
| Recordatorio vencido con la isla cerrada → avisa al arrancar como "atrasado"; *Listo* lo borra | ✅ |
| Portapapeles: registra textos, **ignora el contenido marcado como privado**, vuelve a copiar con clic | ✅ |
| 20 copias seguidas desde otra app con la isla escuchando | ✅ 0 fallos (antes de esperar 60 ms, fallaba alguna) |
| Cronómetro: arranca y marca vueltas | ✅ |
| Cambio de páginas con la isla expandida | ✅ (usado con el mouse real) |
| RAM / CPU con los 5 módulos activos y una alerta en pantalla | 35,6 MB / 0,23 % de un núcleo |

**Limitaciones conocidas**

- El sonido de las alertas es el sonido de sistema de Windows (Exclamación / Asterisco). Si están en silencio en Windows, no suena.
- El temporizador y el cronómetro no sobreviven a cerrar la app (los recordatorios sí).
- La rueda del mouse para cambiar de página requiere "Desplazar ventanas inactivas al pasar el mouse" (activado por defecto en Windows 10/11).
- El historial del portapapeles es solo de texto (no imágenes ni archivos).
- Un solo temporizador a la vez: iniciar otro reemplaza al actual.

### Etapa 6 — Monitoreo del sistema ✅ (17/11/2026, v0.6.0)

**Qué ofrece Windows (relevado en esta PC antes de implementar)**

| Dato | Fuente | Estado |
|---|---|---|
| CPU | `GetSystemTimes` | ✅ exacto |
| RAM | `GlobalMemoryStatusEx` | ✅ exacto |
| Batería | `GetSystemPowerStatus` | ✅ (en PCs de escritorio no se muestra) |
| Conectividad y nombre de red | `Windows.Networking.Connectivity` (WinRT) | ✅ con aviso de cambios |
| Velocidad de red | bytes de las interfaces **con puerta de enlace** | ✅ (así no se duplican los adaptadores virtuales de VMware/Hyper-V) |
| Temperatura | zonas térmicas ACPI vía PDH con nombres en inglés (`PdhAddEnglishCounter`) | ⚠️ **aproximada**: no es exactamente la del procesador. El sensor de WMI da "Acceso denegado" sin administrador; la temperatura real de la CPU solo la dan programas con driver propio (HWiNFO). Los nombres de contadores vienen traducidos según el idioma de Windows, por eso se usan los nombres en inglés |

**Implementado**

- **Panel "Sistema"** (botón ⊞ → Sistema): CPU, memoria (con barras), red (nombre, ↓/↑), batería y temperatura aprox. Se actualiza una vez por segundo **solo mientras está abierto**. La temperatura se lee cada 5 s, porque es lo más caro.
- **Alertas en segundo plano** (revisión cada 3 s, **0,14 ms por revisión**):
  - CPU ≥ 90 % durante 30 s, indicando qué app la usa (sumando todos sus procesos: Chrome son decenas).
  - Memoria ≥ 90 % durante 10 s, indicando qué app la usa.
  - Batería ≤ 20 % y ≤ 10 % sin enchufar; se rearman al enchufar.
  - Sin Internet durante 5 s: queda en la isla mientras dure. Al volver, aviso breve "Conectado (red)".
  - Margen de 10 puntos para dar por terminada una alerta (no "parpadea" cerca del umbral). Los picos cortos no avisan. Un portal cautivo (Wi-Fi que pide iniciar sesión) no cuenta como "sin Internet".
  - Botones *Entendido* y *Ver sistema*. Cada tipo de alerta se activa o desactiva en la Configuración.
- **La propia isla nunca se señala** como la app que más consume.
- No hay alertas de temperatura: con un sensor aproximado darían falsas alarmas.

**Verificado (Release; UI Automation con `NEXUS_PIN_EXPANDED` y `NEXUS_MONITOR_TEST`)**

| Prueba | Resultado |
|---|---|
| Tests unitarios (CPU, velocidad de red, formatos, reglas de alerta: sostenido, margen, picos, batería, sin Internet, portal cautivo, desactivar) | ✅ 87/87 |
| Alertas de CPU y memoria con datos reales; detalle con la app responsable ("Google Chrome") | ✅ captura |
| *Ver sistema* abre el panel: 56 °C aprox., CPU 22 %, 14,0 de 15,2 GB, "WiFi Talleres" ↓/↑, batería 100 % enchufada | ✅ captura |
| Con la PC bloqueada, la isla se aparta (la pantalla de bloqueo cuenta como pantalla completa) | ✅ |
| Costo de la vigilancia: medido desde adentro | 0,14 ms por revisión (≈0,005 % de un núcleo) |

**Limitaciones conocidas**

- **Temperatura aproximada** (ver arriba). En PCs sin zonas térmicas ACPI no aparece.
- La alerta "Sin Internet" no se probó en vivo (no se cortó el Wi-Fi de la PC en uso). La cubren los tests de las reglas.
- La velocidad de red suma las interfaces con puerta de enlace: con una VPN activa el tráfico puede contarse dos veces.
- Identificar la app que más CPU usa mide durante 1 s; algunos procesos del sistema no se pueden leer sin administrador y se ignoran.
- **En esta PC la memoria suele estar cerca del 90 %** (13,8–14 de 15,2 GB en las pruebas), así que la alerta de memoria puede aparecer seguido. Avisa una sola vez por episodio y se puede desactivar.

## Próximas etapas

7. Empaquetado MSIX + notificaciones de Windows.
8. Tareas en segundo plano (descargas, instalaciones) por heurística.
9. Producto: accesibilidad completa, idiomas, actualizaciones, firma y distribución.
