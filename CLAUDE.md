# CLAUDE.md — ProjectM

Gestor de proyectos estilo ClickUp (MVP, un solo tenant, UI en español). Plan completo y decisiones de arquitectura: [PLAN.md](PLAN.md).

## Stack
- Backend: ASP.NET Core 10 (Minimal APIs por feature), EF Core 10 + PostgreSQL (Npgsql; Supabase en producción), Identity + JWT, SignalR.
- **Rama `vercel`**: despliegue Vercel (frontend) + Render (API Docker, `render.yaml`) + Supabase (Postgres + Storage S3). `main` conserva SQL Server. Guía: `docs/DEPLOY.md`.
- Frontend: React 19 + TypeScript + Vite 8 + Tailwind v4 + dnd-kit + TanStack Query.
- Despliegue: Docker Compose (`db`, `api`, `web` con nginx que hace proxy de `/api` y `/hubs`).

## Comandos
**No hay .NET SDK local**: todo `dotnet` corre en contenedor vía `scripts/dotnet.ps1` (usa caché NuGet en volumen).

```bash
# Backend
powershell -File scripts/dotnet.ps1 build
powershell -File scripts/dotnet.ps1 test tests/ProjectM.UnitTests
powershell -File scripts/dotnet.ps1 ef migrations add <Nombre> --project src/ProjectM.Api --output-dir Infrastructure/Persistence/Migrations
powershell -File scripts/test.ps1                           # unit + integración (contenedor SDK en la red del compose)
powershell -File scripts/test.ps1 --filter ProjectClosureTests

# Frontend (Node local)
cd frontend && npm run dev        # http://localhost:5173 (proxy a la API en localhost:5010)
cd frontend && npm run test       # vitest (incluye test de contraste de la paleta)
cd frontend && npm run lint && npm run build

# Stack completo
cp .env.example .env              # y completa secretos
docker compose up -d --build      # web en http://localhost:8080, API en 127.0.0.1:5010
```

## Convenciones
- **Backend**: `src/ProjectM.Api/Features/<Feature>/` contiene `*Endpoints.cs` (MapGroup), servicio, DTOs (records) y validadores FluentValidation (`.Validate<T>()` en el endpoint). Reglas de negocio puras en `ProjectM.Domain` (sin dependencias) y lanzan `DomainException(code, msg)`.
- Errores: siempre `ProblemDetails` con extensión `code` (constantes en `Infrastructure/Errors/AppException.cs#ErrorCodes`). Lanza `AppException.NotFound/Forbidden/Conflict/Validation`; no devuelvas errores a mano.
- Autorización: todo endpoint que toca un proyecto o sus hijos debe pasar por el servicio de autorización por proyecto (resource-based). Proyecto Cerrado ⇒ escritura denegada (409 `PROYECTO_CERRADO`).
- Listados paginados con `ToPagedAsync(page, pageSize)` (máx 100) → `{ items, page, pageSize, totalCount }`.
- Enums se serializan como string (nombres en español sin tildes: `EnProgreso`, `Planeacion`).
- Fechas de calendario (`StartDate`, `DueDate`, fechas planeadas) son `DateOnly`; instantes son `DateTimeOffset` UTC.
- **Frontend**: colores SOLO por tokens semánticos que cambian con el tema: superficies (`bg-bg`, `bg-surface`, `text-fg`, `text-fg-muted`, `border-border`, `bg-track`, `bg-overlay/50`) y tonos (`{primary|secondary|accent|success|danger|neutral}-{subtle|subtle-fg|fg|solid|solid-hover|mark|line}` + `text-on-{tono}-solid`). Las escalas 50–900 no se usan en componentes (`design-rules.test.ts` lo prohíbe, salvo `DesignSystemPage`), ni hex/rgb literales. Paleta y valores claro/oscuro en `src/styles/tokens.css`; `tokens.contrast.test.ts` valida AA en ambos temas.
- Patrones: badge = `bg-X-subtle text-X-subtle-fg`; botón = `bg-X-solid text-on-X-solid hover:bg-X-solid-hover`; texto de estado/error = `text-X-fg`; puntos/barras/bordes de color = `X-mark`.
- Tema: `ThemeProvider`/`ThemeSwitch` (`src/app/theme.tsx`); `data-theme` en `<html>`, inicia según el sistema y recuerda la elección (`localStorage.pm-theme`); script inline en `index.html` evita el parpadeo; al imprimir se fuerza claro.
- Estados de tarea → tonos: Pendiente secondary, En progreso primary, En revisión accent, Completada success, Bloqueada/Vencida danger (`src/lib/status.ts`).
- Responsive (mobile-first): variante `touch:` (= `pointer: coarse`) para objetivos ≥ 40 px y campos de 16 px (evita zoom en iOS); `pt-safe`/`pb-safe` para áreas seguras; tablas pasan a tarjetas bajo `md` (`ListView`), filtros de tareas plegables en móvil, calendario en vista Agenda bajo 640 px, Kanban/Calendario con `TouchSensor` (mantener presionado para arrastrar); modales como hoja inferior en móvil.
- TypeScript sin `enum` (`erasableSyntaxOnly`): usar uniones de strings. Alias `@/` → `src/`.
- Lint: oxlint (plantilla de Vite 8) + Prettier.

## Decisiones
- `ProjectM.Domain` referencia solo `Microsoft.Extensions.Identity.Stores` (para `AppUser : IdentityUser<Guid>` con navegaciones); el resto del dominio no tiene dependencias.
- Autorización por proyecto centralizada en `ProjectGuard` (`Infrastructure/Authorization`): 404 si no puede ver, 409 `PROYECTO_CERRADO` si escribe en cerrado, 403 si no tiene el permiso. Reglas puras en `ProjectRules` (probadas en `PermissionMatrixTests`).
- "Hoy" sale de `AppClock` con la zona `App:TimeZone` (`APP_TIMEZONE`).
- `Project.Code` (PRJ-0001) es columna calculada sobre la secuencia `ProjectNumbers`.
- Fechas de calendario `DateOnly`; FKs a usuarios con `Restrict`; Project→Lists→Tasks en cascada, resto `NoAction` (rutas múltiples en SQL Server).
- Entidades con `Id` generado en cliente (`Guid.NewGuid()`, `ValueGeneratedNever`).
- Build/test del backend 100 % en Docker (decisión del usuario, sin SDK local).
- Adjuntos en disco (`/data/attachments`, volumen Docker) detrás de `IFileStorage`.
- Rate limiting por IP: login 10/min (`Auth:LoginPermitLimitPerMinute`) y refresh 120/min (`Auth:RefreshPermitLimitPerMinute`); el cliente reintenta el refresh ante 429 en vez de cerrar la sesión. Además: tope global del login (`Auth:LoginGlobalPermitLimitPerMinute`, la IP puede falsificarse) y límites por usuario (`ServiceRegistration.UserWritesPolicy` / `UploadsPolicy`) en crear tareas, comentar, etiquetas, dashboard y subidas; endpoints nuevos costosos o que notifican deben usar `.RequireRateLimiting(...)`.
- Refresh token rotativo en cookie HttpOnly `SameSite=Strict` con path `/api/v1/auth`; access token solo en memoria del SPA. Rotación atómica (UPDATE condicional); reusar un token recién rotado (< `Auth:RefreshReuseGraceSeconds`) responde 409 `REFRESH_EN_CURSO` y el cliente reintenta; fuera del margen revoca todas las sesiones.
- Seguridad (ver `docs/SECURITY_AUDIT.md`): login con respuesta uniforme (sin 423, hash ficticio); contraseña asignada por otro ⇒ `MustChangePassword` + claim `pwd_change` ⇒ 403 `CAMBIO_CONTRASENA_REQUERIDO` fuera de `/api/v1/auth` (`Infrastructure/Security.cs`), pantalla `ForcedPasswordChange` en el SPA; `StartupGuard` impide arrancar con valores de `.env.example`; JWT sin correo (viaja en la URL de SignalR); `Proxy:SharedSecret` opcional para aceptar `/auth/*` solo vía el proxy.
- CSP estricta sin scripts inline (`frontend/security-headers.conf` en nginx, `vercel.json` en Vercel; el tema inicial va en `public/theme-init.js`). No agregar `<script>` inline ni orígenes externos sin actualizar ambas CSP.
- PostgreSQL remoto: nunca menos que `SSL Mode=Require`; `*.supabase.com` usa `VerifyFull` con la CA incluida (`Api/certs/supabase-prod-ca-2021.crt`); otro proveedor: `Database:RootCertificate`.
- Tareas: `PUT /tasks/{id}` reemplaza los campos básicos (con `rowVersion`); estado y movimiento van por `PATCH .../status` y `PATCH .../move` (orden fraccional con rebalanceo).
- Campos personalizados obligatorios: no bloquean el alta rápida; se exigen al completar la tarea (`CAMPOS_REQUERIDOS`) y no pueden vaciarse.
- El detalle de tarea se abre desde cualquier pantalla con `?task=<id>` (`TaskDrawer` montado en `MainLayout`).
- Eventos de dominio: las entidades hacen `Raise(...)` (o `db.AddEvent(...)` tras un `ExecuteUpdate`); `AppDbContext.SaveChangesAsync` los pasa a `DomainEventDispatcher`, que escribe `ActivityLog` y `Notification` en la misma transacción y, tras confirmar, entrega por `INotificationChannel` (v1: SignalR) y avisa `projectChanged` al grupo del proyecto. Correo/WhatsApp = otra implementación de `INotificationChannel`.
- Ediciones de tareas (título, fechas, prioridad, lista, hito, descripción) se detectan en el change tracker y generan `TaskUpdated` automáticamente.
- Hub `/hubs/notifications`: grupos `user:{id}` y `project:{id}` (`JoinProject` valida permiso de lectura). `DueDateNotifier` (hosted service, cada hora) avisa "vence mañana" y "vencida" sin duplicar; se desactiva con `Notifications:DueDateJob=false`.
- Dashboard (`GET /dashboard`): agrega solo proyectos visibles; tareas de proyectos cerrados no cuentan como carga. Gráficos como barras horizontales etiquetadas (la paleta obligatoria no separa bien accent/success ni secondary como categorías, así que el color nunca es la única codificación) con leyenda, tooltip y vista de tabla.
- `DemoSeeder` (Seed:Demo) usa fechas relativas a hoy, usuarios `@projectm.demo` e ignora eventos de dominio al sembrar.
- PWA (`vite-plugin-pwa`, `registerType: prompt`): precache solo del cascarón; `/api`, `/hubs` y `/health` jamás en caché del SW (NetworkOnly + denylist del fallback). `PwaPrompts` (src/app/pwa.tsx) registra el SW y muestra “nueva versión” y “sin conexión”; “Instalar app” en el menú de usuario (instrucciones en iOS). nginx sirve `sw.js`, `manifest.webmanifest` e `index.html` sin caché. Íconos generados con `npx pwa-assets-generator` (`pwa-assets.config.ts`). El navegador integrado de la app de escritorio no registra service workers: verificar la PWA en Chrome.
- Rutas cargadas con `React.lazy` (bundle inicial < 500 kB).
- PostgreSQL: concurrencia con `xmin` (`uint RowVersion`, se envía como string); búsquedas con `EF.Functions.ILike(x, Search.Contains(q))`; columnas largas `text`, listas como arreglos (`text[]`, `uuid[]`), `ActivityLog.Data` en `jsonb`; código `PRJ-0001` como columna generada.
- Adjuntos: `Storage:Provider` = `Local` (disco) o `S3` (`S3FileStorage`: Supabase Storage/R2/AWS; bucket privado, descarga vía API). Prueba `S3StorageTests` solo corre con `TEST_S3_URL/KEY/SECRET`.
- Frontend: `VITE_API_URL` (vacío = mismo origen; en Vercel el rewrite `/api` hace de proxy para la cookie) y `VITE_DIRECT_API_URL` (SignalR y adjuntos directo a la API, con JWT). `Proxy:ForwardLimit` = proxies confiables delante de la API (1 nginx, 2 Vercel+Render).
- nginx no registra query strings (el JWT de SignalR va en `?access_token=`).
- git: un commit por fase.
