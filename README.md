# ProjectM

Gestor de proyectos web estilo ClickUp para uso interno de un equipo (un solo tenant), con interfaz en español.

- **Jerarquía:** Espacio › Proyecto › Lista › Tarea › Subtarea
- **Tareas:** responsable, estado, prioridad, fechas, etiquetas, campos personalizados (texto, número, fecha, selección), adjuntos
- **Vistas:** Lista, Kanban (arrastrar y soltar) y Calendario (mes/semana, reprogramar arrastrando)
- **Colaboración:** comentarios con @menciones, historial de actividad y notificaciones en tiempo real
- **Ciclo de vida del proyecto:** apertura con plantilla → seguimiento (avance, hitos, riesgos y bloqueos) → cierre con checklist, acta, lecciones aprendidas y cierre forzado justificado; los proyectos cerrados son de solo lectura y solo un Admin puede reabrirlos
- **Dashboard:** tareas por estado, vencidas, carga por persona y proyectos por estado

Stack: ASP.NET Core 9 + EF Core + SQL Server 2022, ASP.NET Identity + JWT, SignalR · React 19 + TypeScript + Vite + Tailwind v4 + dnd-kit · Docker Compose.
Arquitectura, modelo de datos y endpoints: [PLAN.md](PLAN.md). Convenciones para desarrollo: [CLAUDE.md](CLAUDE.md).

## Requisitos

- Docker Desktop (o Docker Engine + Compose v2)
- Para desarrollar el frontend fuera de Docker: Node.js 22+
- **No** se necesita el SDK de .NET: compilación, migraciones y pruebas corren en contenedores.

## Arranque rápido

```bash
cp .env.example .env
```

Edita `.env` y reemplaza **todos** los valores de ejemplo:

| Variable | Descripción |
|---|---|
| `MSSQL_SA_PASSWORD` | Contraseña de SQL Server (mayúsculas, minúsculas, números y símbolo; mín. 8) |
| `JWT_KEY` | Clave aleatoria de ≥ 32 caracteres (`openssl rand -base64 48`) |
| `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` | Administrador inicial (contraseña ≥ 10 con mayúscula, minúscula y número) |
| `SEED_DEMO` / `SEED_DEMO_PASSWORD` | Carga datos de demostración y contraseña común de los usuarios demo |
| `ALLOWED_ORIGINS` | Orígenes permitidos por CORS (separados por coma) |
| `APP_TIMEZONE` | Zona horaria del equipo (IANA), p. ej. `America/Bogota` |
| `AUTH_SECURE_COOKIES` | `true` (recomendado). Usa `false` solo si sirves por HTTP fuera de `localhost` |
| `WEB_PORT` | Puerto público de la aplicación (por defecto 8080) |

```bash
docker compose up -d --build
```

Abre <http://localhost:8080>. La API aplica las migraciones y los datos iniciales al arrancar.
Salud del servicio: <http://localhost:8080/health>.

### Usuarios de demostración

Con `SEED_DEMO=true` se crean estos usuarios (contraseña: el valor de `SEED_DEMO_PASSWORD`):

| Usuario | Rol | Qué ver |
|---|---|---|
| `laura@projectm.demo` | Gestor | Responsable de «Implementación CRM» y «Renovación sitio web» |
| `carlos@projectm.demo` | Gestor | «App móvil de pedidos» (en riesgo, con bloqueos) y «Migración a la nube 2026» (cerrado, acta con cierre forzado) |
| `ana@projectm.demo`, `diego@projectm.demo`, `sofia@projectm.demo`, `mateo@projectm.demo` | Miembro | Tareas asignadas, comentarios y notificaciones |
| `valeria@projectm.demo` | Observador | Solo lectura |

Además existe el Admin definido en `SEED_ADMIN_EMAIL`. Los datos demo usan fechas relativas a la fecha de arranque y solo se cargan si no existen.

## Roles

| Rol | Puede |
|---|---|
| **Admin** | Todo: usuarios, plantillas, checklist de cierre, cualquier proyecto; único que reabre proyectos cerrados |
| **Gestor** | Abrir proyectos; gestionar y cerrar los proyectos donde es responsable; trabajar tareas donde es miembro |
| **Miembro** | Crear, editar, mover y comentar tareas de sus proyectos |
| **Observador** | Ver los proyectos donde es miembro (solo lectura) |

## Desarrollo

```bash
# Backend (en contenedor .NET 9 SDK)
powershell -File scripts/dotnet.ps1 build
powershell -File scripts/dotnet.ps1 ef migrations add <Nombre> --project src/ProjectM.Api --output-dir Infrastructure/Persistence/Migrations

# Frontend con recarga en caliente (la API debe estar arriba: docker compose up -d db api)
cd frontend
npm install
npm run dev            # http://localhost:5173
```

## Pruebas

```bash
# Backend: unitarias + integración contra el SQL Server del compose (cada clase usa una BD efímera)
powershell -File scripts/test.ps1

# Frontend: componentes, cliente API y contraste WCAG de la paleta
cd frontend
npm run test
npm run lint
```

Alternativa sin PowerShell: `docker compose --profile test run --rm --build tests`.

Cobertura de los flujos críticos: matriz de permisos por rol y por proyecto, ciclo de vida y cierre de proyecto (checklist, tareas abiertas, cierre forzado, solo lectura, reapertura), cambios de estado de tareas, campos personalizados, adjuntos, concurrencia optimista, notificaciones (incluido SignalR de punta a punta), autenticación (lockout, rate limiting, rotación y revocación de refresh tokens).

## Seguridad

- Contraseñas con ASP.NET Identity (PBKDF2), bloqueo tras 5 intentos fallidos y rate limiting en login/refresh.
- Access token JWT de 15 min solo en memoria; refresh token rotativo en cookie `HttpOnly`, `Secure`, `SameSite=Strict`, con detección de reutilización.
- Desactivar un usuario, cambiar su rol o su contraseña invalida sus sesiones de inmediato.
- Autorización por proyecto en cada endpoint; los proyectos ajenos responden 404.
- CORS restringido a `ALLOWED_ORIGINS`; en Docker el frontend y la API comparten origen vía nginx.
- Adjuntos: lista blanca de extensiones, máx. 20 MB, nombre en disco aleatorio y descarga solo autorizada.
- Sin secretos en el repositorio: toda la configuración sensible llega por variables de entorno (`.env` está en `.gitignore`).

## Extensión: notificaciones por correo o WhatsApp

Las notificaciones se persisten y luego se entregan por cada `INotificationChannel` registrado (v1: tiempo real con SignalR).
Para agregar correo o WhatsApp, implementa esa interfaz en `backend/src/ProjectM.Api/Infrastructure/Notifications` y regístrala en `Program.cs`.
