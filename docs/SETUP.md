# Instalación y entorno local

Cómo levantar ProjectM en una máquina de desarrollo, qué variables de entorno usa y cómo correr las pruebas. Rama documentada: `vercel` (PostgreSQL). La rama `main` conserva la versión con SQL Server (README.md).

> Todos los valores de ejemplo de este documento son marcadores. Nunca subas `.env` al repositorio (.env.example).

## 1. Requisitos previos

| Herramienta | Para qué | Evidencia |
|---|---|---|
| Docker Desktop (o Docker Engine + Compose v2) | Base de datos, API, web y **todo** el tooling de .NET | (docker-compose.yml), (scripts/dotnet.ps1) |
| PowerShell | Ejecutar `scripts/dotnet.ps1` y `scripts/test.ps1` | (scripts/dotnet.ps1), (scripts/test.ps1) |
| Node.js (la imagen usa `node:24-alpine`; el README pide 22+) | Desarrollar y probar el frontend fuera de Docker | (frontend/Dockerfile), (README.md) |
| Git | Clonar el repositorio | — |

**No se necesita el SDK de .NET instalado.** `scripts/dotnet.ps1` ejecuta `dotnet` dentro de `mcr.microsoft.com/dotnet/sdk:10.0`, con la caché de NuGet en el volumen `projectm-nuget` y las herramientas (dotnet-ef) en `projectm-dotnet-tools` (scripts/dotnet.ps1).

## 2. Instalación paso a paso

```bash
git clone https://github.com/ludwigramirez-source/projectM.git
cd projectM
git checkout vercel
cp .env.example .env          # luego edita .env y reemplaza TODOS los valores
docker compose up -d --build  # db + api + web
```

- Web: <http://localhost:8080> (puerto `WEB_PORT`). nginx sirve el SPA y hace de proxy de `/api`, `/hubs` y `/health` hacia la API (frontend/nginx.conf).
- API directa: `127.0.0.1:5010`, publicada solo en loopback (docker-compose.yml).
- Salud: <http://localhost:8080/health>, que responde `{"status":"ok"}` o 503 `db_unavailable` (backend/src/ProjectM.Api/Program.cs).
- Al arrancar, la API aplica las migraciones (`Database__MigrateOnStartup: "true"` en docker-compose.yml) y siembra los roles, el admin, los datos de referencia y, si `SEED_DEMO=true`, los datos demo (backend/src/ProjectM.Api/Program.cs).

### Usuarios demo

Con `SEED_DEMO=true` se crean usuarios `@projectm.demo` (Gestor, Miembro y Observador) que comparten la contraseña `SEED_DEMO_PASSWORD` (backend/src/ProjectM.Api/Infrastructure/Seed/DemoSeeder.cs). El README lista cada usuario y qué ver con él (README.md).

### Reglas de contraseña

Mínimo 10 caracteres, con dígito, minúscula y mayúscula; no exige símbolo. La cuenta se bloquea 15 minutos tras 5 intentos fallidos. El correo es único (backend/src/ProjectM.Api/Infrastructure/ServiceRegistration.cs). `SEED_ADMIN_PASSWORD` debe cumplir estas reglas.

## 3. Variables de entorno

ASP.NET Core lee la configuración de `appsettings.json` y de variables de entorno, donde `__` separa las secciones (`Jwt__Key` equivale a `Jwt:Key`). En local, `docker-compose.yml` traduce las variables de `.env` (MAYÚSCULAS) a las claves de la API.

### 3.1 `.env` de Docker Compose (raíz)

| Variable `.env` | Llega a la API como | Propósito | Obligatoria |
|---|---|---|---|
| `POSTGRES_PASSWORD` | `ConnectionStrings__Default` (Host=db…) | Contraseña del PostgreSQL local | Sí (compose falla si falta) |
| `JWT_KEY` | `Jwt__Key` | Clave HMAC para firmar los JWT; ≥ 32 caracteres o la API no arranca | Sí |
| `SEED_ADMIN_EMAIL` | `Seed__AdminEmail` | Correo del Admin inicial | Sí |
| `SEED_ADMIN_PASSWORD` | `Seed__AdminPassword` | Contraseña del Admin inicial | Sí |
| `SEED_DEMO` | `Seed__Demo` | `true` carga datos demo (por defecto `false`) | No |
| `SEED_DEMO_PASSWORD` | `Seed__DemoPassword` | Contraseña común de los usuarios demo | Solo con demo |
| `ALLOWED_ORIGINS` | `Cors__AllowedOrigins` | Orígenes CORS, separados por coma | No (por defecto `http://localhost:8080`) |
| `APP_TIMEZONE` | `App__TimeZone` | Zona IANA para calcular "hoy" y los vencimientos | No (por defecto `UTC`) |
| `AUTH_SECURE_COOKIES` | `Auth__SecureCookies` | Marca la cookie de refresh como `Secure` | No (por defecto `true`) |
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | Entorno de ASP.NET; en `Development` se exponen OpenAPI y Scalar | No (por defecto `Production`) |
| `WEB_PORT` | — (puerto del servicio `web`) | Puerto público del frontend | No (por defecto 8080) |

Fuentes: (.env.example), (docker-compose.yml), (backend/src/ProjectM.Api/Infrastructure/ServiceRegistration.cs).

### 3.2 Claves de configuración de la API (todas)

| Clave (variable de entorno) | Valor por defecto | Propósito | Dónde se lee |
|---|---|---|---|
| `ConnectionStrings__Default` | vacío | Cadena de PostgreSQL (formato clave=valor de Npgsql o URI `postgresql://…`). Hosts remotos: nunca menos que `SSL Mode=Require`; con CA disponible, `VerifyFull` (la de Supabase va incluida en la imagen para `*.supabase.com`) | (Program.cs), (Infrastructure/Persistence/ConnectionStrings.cs) |
| `Database__RootCertificate` | — | Ruta a la CA raíz para validar el TLS de un PostgreSQL que no sea Supabase (activa `VerifyFull`) | (Infrastructure/Persistence/ConnectionStrings.cs) |
| `Jwt__Key` | vacío | Clave de firma de los JWT (≥ 32 caracteres) | (Infrastructure/Auth/JwtOptions.cs), (Infrastructure/ServiceRegistration.cs) |
| `Jwt__Issuer` / `Jwt__Audience` | `ProjectM` / `ProjectM.Web` | Emisor y audiencia del token | (Infrastructure/Auth/JwtOptions.cs) |
| `Jwt__AccessTokenMinutes` | 15 | Vida del access token | (Infrastructure/Auth/JwtOptions.cs) |
| `Jwt__RefreshTokenDays` | 7 | Vida del refresh token | (Infrastructure/Auth/JwtOptions.cs) |
| `Auth__SecureCookies` | `true` | Cookie de refresh solo por HTTPS (localhost funciona) | (Infrastructure/Auth/JwtOptions.cs) |
| `Auth__LoginPermitLimitPerMinute` | 10 | Intentos de login por IP y minuto | (Infrastructure/Auth/JwtOptions.cs) |
| `Auth__RefreshPermitLimitPerMinute` | 120 | Renovaciones de sesión por IP y minuto | (Infrastructure/Auth/JwtOptions.cs) |
| `Auth__LoginGlobalPermitLimitPerMinute` | 60 | Intentos de login por minuto sumando todas las IP (contra IPs rotadas o falsificadas) | (Infrastructure/Auth/JwtOptions.cs) |
| `Auth__UserWritePermitLimitPerMinute` | 60 | Por usuario: crear tareas, comentar, crear etiquetas y consultar el dashboard | (Infrastructure/Auth/JwtOptions.cs) |
| `Auth__UploadPermitLimitPer10Minutes` | 30 | Subidas de adjuntos por usuario cada 10 minutos | (Infrastructure/Auth/JwtOptions.cs) |
| `Auth__RefreshReuseGraceSeconds` | 10 | Margen en que reusar un refresh recién rotado se trata como carrera entre pestañas (409) y no como robo | (Infrastructure/Auth/JwtOptions.cs) |
| `Auth__ForcePasswordChange` | `true` | Obliga a cambiar la contraseña asignada por otra persona (Admin sembrado, usuarios creados o restablecidos por un Admin) | (Infrastructure/Auth/JwtOptions.cs), (Infrastructure/Security.cs) |
| `Cors__AllowedOrigins` | vacío | Orígenes permitidos (CSV); con credenciales | (Program.cs) |
| `Database__MigrateOnStartup` | `false` | Aplica las migraciones de EF al arrancar | (Program.cs), (appsettings.json) |
| `Seed__AdminEmail` / `Seed__AdminPassword` | vacío | Admin inicial; si faltan, solo se registra una advertencia | (Infrastructure/Seed/IdentitySeeder.cs) |
| `Seed__Demo` / `Seed__DemoPassword` | `false` / vacío | Datos de demostración | (Infrastructure/Seed/DemoSeeder.cs) |
| `App__TimeZone` | UTC (también si el id no existe) | Zona IANA del equipo para calcular "hoy" | (Infrastructure/AppClock.cs) |
| `Storage__Provider` | `Local` | `Local` (disco) o `S3` | (Program.cs), (Infrastructure/Storage/FileStorage.cs) |
| `Storage__AttachmentsPath` | `/data/attachments` | Carpeta de adjuntos en modo `Local` | (Infrastructure/Storage/FileStorage.cs) |
| `Storage__MaxFileSizeMb` | 20 | Tamaño máximo por archivo (además, el formulario multipart admite hasta 25 MB) | (Infrastructure/Storage/FileStorage.cs), (Program.cs) |
| `Storage__S3__ServiceUrl` | vacío | Endpoint S3 (Supabase: `https://<ref>.supabase.co/storage/v1/s3`) | (Infrastructure/Storage/S3FileStorage.cs) |
| `Storage__S3__Region` | `us-east-1` | Región S3 | (Infrastructure/Storage/S3FileStorage.cs) |
| `Storage__S3__Bucket` | `attachments` | Bucket privado | (Infrastructure/Storage/S3FileStorage.cs) |
| `Storage__S3__AccessKeyId` / `Storage__S3__SecretAccessKey` | vacío | Credenciales S3 (secretas) | (Infrastructure/Storage/S3FileStorage.cs) |
| `Notifications__DueDateJob` | `true` | Activa el servicio horario de avisos de vencimiento | (Program.cs) |
| `Proxy__ForwardLimit` | 1 | Proxies confiables delante de la API (1 = nginx, 2 = Vercel + Render) | (Program.cs) |
| `Proxy__SharedSecret` | — | Si se define, `/api/v1/auth/*` solo se acepta con el encabezado `X-Proxy-Secret` que inyecta el proxy del frontend (ver DEPLOY.md) | (Infrastructure/Security.cs) |
| `PORT` | — | Solo en Render: le indica a Render a qué puerto enrutar. La API no lo lee; escucha en 8080, el puerto por defecto de la imagen `aspnet:10.0` (`EXPOSE 8080`) | (render.yaml), (backend/src/ProjectM.Api/Dockerfile) |

Las rutas de esta tabla son relativas a `backend/src/ProjectM.Api/`.

Extensiones de adjuntos permitidas: pdf, doc(x), xls(x), ppt(x), odt, ods, txt, csv, md, png, jpg/jpeg, gif, webp y zip (backend/src/ProjectM.Api/Infrastructure/Storage/FileStorage.cs).

### 3.3 Frontend (Vite)

| Variable | Propósito | Fuente |
|---|---|---|
| `VITE_API_URL` | Origen de la API para las llamadas REST. Vacío = mismo origen (nginx o el rewrite de Vercel) | (frontend/.env.example), (frontend/src/api/client.ts) |
| `VITE_DIRECT_API_URL` | API accedida directamente para SignalR y adjuntos (con JWT, sin cookies) | (frontend/.env.example), (frontend/src/app/realtime.tsx), (frontend/src/api/tasks.ts) |

### 3.4 Pruebas

| Variable | Propósito | Fuente |
|---|---|---|
| `TEST_SQL_CONNECTION` | Servidor PostgreSQL de las pruebas de integración; cada clase crea y borra su propia base | (backend/tests/ProjectM.IntegrationTests/Infrastructure/ApiFactory.cs), (scripts/test.ps1) |
| `TEST_S3_URL`, `TEST_S3_KEY`, `TEST_S3_SECRET` | Habilitan `S3StorageTests` (se omite si faltan) | (backend/tests/ProjectM.IntegrationTests/S3StorageTests.cs) |

## 4. Correr en local

### Opción A: todo en Docker (recomendada)

```bash
docker compose up -d --build
docker compose logs -f api        # registros de la API
docker compose down               # detener (los volúmenes pgdata y attachments se conservan)
```

### Opción B: frontend con recarga en caliente

Con `db` y `api` corriendo en Docker, el servidor de Vite (puerto 5173) hace de proxy de `/api` y `/hubs` hacia `http://localhost:5010` (frontend/vite.config.ts):

```bash
cd frontend
npm install
npm run dev        # http://localhost:5173
```

Agrega `http://localhost:5173` a `ALLOWED_ORIGINS` (el `.env.example` ya lo trae).

### Backend: compilar y migraciones

```bash
powershell -File scripts/dotnet.ps1 build
powershell -File scripts/dotnet.ps1 ef migrations add <Nombre> --project src/ProjectM.Api --output-dir Infrastructure/Persistence/Migrations
```

`dotnet ef` usa `DesignTimeDbContextFactory`, que no necesita una base real (backend/src/ProjectM.Api/Infrastructure/Persistence/DesignTimeDbContextFactory.cs).

Con `ASPNETCORE_ENVIRONMENT=Development`, la API expone el documento OpenAPI y la UI de Scalar (backend/src/ProjectM.Api/Program.cs). ⚠️ A CONFIRMAR: la ruta exacta depende de los valores por defecto de `MapOpenApi`/`MapScalarApiReference` (normalmente `/openapi/v1.json` y `/scalar/v1`).

## 5. Pruebas

| Suite | Comando | Qué cubre |
|---|---|---|
| Unitarias backend | `powershell -File scripts/dotnet.ps1 test tests/ProjectM.UnitTests` | Matriz de permisos, ciclo de vida del proyecto, reglas de tareas y conversión de cadenas de conexión (backend/tests/ProjectM.UnitTests/*.cs) |
| Unitarias + integración | `powershell -File scripts/test.ps1` | Además Auth, Proyectos, Tareas, Colaboración, Dashboard, seed demo y S3 contra el PostgreSQL del compose (backend/tests/ProjectM.IntegrationTests/*.cs) |
| Filtrar | `powershell -File scripts/test.ps1 --filter ProjectTests` | Argumentos extra de `dotnet test` (scripts/test.ps1) |
| Alternativa | `docker compose --profile test run --rm tests` | Servicio `tests` del compose (docker-compose.yml), (backend/tests.Dockerfile) |
| Frontend | `cd frontend && npm run test` | Vitest con jsdom: cliente HTTP, tema, Kanban, reglas de diseño y contraste AA de la paleta (frontend/src/**/*.test.ts*) |
| Lint y build | `cd frontend && npm run lint && npm run build` | oxlint, verificación de tipos con `tsc -b` y build de Vite (frontend/package.json) |

`scripts/test.ps1` levanta `db` con `docker compose up -d --wait db` y corre `dotnet test` en un contenedor SDK conectado a la red `projectm_default` (scripts/test.ps1).
