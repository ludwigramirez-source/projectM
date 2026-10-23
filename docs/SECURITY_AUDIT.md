# Auditoría de seguridad: ProjectM

| | |
|---|---|
| **Tipo** | Revisión defensiva de código estático + configuración (sin pruebas activas contra servicios) |
| **Rama / commit** | `vercel` @ `05e30e1` |
| **Fecha** | 2026-10-23 |
| **Alcance** | Backend `backend/src/**` (ASP.NET Core 9, EF Core 9/Npgsql, Identity+JWT, SignalR), frontend `frontend/src/**` (React 19), configuración de despliegue (`docker-compose.yml`, `render.yaml`, `frontend/vercel.json`, `frontend/nginx.conf`, Dockerfiles), historial de git y dependencias |
| **Herramientas** | Lectura de código, `git log -S/-p` (búsqueda de secretos), `npm audit`, `dotnet list package --vulnerable/--outdated` |
| **Metodología** | Clasificación OWASP Top 10 (2021); severidad con criterio CVSS 3.1 aproximado |

> Las rutas de backend abreviadas como `Api/…` corresponden a `backend/src/ProjectM.Api/…`.
> "**Requiere verificación en producción**" = el riesgo real depende del entorno (Render, Vercel, Supabase) y no puede confirmarse solo con el código.

---

## 1. Resumen ejecutivo

**Postura general: buena para un MVP, con varios endurecimientos pendientes antes de abrirlo a más usuarios.**

ProjectM tiene una base de seguridad sólida en los puntos donde suelen fallar las aplicaciones de este tipo:

- **Sin inyección SQL.** Todo el acceso a datos es LINQ parametrizado. No hay SQL crudo, y la búsqueda `ILIKE` escapa los comodines (`Api/Infrastructure/Persistence/Search.cs:7-8`).
- **Sin IDOR.** Se revisaron los 92 endpoints de `Features/**`. Cada ID que llega por ruta, cuerpo o query se resuelve a su proyecto y se valida contra el proyecto autorizado por `ProjectGuard`.
- **Sesión bien diseñada.**
  - El access token JWT vive solo en memoria y dura 15 minutos.
  - El refresh token es rotativo, se guarda hasheado (SHA-256) y va en una cookie `HttpOnly; Secure; SameSite=Strict` con path restringido.
  - Hay detección de reutilización.
  - El `security stamp` se valida en cada petición, así que una desactivación o un cambio de rol o contraseña cortan las sesiones al instante.
- **Sin XSS evidente.** No hay `dangerouslySetInnerHTML`, `innerHTML` ni `eval`. Las menciones se pintan como texto de React.
- **Adjuntos razonablemente aislados.**
  - La clave de almacenamiento es un GUID, así que no hay path traversal.
  - Hay lista blanca de extensiones, sin SVG ni HTML.
  - El bucket es privado y la descarga siempre pasa por la API con `Content-Disposition: attachment`.
- **Sin secretos en el repositorio.** El historial de git no contiene credenciales reales, y `npm audit` reporta 0 vulnerabilidades.

**Riesgos principales:**

1. **Credenciales de ejemplo utilizables** (`.env.example`). Si un despliegue con Docker Compose copia la plantilla sin cambiarla, quedan un Admin y siete usuarios demo con contraseñas públicas. Es el único hallazgo **Alto**.
2. **El límite de intentos de login se puede eludir.** La API confía en `X-Forwarded-For` sin restringir qué proxies son de confianza, y en Render también es accesible directamente.
3. **Faltan controles de abuso** fuera del login. No hay límites en comentarios, menciones, subidas de archivos ni el dashboard.
4. **Defensa en profundidad incompleta:**
   - no hay **Content-Security-Policy**;
   - la conexión a Postgres usa TLS **sin verificar el certificado**;
   - la rotación del refresh token tiene una **condición de carrera**.
5. **Protección de datos (Habeas Data, Ley 1581 de 2012).** Los archivos subidos nunca se borran físicamente. Los refresh tokens, junto con su IP, se conservan indefinidamente. Los ex-miembros siguen recibiendo notificaciones con contenido de sus antiguos proyectos.
6. **Plataforma.** .NET 9 es una versión STS cuyo soporte termina en **noviembre de 2026** (⚠️ verificar la fecha exacta en el ciclo de vida de .NET). Después de esa fecha no habrá parches de seguridad.

**Conteo de hallazgos:**

| Crítica | Alta | Media | Baja | Total |
|:-:|:-:|:-:|:-:|:-:|
| **0** | **1** | **10** | **24** | **35** |

---

## 2. Tabla de hallazgos

| ID | Severidad | OWASP 2021 | Archivo:línea | Riesgo | Remediación |
|---|---|---|---|---|---|
| **SEC-01** | **Alta** | A07, A05 | `.env.example:4,7,10-11,14-15`; `docker-compose.yml:27,30-33` | Valores de ejemplo que cumplen la política de contraseñas (Admin, demo, JWT, Postgres) y `SEED_DEMO=true` por defecto. Si se despliega con la plantilla, cualquiera entra como Admin. *Requiere verificación en producción (Compose)* | Plantilla vacía, `SEED_DEMO=false` y negarse a arrancar con valores conocidos fuera de Development (ver §3) |
| SEC-02 | Media | A07, A05 | `Api/Program.cs:85-87`; `render.yaml:43`; `Api/Infrastructure/ServiceRegistration.cs:106-117` | `KnownNetworks`/`KnownProxies` vacíos con `ForwardLimit=2`: falsificando `X-Forwarded-For` (sobre todo llamando directo a `*.onrender.com`) se evade el límite de 10 intentos/min del login y se falsea la IP auditada. *Requiere verificación en producción* | Limitador global y por cuenta, además del de IP; cerrar el acceso directo a `/api/v1/auth/*` con un encabezado secreto del proxy; fijar `KnownNetworks` si Render publica sus rangos |
| SEC-03 | Media | A07 | `Api/Infrastructure/Auth/TokenService.cs:38-52` | La rotación del refresh token lee, verifica y actualiza sin atomicidad: dos peticiones simultáneas obtienen dos sucesores válidos (se evade la detección de reutilización), o se revocan todas las sesiones de un usuario con varias pestañas | `UPDATE … WHERE RevokedAt IS NULL` condicional y aceptar solo si afecta 1 fila; periodo de gracia corto |
| SEC-04 | Media | A07 | `Api/Features/Auth/AuthEndpoints.cs:61,64-66` | Enumeración de cuentas: un usuario inexistente responde sin calcular el hash (diferencia de tiempo), y solo las cuentas reales responden `423 CUENTA_BLOQUEADA`. Además, cualquiera puede bloquear una cuenta conocida con 5 intentos | Hash ficticio cuando el usuario no existe; respuesta idéntica (401) también al bloquear; retraso progresivo en vez de bloqueo duro |
| SEC-05 | Media | A05 | `frontend/vercel.json:10-19`; `frontend/nginx.conf:18-20`; `frontend/index.html:16-25` | No hay `Content-Security-Policy` ni `Permissions-Policy`. Un XSS futuro podría robar el access token y usar `/auth/refresh` desde el mismo origen | CSP estricta con el hash del script inline (o moverlo a un archivo), `connect-src` limitado a la API, `frame-ancestors 'none'`; probarla primero en modo Report-Only |
| SEC-06 | Media | A02 | `Api/Infrastructure/Persistence/ConnectionStrings.cs:15-17,28,32-39` | `SslMode.Require` cifra pero no valida el certificado (posible MITM). La URI permite rebajar a `sslmode=disable`, y el formato clave=valor no fuerza SSL. *Requiere verificación en producción* | `SslMode.VerifyFull` con la CA raíz de Supabase; imponer un SSL mínimo fuera de Compose/Development |
| SEC-07 | Media | A04 | `Api/Infrastructure/ServiceRegistration.cs:101-130`; `Api/Features/Tasks/TaskSupportEndpoints.cs:81`; `Api/Features/Collaboration/CollaborationEndpoints.cs:38`; `Api/Infrastructure/Events/DomainEventDispatcher.cs:148-151`; `Api/Features/Dashboard/DashboardEndpoints.cs:53-89` | Solo login y refresh tienen límite de frecuencia. Un Miembro puede subir archivos de 20 MB sin tope (costo y cuota de Supabase), inundar de comentarios con 20 menciones cada uno o saturar el dashboard | Políticas por usuario (`sub`) para escrituras y subidas; cuota de bytes por proyecto; caché corta del dashboard |
| SEC-08 | Media | A01 | `Api/Features/Projects/ProjectEndpoints.cs:268-276`; `Api/Infrastructure/Events/DomainEventDispatcher.cs:123-124,150-151`; `Api/Infrastructure/Notifications/DueDateNotifier.cs:45-49` | Al quitar a un miembro no se le desasignan tareas ni riesgos, y las notificaciones no verifican que el destinatario aún vea el proyecto. El ex-miembro sigue recibiendo títulos de tareas y extractos de comentarios | Filtrar destinatarios por permiso vigente antes de `Notify`; desasignar al quitar al miembro |
| SEC-09 | Media | A01 | `Api/Features/Tasks/TaskSupportEndpoints.cs:76-77,238-254`; `Api/Infrastructure/Persistence/Configurations/TaskConfigurations.cs:67` | Las etiquetas son globales: cualquier Gestor puede renombrarlas o borrarlas, con borrado en cascada de `TaskTags` en proyectos ajenos y **cerrados**, sin ActivityLog. Rompe la regla "Cerrado = solo lectura" | `PUT`/`DELETE /tags` solo para Admin, o 409 si la etiqueta está en uso fuera de los proyectos del usuario; a medio plazo, etiquetas por proyecto o espacio |
| SEC-10 | Media | A04 (Habeas Data) | `Api/Infrastructure/Storage/FileStorage.cs:8`; `Api/Features/Tasks/TaskSupportEndpoints.cs:133-134,284-297,316` | `IFileStorage.DeleteAsync` nunca se invoca. Borrar un adjunto es solo lógico, y borrar una lista elimina las filas pero deja los archivos huérfanos. Una subida fallida también deja huérfano el archivo. No se puede atender una solicitud de supresión de datos | Borrar los archivos al eliminar listas o si falla la subida; job de purga de adjuntos eliminados con más de N días; política de retención documentada |
| SEC-11 | Media | A06 | `backend/Directory.Build.props:3`; `Api/Dockerfile:2,11` | .NET 9 (STS) termina soporte en noviembre de 2026 (⚠️ verificar fecha). Después no habrá parches para ASP.NET Core, Kestrel ni SignalR | Planificar la migración a .NET 10 (LTS) y a los paquetes 10.x (`dotnet list package --outdated` ya los ofrece) |
| SEC-12 | Baja | A09, A02 | `Api/Infrastructure/ServiceRegistration.cs:65-71`; `Api/Infrastructure/Auth/TokenService.cs:62`; `frontend/src/app/realtime.tsx:28` | El JWT viaja en `?access_token=` hacia SignalR y lleva el claim `email`. Si los logs de Render guardan la URL, exponen un token válido 15 min y el correo. *Requiere verificación en producción* | Confirmar si Render registra la query; quitar `email` del JWT; ticket de hub de un solo uso |
| SEC-13 | Baja | A01, A07 | `Api/Program.cs:145`; `Api/Infrastructure/Realtime/NotificationsHub.cs:30-42` | La conexión SignalR y la pertenencia a `project:{id}` sobreviven a la desactivación, la expulsión o el cambio de rol. `JoinProject` usa el rol del token original | `CloseOnAuthenticationExpiration = true`; en `JoinProject`, revalidar contra la base; cerrar las conexiones al desactivar |
| SEC-14 | Baja | A07 | `Api/Infrastructure/Auth/TokenService.cs:104` | Expiración deslizante sin tope: una sesión renovada al menos cada 7 días no expira nunca | Guardar el inicio de la sesión (o un `FamilyId`) y exigir reautenticación tras N días |
| SEC-15 | Baja | A04 (Habeas Data) | `Api/Infrastructure/Auth/TokenService.cs:25,51,105` | Los refresh tokens nunca se purgan y conservan la IP de creación indefinidamente | Job de purga de tokens vencidos o revocados; política de retención |
| SEC-16 | Baja | A07 | `Api/Features/Auth/AuthEndpoints.cs:51,110-119` | `change-password` no tiene límite de frecuencia ni cuenta para el bloqueo: permite adivinar la contraseña actual desde una sesión robada | `RequireRateLimiting`; verificar con `CheckPasswordSignInAsync(..., lockoutOnFailure: true)` |
| SEC-17 | Baja | A02, A07 | `Api/Infrastructure/ServiceRegistration.cs:29-40` | PBKDF2 con las iteraciones por defecto de Identity (100 000), por debajo de las ~210 000 que recomienda OWASP para SHA-512. No se rechazan contraseñas comunes ni filtradas | `PasswordHasherOptions.IterationCount = 210_000`; un `IPasswordValidator` con lista de contraseñas comunes; mínimo de 12 caracteres |
| SEC-18 | Baja | A05 | `frontend/nginx.conf:10,44-50,59-62`; `docker-compose.yml:48` | Un `add_header` dentro de un `location` anula la herencia del `server`: `/sw.js`, el manifest y `/assets/` pierden `nosniff`, XFO y Referrer-Policy. `server_tokens` está activo y Compose publica HTTP sin TLS | Un snippet de cabeceras incluido en cada `location`; `server_tokens off`; terminador TLS con HSTS si se expone fuera de localhost |
| SEC-19 | Baja | A05 | `Api/Program.cs:99-114`; `Api/appsettings.json:9` | La API no aplica `UseHsts`, no redirige a HTTPS, no envía cabeceras propias y tiene `AllowedHosts: "*"`. *Requiere verificación en producción (Render)* | `UseHsts()` fuera de Development; middleware de cabeceras (`nosniff`, `no-store` en descargas); `AllowedHosts` con los hosts reales |
| SEC-20 | Baja | A09 (Habeas Data) | `Api/Infrastructure/Seed/IdentitySeeder.cs:39` | El correo del Admin queda en los logs de la plataforma | Registrar el Id o el correo enmascarado; borrar las variables `Seed__Admin*` tras el primer arranque |
| SEC-21 | Baja | A01 | `Api/Features/Tasks/TaskSupportEndpoints.cs:74-75,223-228`; `Api/Features/Users/UserEndpoints.cs:115-116,141-142` | `POST /tags` usa una regla negativa ("no Observador") en lugar de una lista de roles permitidos. `GET /tags` expone las etiquetas de todos los proyectos. Crear un usuario y asignarle rol no es transaccional (puede quedar sin rol) | Política positiva de roles; transacción en la creación y el cambio de rol |
| SEC-22 | Baja | A04 | `Api/Features/Tasks/TaskEndpoints.cs:146,160` | Crear una tarea con `status: Completada` se salta la regla `CAMPOS_REQUERIDOS` | Validar los campos obligatorios antes de `ChangeStatus` en Create |
| SEC-23 | Baja | A01 (Habeas Data) | `Api/Features/Users/UserEndpoints.cs:15,59,90-101` | `GET /users/lookup` entrega nombre y correo de todos los usuarios activos a cualquier autenticado, incluido el Observador | Restringirlo a Admin y Gestor, o limitarlo a compañeros de proyecto y sin correo |
| SEC-24 | Baja | A01 | `Api/Features/Spaces/SpaceEndpoints.cs:52,57-64,134-139` | `GET /clients` y `GET /spaces` listan todos los clientes y espacios sin filtrar por proyectos visibles | Filtrar con `VisibleProjects()` para roles no gestores |
| SEC-25 | Baja | A01 | `Api/Infrastructure/Authorization/ProjectAuthorization.cs:50`; `Api/Features/Users/UserEndpoints.cs:138-144` | Un gestor degradado a Miembro u Observador conserva `EditTasks` en los proyectos que gestionaba; la matriz de pruebas no cubre el caso | Exigir el rol en `isManager`, o bloquear la degradación sin reasignar antes los proyectos; añadir casos a `PermissionMatrixTests` |
| SEC-26 | Baja | A01 | `Api/Features/Tasks/TaskSupportEndpoints.cs:312-318` | Cualquier editor puede borrar adjuntos ajenos, sin ActivityLog (los comentarios sí exigen autor o gestor) | Exigir autor o `CanManage` y registrar `AttachmentDeleted` |
| SEC-27 | Baja | A04, A08 | `Api/Infrastructure/Storage/FileStorage.cs:22-26`; `Api/Features/Tasks/TaskSupportEndpoints.cs:274-281` | Solo se valida la extensión: no se comprueban las firmas del contenido (magic bytes), no hay antivirus, y se aceptan `.doc/.xls/.ppt` (con macros) y `.zip` | Validar las firmas; escanear con ClamAV o similar; retirar los formatos con macros |
| SEC-28 | Baja | A03 | `Api/Features/Tasks/TaskSupportEndpoints.cs:274-275` | Los nombres de archivo admiten caracteres de control o bidi (U+202E), lo que permite disfrazar la extensión | Quitar las categorías Unicode `Cc`/`Cf`/`Zl`/`Zp` y normalizar a NFC |
| SEC-29 | Baja | A03 | `Api/Program.cs:48`; `Api/Features/Tasks/TaskDtos.cs:100-138`; `Api/Features/Projects/ProjectDtos.cs:122-139` | El convertidor de enums acepta enteros y faltan `IsInEnum()`: se persisten valores inválidos (p. ej. `"99"`) | `JsonStringEnumConverter(allowIntegerValues: false)` e `IsInEnum()` |
| SEC-30 | Baja | A04 | `Api/Features/Tasks/TaskSupportEndpoints.cs:61,140-149`; `Api/Infrastructure/Pagination/Paging.cs:12,20`; `Api/Infrastructure/Events/DomainEventDispatcher.cs:88`; `Api/Features/Tasks/TaskEndpoints.cs:315-316` | Entradas que provocan un 500: `reorder` sin `ids`, desbordamiento de `page`, motivo de reapertura de más de 971 caracteres, `move` con la propia tarea como vecina | Validadores (`NotNull`, topes), `Math.Clamp` y truncado del mensaje |
| SEC-31 | Baja | A04 | `Api/Features/Projects/ProjectEndpoints.cs:365`; `Api/Features/Tasks/TaskEndpoints.cs:298-318`; `Api/Infrastructure/Authorization/ProjectAuthorization.cs:89-97` | La concurrencia optimista se omite si no llega `rowVersion`; el reordenamiento fraccional y el cierre son comprobar-y-actuar (posibles carreras) | `rowVersion` obligatorio en `PUT`; rebalanceo con un único UPDATE; bloqueo de fila (`FOR UPDATE`) al cerrar |
| SEC-32 | Baja | A04 | `Api/Features/Auth/AuthEndpoints.cs:22,31`; `Api/Features/Users/UserEndpoints.cs:27,44` | Las contraseñas no tienen longitud máxima: entradas enormes elevan el costo del hash | `MaximumLength(128)` |
| SEC-33 | Baja | A04 | `Api/Infrastructure/Notifications/DueDateNotifier.cs:59` | Comprobar y luego insertar sin índice único: con más de una instancia se duplican los avisos de vencimiento. *Requiere verificación en producción* | Índice único parcial o `pg_try_advisory_lock` |
| SEC-34 | Baja | A05, A02 | `Api/Infrastructure/ServiceRegistration.cs:52-61`; `Api/Program.cs:92-96`; `.gitignore:2-3`; `Api/Infrastructure/Storage/S3FileStorage.cs:32-37` | Endurecimientos menores: el JWT no fija `ValidAlgorithms`; CORS con `AllowCredentials` innecesario; `.gitignore` no cubre `.env.*` ni `frontend/.env`; `S3:ServiceUrl` acepta `http`; las claves de DataProtection no se persisten | Fijar HS256; quitar `AllowCredentials`; añadir `.env.*` y `!.env.example` al `.gitignore`; exigir `https`; `PersistKeysToDbContext` si llegan los enlaces por correo |
| SEC-35 | Baja | A06, A08 | `backend/tests/ProjectM.IntegrationTests/ProjectM.IntegrationTests.csproj:14`; `frontend/Dockerfile:1,8`; `Api/Dockerfile:2,11` | `Microsoft.AspNetCore.SignalR.Common 9.0.10` (transitivo, solo en pruebas) tiene un aviso **High** (GHSA-334f-vvp4-7g6p). Las imágenes base usan tags flotantes (`aspnet:9.0`, `node:24-alpine`), y `nginx:1.27` es una rama sin mantenimiento. *Requiere verificación en producción*: confirmar el parche del runtime en la imagen desplegada | Subir los paquetes de prueba; fijar las imágenes por digest y renovarlas periódicamente (Dependabot o Renovate); usar nginx `stable` (1.28 o posterior) |

---

## 3. Detalle del hallazgo Alto

### SEC-01: Credenciales y semillas de ejemplo utilizables en producción (Alta)

**Severidad Alta** (CVSS aprox. 8.1, AV:N/AC:L/PR:N/UI:N/C:H/I:H). Un atacante remoto y sin cuenta tomaría control total como Admin usando valores públicos del repositorio. La severidad queda **condicionada**: solo aplica si alguien despliega con la plantilla sin cambiarla (*requiere verificación en producción*).

**Evidencia:**
- `.env.example:11`: `SEED_ADMIN_PASSWORD=Admin_Cambiar_123!`. Cumple la política: 10 o más caracteres, con mayúscula, minúscula y dígito (`Api/Infrastructure/ServiceRegistration.cs:31-35`).
- `.env.example:14-15`: `SEED_DEMO=true` y `SEED_DEMO_PASSWORD=Demo_Cambiar_123!`. Crea siete usuarios `@projectm.demo` (dos Gestores) con una contraseña compartida y pública.
- `.env.example:7`: el `JWT_KEY` de ejemplo tiene unos 67 caracteres, así que pasa la validación de longitud (`ServiceRegistration.cs:26`). La falsificación de tokens queda mitigada por la comprobación del `security_stamp` (`ServiceRegistration.cs:73-89`), pero ese pasa a ser el único control.
- `.env.example:4`: la contraseña de Postgres es predecible. Por ahora no se publica el puerto de `db`.
- El flujo documentado (`CLAUDE.md`, `README.md`, `docs/SETUP.md`) es `cp .env.example .env` seguido de `docker compose up`.

**Impacto:**
- El despliegue en la nube actual (Render) no se ve afectado de forma directa: allí `Jwt__Key` se genera automáticamente y los `Seed__*` se cargan a mano (`render.yaml:25-35`).
- Cualquier despliegue autohospedado con Compose que no edite el `.env` publicaría:
  - un Admin con credenciales conocidas;
  - datos demo, que no deberían existir en producción.

**Fix 1: plantilla sin valores utilizables (`.env.example`)**

```dotenv
# Genera valores propios: openssl rand -base64 48
POSTGRES_PASSWORD=
JWT_KEY=
SEED_ADMIN_EMAIL=
SEED_ADMIN_PASSWORD=
# Solo para demos locales
SEED_DEMO=false
SEED_DEMO_PASSWORD=
```

**Fix 2: negarse a arrancar con valores conocidos fuera de Development (`Api/Program.cs`, antes de `builder.Build()`)**

```csharp
string[] knownPlaceholders =
[
    "reemplazar-por-una-clave-aleatoria-larga-de-al-menos-32-caracteres",
    "Admin_Cambiar_123!", "Demo_Cambiar_123!", "Cambiar_Esta_Clave_123"
];

if (!builder.Environment.IsDevelopment())
{
    foreach (var key in new[] { "Jwt:Key", "Seed:AdminPassword", "Seed:DemoPassword" })
        if (knownPlaceholders.Contains(config[key]))
            throw new InvalidOperationException($"{key} usa el valor de ejemplo del repositorio; genera uno propio.");

    if (config.GetValue<bool>("Seed:Demo") && !config.GetValue<bool>("Seed:AllowDemoInProduction"))
        throw new InvalidOperationException("Seed:Demo está desactivado fuera de Development.");
}
```

**Fix 3 (recomendado):** forzar el cambio de contraseña del Admin sembrado en el primer inicio de sesión. Una opción es un flag `MustChangePassword` en `AppUser`, que el login devuelva y el SPA respete. También hay que borrar `Seed__AdminPassword` de la plataforma después del primer arranque.

---

## 4. Detalle y fixes de los hallazgos Medios

Aunque la consigna solo exige detalle para Críticos y Altos, se incluyen los fixes de los Medios porque son los siguientes en prioridad.

### SEC-02: Evasión del rate limit del login con `X-Forwarded-For` (Media, *requiere verificación en producción*)

**CVSS aprox. 5.3.** Remoto y sin autenticación. El bloqueo por cuenta (5 intentos) limita el impacto a *password spraying* y a bloqueos masivos.

```csharp
// Api/Infrastructure/ServiceRegistration.cs: además de la partición por IP
o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    http.Request.Path.StartsWithSegments("/api/v1/auth/login")
        ? RateLimitPartition.GetFixedWindowLimiter("login-global",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
        : RateLimitPartition.GetNoLimiter("otros"));

// Api/Program.cs: solo aceptar /api/v1/auth/* si llega desde el proxy (Vercel inyecta el encabezado)
app.Use(async (ctx, next) =>
{
    var secret = config["Proxy:SharedSecret"];
    if (!string.IsNullOrEmpty(secret) && ctx.Request.Path.StartsWithSegments("/api/v1/auth")
        && !CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Proxy-Secret"].ToString()),
               Encoding.UTF8.GetBytes(secret)))
    { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
    await next();
});
```

Hay que agregar también un limitador por correo normalizado dentro de `Login`. Verificación: registrar temporalmente la cadena real de `X-Forwarded-For` en las dos rutas (vía Vercel y directa a Render), anonimizando las IP.

### SEC-03: Carrera en la rotación del refresh token (Media)

**CVSS aprox. 4.8** (AC:H). Puede bifurcar una sesión robada sin que se detecte, o expulsar al usuario legítimo.

```csharp
// Api/Infrastructure/Auth/TokenService.cs: reemplaza las líneas 48-52
var (raw, replacement) = NewRefreshToken(existing.UserId, ip, now);
await using var tx = await db.Database.BeginTransactionAsync(ct);
var won = await db.RefreshTokens
    .Where(x => x.Id == existing.Id && x.RevokedAt == null)
    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now)
                              .SetProperty(x => x.ReplacedById, replacement.Id), ct);
if (won != 1) { await tx.RollbackAsync(ct); return null; }   // otra petición ya lo rotó
db.RefreshTokens.Add(replacement);
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
return (existing.User, raw, replacement);
```

Recomendado: un periodo de gracia de unos 30 s para el token recién reemplazado, que no dispare `RevokeAllAsync`. Así las pestañas múltiples y la PWA no cierran todas las sesiones.

### SEC-04: Enumeración de cuentas y bloqueo malicioso (Media)

**CVSS aprox. 5.3** (C:L). Confirma qué correos (datos personales) tienen cuenta.

```csharp
// Api/Features/Auth/AuthEndpoints.cs:58-67
var user = await users.FindByEmailAsync(req.Email.Trim());
if (user is null || !user.IsActive)
{
    hasher.VerifyHashedPassword(DummyUser, DummyHash, req.Password); // tiempo constante
    throw invalid;
}
var result = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
if (!result.Succeeded) throw invalid;   // mismo 401 también si IsLockedOut
```

Hay que inyectar `IPasswordHasher<AppUser>`, y `DummyHash` debe generarse una vez al arrancar. El aviso de "cuenta bloqueada" puede ir por otro canal, como un correo al titular o el panel de Admin.

### SEC-05: Sin CSP ni Permissions-Policy (Media)

**Defensa en profundidad** (C:H condicionado a un XSS, AC:H).

```json
// frontend/vercel.json → headers[0].headers (equivalente en nginx.conf)
{ "key": "Content-Security-Policy",
  "value": "default-src 'self'; script-src 'self' 'sha256-<hash-del-script-de-tema>'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self' https://projectm-api.onrender.com wss://projectm-api.onrender.com; worker-src 'self'; manifest-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'" },
{ "key": "Permissions-Policy", "value": "camera=(), microphone=(), geolocation=(), payment=()" }
```

Hay que desplegarla primero como `Content-Security-Policy-Report-Only` y vigilar las violaciones. Tailwind o dnd-kit pueden requerir `style-src 'unsafe-inline'`.

### SEC-06: TLS a Postgres sin verificación de certificado (Media, *requiere verificación en producción*)

**CVSS aprox. 5.9** (AC:H por la posición de red requerida, C:H/I:H).

```csharp
// Api/Infrastructure/Persistence/ConnectionStrings.cs: al final de NormalizePostgres
var b = new NpgsqlConnectionStringBuilder(result);
var isLocal = b.Host is "db" or "localhost" or "127.0.0.1";
if (!isLocal)
{
    if (b.SslMode < SslMode.VerifyFull) b.SslMode = SslMode.VerifyFull;
    b.RootCertificate ??= Environment.GetEnvironmentVariable("PG_ROOT_CERT") ?? "/app/certs/supabase-ca.crt";
}
return b.ConnectionString;
```

La CA de Supabase se descarga desde *Project Settings → Database → SSL Configuration* y se copia a la imagen en `backend/src/ProjectM.Api/Dockerfile`.

### SEC-07: Sin límite de frecuencia en escrituras, subidas ni menciones (Media)

**CVSS aprox. 4.3** (PR:L, A:L, más costo).

```csharp
// Api/Infrastructure/ServiceRegistration.cs
o.AddPolicy("writes", http => RateLimitPartition.GetTokenBucketLimiter(
    http.User.FindFirstValue("sub") ?? http.Connection.RemoteIpAddress?.ToString() ?? "anon",
    _ => new TokenBucketRateLimiterOptions { TokenLimit = 30, TokensPerPeriod = 30,
         ReplenishmentPeriod = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
o.AddPolicy("uploads", http => RateLimitPartition.GetFixedWindowLimiter(
    http.User.FindFirstValue("sub") ?? "anon",
    _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(10) }));

// TaskSupportEndpoints.cs:81 → .RequireRateLimiting("uploads")
// CollaborationEndpoints.cs:38 (comentarios), TaskEndpoints.cs:22 (tareas), TaskSupportEndpoints.cs:75 (tags) → .RequireRateLimiting("writes")
```

`UseRateLimiter` corre después de `UseAuthentication` (`Program.cs:112-113`), así que la partición por `sub` funciona.

### SEC-08: Ex-miembros siguen recibiendo notificaciones (Media)

**CVSS aprox. 4.3** (C:L sostenida hacia un usuario sin acceso).

```csharp
// Api/Infrastructure/Events/DomainEventDispatcher.cs: antes de cada Notify(uid, ...)
async Task<bool> CanSeeAsync(Guid projectId, Guid userId) =>
    await db.Projects.AnyAsync(p => p.Id == projectId &&
        (p.ManagerId == userId || p.Members.Any(m => m.UserId == userId)), ct)
    || await db.UserRoles.AnyAsync(ur => ur.UserId == userId &&
        db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Admin), ct);

// Api/Features/Projects/ProjectEndpoints.cs:274: al quitar al miembro
await db.Tasks.Where(t => t.ProjectId == id && t.AssigneeId == userId)
    .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssigneeId, (Guid?)null), ct);
await db.Risks.Where(r => r.ProjectId == id && r.OwnerId == userId)
    .ExecuteUpdateAsync(s => s.SetProperty(r => r.OwnerId, (Guid?)null), ct);
```

Hay que aplicar el mismo filtro en `DueDateNotifier`.

### SEC-09: Etiquetas globales alteran proyectos ajenos o cerrados (Media)

**CVSS aprox. 4.3** (PR:L, I:L en proyectos fuera del alcance del usuario).

```csharp
// Api/Features/Tasks/TaskSupportEndpoints.cs:75-77
t.MapPost("/", CreateTag).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Gestor, Roles.Miembro))
                         .Validate<SaveTagRequest>();
t.MapPut("/{id:guid}", UpdateTag).RequireAuthorization(Policies.Admin).Validate<SaveTagRequest>();
t.MapDelete("/{id:guid}", DeleteTag).RequireAuthorization(Policies.Admin);
```

### SEC-10: Archivos nunca eliminados (Media, Habeas Data)

**CVSS aprox. 4.0.** No lo explota un atacante, pero impide cumplir la supresión de datos (Ley 1581/2012, art. 8 y 15).

```csharp
// TaskSupportEndpoints.cs (DeleteList), antes de los ExecuteDelete de las líneas 133-134
var keys = await db.Attachments.IgnoreQueryFilters()
    .Where(a => db.Tasks.IgnoreQueryFilters().Any(t => t.Id == a.TaskId && t.ListId == id))
    .Select(a => a.StorageKey).ToListAsync(ct);
// ... borrar tareas y confirmar ...
foreach (var k in keys)
    try { await storage.DeleteAsync(k, ct); } catch (Exception ex) { logger.LogWarning(ex, "Blob {Key} no eliminado", k); }

// Upload (líneas 284-297): compensar si falla la base de datos
var key = await storage.SaveAsync(stream, ct);
try { db.Attachments.Add(attachment); await db.SaveChangesAsync(ct); }
catch { await storage.DeleteAsync(key, CancellationToken.None); throw; }
```

Además: un job de purga para adjuntos `IsDeleted` con más de N días (p. ej. 30), y documentar la política en el aviso de privacidad.

### SEC-11: Runtime .NET 9 próximo a fin de soporte (Media)

**CVSS: no aplica directamente.** A partir del fin de soporte, cualquier CVE nuevo de ASP.NET Core quedaría sin parche.

- Cambiar `backend/Directory.Build.props:3` a `<TargetFramework>net10.0</TargetFramework>`.
- Cambiar las imágenes a `mcr.microsoft.com/dotnet/sdk:10.0` y `aspnet:10.0` (`Api/Dockerfile:2,11`).
- Subir los paquetes `Microsoft.*` y `Npgsql.EntityFrameworkCore.PostgreSQL` a 10.x.
- Correr `scripts/test.ps1` completo.

---

## 5. Controles verificados como correctos

| Control | Evidencia |
|---|---|
| Autenticación obligatoria por defecto en `/api/v1` (solo login, refresh, logout y `/health` son anónimos) | `Api/Program.cs:131` |
| JWT: validación de emisor, audiencia, firma y tiempo de vida; `ClockSkew` de 30 s; clave de al menos 32 caracteres obligatoria | `Api/Infrastructure/ServiceRegistration.cs:26-27,47-61` |
| Revocación inmediata: `OnTokenValidated` comprueba `IsActive` y `SecurityStamp` | `Api/Infrastructure/ServiceRegistration.cs:73-89` |
| Refresh token aleatorio (64 bytes), guardado como SHA-256 y con detección de reutilización | `Api/Infrastructure/Auth/TokenService.cs:41-45,97,102,109` |
| Cookie `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth` | `Api/Features/Auth/AuthEndpoints.cs:124-132` |
| Access token solo en memoria; descargas con `Authorization` y blob, sin token en la URL | `frontend/src/api/client.ts:19,53-56`; `frontend/src/api/tasks.ts:296-300` |
| Bloqueo de cuenta (5 intentos, 15 min) y política de contraseñas | `Api/Infrastructure/ServiceRegistration.cs:31-38` |
| Autorización por recurso centralizada (`ProjectGuard`), 409 en proyectos cerrados y sin IDOR en los 92 endpoints | `Api/Infrastructure/Authorization/ProjectAuthorization.cs` |
| Hub: grupo `user:{id}` asignado en el servidor; `JoinProject` valida `View` | `Api/Infrastructure/Realtime/NotificationsHub.cs:30-40` |
| Sin SQL crudo; `ILIKE` con escape; ordenación por lista blanca | `Api/Infrastructure/Persistence/Search.cs:7-8`; `Api/Features/Tasks/TaskEndpoints.cs:78-88` |
| Sin XSS por construcción (sin `dangerouslySetInnerHTML`, `innerHTML` ni `eval`) | `frontend/src/**`; `frontend/src/features/collab/TaskConversation.tsx:94-107` |
| Sin SSRF ni ejecución de comandos | Sin `HttpClient` hacia URLs del usuario ni `Process.Start` |
| Almacenamiento con clave GUID validada (32 hex) y bucket privado con `octet-stream` | `Api/Infrastructure/Storage/FileStorage.cs:36,58-62`; `S3FileStorage.cs:47-58` |
| Errores 500 genéricos, sin trazas; logs sin cuerpo ni query | `Api/Infrastructure/Errors/GlobalExceptionHandler.cs:26-32`; `frontend/nginx.conf:2-3` |
| DTOs sin `PasswordHash`, `SecurityStamp` ni tokens | `Api/Features/Users/UserEndpoints.cs:14`; `Api/Features/Auth/AuthEndpoints.cs:13-14` |
| OpenAPI y Scalar solo en Development | `Api/Program.cs:105-109` |
| Secretos fuera del repositorio; Compose exige variables (`:?`); API publicada solo en loopback; contenedor no root | `docker-compose.yml:9,27,43`; `Api/Dockerfile:15`; historial de git limpio |
| HSTS de 1 año, `nosniff`, `X-Frame-Options: DENY` y `Referrer-Policy` en Vercel | `frontend/vercel.json:10-19` |
| Supabase: RLS activo y permisos revocados a `anon`/`authenticated` | `docs/DEPLOY.md` §1.5 (aplicado el 2026-10-22) |
| `npm audit`: 0 vulnerabilidades (prod y dev) | `frontend/package-lock.json` |

---

## 6. Checklist de hardening pendiente

**Autenticación y sesión**
- [ ] Plantilla `.env.example` sin valores utilizables y arranque bloqueado con placeholders (SEC-01)
- [ ] Forzar el cambio de contraseña del Admin sembrado; borrar `Seed__Admin*` de Render tras el primer arranque (SEC-01, SEC-20)
- [ ] Rate limit global y por cuenta en el login; acceso a `/auth/*` solo vía proxy (SEC-02)
- [ ] Rotación atómica del refresh token con periodo de gracia (SEC-03)
- [ ] Respuesta y tiempo uniformes en el login; retraso progresivo (SEC-04)
- [ ] Vida máxima absoluta de la sesión (SEC-14)
- [ ] Rate limit y bloqueo en `change-password` (SEC-16)
- [ ] PBKDF2 a 210 000 iteraciones, lista de contraseñas comunes y mínimo de 12 caracteres (SEC-17)
- [ ] `MaximumLength(128)` en las contraseñas (SEC-32)
- [ ] `ValidAlgorithms = [HmacSha256]` en el JWT y sin claim `email` (SEC-34, SEC-12)

**Autorización**
- [ ] Etiquetas: escritura solo para Admin o por proyecto (SEC-09, SEC-21)
- [ ] Notificaciones filtradas por permiso vigente; desasignar al quitar un miembro (SEC-08)
- [ ] Revalidar en `JoinProject`; `CloseOnAuthenticationExpiration`; cerrar sockets al desactivar (SEC-13)
- [ ] `users/lookup`, `clients` y `spaces` limitados por rol o visibilidad (SEC-23, SEC-24)
- [ ] Gestor degradado sin `EditTasks`, con pruebas en la matriz (SEC-25)
- [ ] Borrado de adjuntos: autor o gestor, con ActivityLog (SEC-26)

**Entrada, archivos y lógica**
- [ ] Rate limit por usuario en escrituras y subidas; cuota de almacenamiento; caché del dashboard (SEC-07)
- [ ] Validar firmas de archivo y antivirus; retirar `.doc/.xls/.ppt` (SEC-27)
- [ ] Sanear los nombres de archivo (Unicode `Cf`/`Cc`) (SEC-28)
- [ ] `allowIntegerValues: false` e `IsInEnum()` (SEC-29)
- [ ] Validadores para `reorder`, `page` y motivo de reapertura; vecino distinto a la propia tarea (SEC-30)
- [ ] `rowVersion` obligatorio; rebalanceo atómico; bloqueo de fila al cerrar el proyecto (SEC-31)
- [ ] `CAMPOS_REQUERIDOS` también al crear una tarea (SEC-22)
- [ ] Lock consultivo o índice único en `DueDateNotifier` (SEC-33)

**Configuración, transporte y cabeceras**
- [ ] CSP (primero Report-Only) y `Permissions-Policy` en Vercel y nginx (SEC-05)
- [ ] Postgres con `VerifyFull` y la CA de Supabase (SEC-06)
- [ ] nginx: snippet de cabeceras por `location`, `server_tokens off` y TLS si se expone (SEC-18)
- [ ] API: `UseHsts`, cabeceras propias y `AllowedHosts` explícito (SEC-19)
- [ ] CORS sin `AllowCredentials`; `.gitignore` con `.env.*`; S3 solo `https` (SEC-34)
- [ ] Confirmar que Render no registra query strings (SEC-12)

**Datos personales (Ley 1581 de 2012)**
- [ ] Borrado físico de archivos y job de purga (SEC-10)
- [ ] Purga de refresh tokens y retención de la IP definida (SEC-15)
- [ ] Logs sin correos (SEC-20)
- [ ] Política de tratamiento y aviso de privacidad que describan la retención (actividad, notificaciones, adjuntos)

**Plataforma y cadena de suministro**
- [ ] Migrar a .NET 10 LTS antes del fin de soporte de .NET 9 (SEC-11)
- [ ] Subir los paquetes de prueba (SignalR.Client 10.x); fijar las imágenes por digest; nginx `stable`; Dependabot o Renovate (SEC-35)
- [ ] CI con `npm audit`, `dotnet list package --vulnerable` y pruebas (hoy no hay `.github/`)

---

## 7. Las 5 acciones más urgentes

1. **Eliminar las credenciales de ejemplo utilizables (SEC-01).** Hay que vaciar `.env.example`, poner `SEED_DEMO=false`, hacer que la API se niegue a arrancar con placeholders y forzar el cambio de contraseña del Admin. *Es el único camino a toma de control total sin cuenta, y su arreglo cuesta minutos.*
2. **Cerrar la evasión del rate limit del login y la enumeración (SEC-02 + SEC-04).** Implica un limitador global y otro por cuenta, que `/auth/*` solo se acepte desde el proxy de Vercel y una respuesta uniforme en el login. *La API de Render es pública, así que hoy la fuerza bruta distribuida es viable.*
3. **Agregar CSP y verificar el certificado de Postgres (SEC-05 + SEC-06).** La CSP se publica primero en modo Report-Only; para Postgres se usa `VerifyFull` con la CA de Supabase. *Son dos controles de defensa en profundidad baratos sobre los dos activos más valiosos: el token de sesión y la base de datos.*
4. **Hacer atómica la rotación del refresh token y limitar el abuso por usuario (SEC-03 + SEC-07).** *Evita sesiones robadas indetectables y el agotamiento de la cuota o la factura de Supabase Storage.*
5. **Planificar la migración a .NET 10 LTS y la higiene de dependencias (SEC-11 + SEC-35).** *.NET 9 sale de soporte en noviembre de 2026: después, cualquier CVE de ASP.NET Core quedaría sin parche en producción.*

Las siguientes en la cola, por impacto en privacidad, son SEC-08 (notificaciones a ex-miembros), SEC-09 (etiquetas globales) y SEC-10 (borrado de archivos, Habeas Data).
