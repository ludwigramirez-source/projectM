# CLAUDE.md — ProjectM

Gestor de proyectos estilo ClickUp (MVP, un solo tenant, UI en español). Plan completo y decisiones de arquitectura: [PLAN.md](PLAN.md).

## Stack
- Backend: ASP.NET Core 9 (Minimal APIs por feature), EF Core 9 + SQL Server 2022, Identity + JWT, SignalR.
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
- Refresh token rotativo en cookie HttpOnly `SameSite=Strict` con path `/api/v1/auth`; access token solo en memoria del SPA.
- Tareas: `PUT /tasks/{id}` reemplaza los campos básicos (con `rowVersion`); estado y movimiento van por `PATCH .../status` y `PATCH .../move` (orden fraccional con rebalanceo).
- Campos personalizados obligatorios: no bloquean el alta rápida; se exigen al completar la tarea (`CAMPOS_REQUERIDOS`) y no pueden vaciarse.
- El detalle de tarea se abre desde cualquier pantalla con `?task=<id>` (`TaskDrawer` montado en `MainLayout`).
- Eventos de dominio: las entidades hacen `Raise(...)` (o `db.AddEvent(...)` tras un `ExecuteUpdate`); `AppDbContext.SaveChangesAsync` los pasa a `DomainEventDispatcher`, que escribe `ActivityLog` y `Notification` en la misma transacción y, tras confirmar, entrega por `INotificationChannel` (v1: SignalR) y avisa `projectChanged` al grupo del proyecto. Correo/WhatsApp = otra implementación de `INotificationChannel`.
- Ediciones de tareas (título, fechas, prioridad, lista, hito, descripción) se detectan en el change tracker y generan `TaskUpdated` automáticamente.
- Hub `/hubs/notifications`: grupos `user:{id}` y `project:{id}` (`JoinProject` valida permiso de lectura). `DueDateNotifier` (hosted service, cada hora) avisa "vence mañana" y "vencida" sin duplicar; se desactiva con `Notifications:DueDateJob=false`.
- Dashboard (`GET /dashboard`): agrega solo proyectos visibles; tareas de proyectos cerrados no cuentan como carga. Gráficos como barras horizontales etiquetadas (la paleta obligatoria no separa bien accent/success ni secondary como categorías, así que el color nunca es la única codificación) con leyenda, tooltip y vista de tabla.
- `DemoSeeder` (Seed:Demo) usa fechas relativas a hoy, usuarios `@projectm.demo` e ignora eventos de dominio al sembrar.
- git: un commit por fase.
