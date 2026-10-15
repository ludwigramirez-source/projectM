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
docker compose --profile test run --rm --build tests        # unit + integración contra SQL Server del compose

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
- **Frontend**: colores SOLO por tokens (`bg-primary-700`, `text-fg-muted`, …). Los colores por defecto de Tailwind están deshabilitados y `design-rules.test.ts` falla si aparece un hex/rgb literal fuera de `src/styles`. Paleta única en `src/styles/tokens.css`.
- Contraste: texto blanco nunca sobre primary/accent/success 500 → usar `text-on-light` o tono 700+. Botón primario = `primary-700`.
- Estados de tarea → tonos: Pendiente secondary, En progreso primary, En revisión accent, Completada success, Bloqueada/Vencida danger (`src/lib/status.ts`).
- TypeScript sin `enum` (`erasableSyntaxOnly`): usar uniones de strings. Alias `@/` → `src/`.
- Lint: oxlint (plantilla de Vite 8) + Prettier.

## Decisiones
- Build/test del backend 100 % en Docker (decisión del usuario, sin SDK local).
- Adjuntos en disco (`/data/attachments`, volumen Docker) detrás de `IFileStorage`.
- Refresh token rotativo en cookie HttpOnly `SameSite=Strict` con path `/api/auth`; access token solo en memoria del SPA.
- git: un commit por fase.
