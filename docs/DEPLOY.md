# Guía de despliegue: Supabase + Render + Vercel (rama `vercel`)

Esta guía publica ProjectM en internet con tres servicios:

| Servicio | Qué aloja | Plan sugerido |
|---|---|---|
| **Supabase** | Base de datos PostgreSQL y archivos adjuntos (Storage) | Free para empezar (ver límites al final) |
| **Render** | La API .NET (contenedor Docker) | **Starter** (~7 USD/mes). El Free sirve para probar, pero se duerme |
| **Vercel** | El frontend (React/PWA), con HTTPS | Hobby para pruebas personales; **Pro** si es uso de una empresa |

```
Navegador / app instalada (PWA)
   │  https://<tu-app>.vercel.app
   ├──► Vercel ── reenvía /api/* ──►┐
   │                                 ▼
   └── SignalR y adjuntos ──► Render: API .NET ──► Supabase (PostgreSQL + Storage)
```

> **Orden importante**: Supabase → Render → Vercel → volver a Render (ajustar CORS). Cada paso necesita un dato del anterior.
>
> Lleva una nota con los valores que irás copiando; la plantilla está en el [Anexo A](#anexo-a-hoja-de-valores).

Tiempo estimado: 45–60 minutos la primera vez.

---

## Paso 0. Antes de empezar

1. Ten acceso al repositorio `ludwigramirez-source/projectM` en GitHub y verifica que existe la rama **`vercel`**.
2. Crea (o usa) cuentas en <https://supabase.com>, <https://render.com> y <https://vercel.com>. Lo más simple es entrar en las tres con **"Continue with GitHub"**.
3. Prepara la **contraseña del Admin inicial**: mínimo 10 caracteres, con mayúscula, minúscula y número (p. ej. generada con tu gestor de contraseñas).

---

## Paso 1. Supabase (base de datos y archivos)

### 1.1 Crear el proyecto

1. Entra a <https://supabase.com/dashboard> → **New project**.
2. Completa:
   - **Organization**: la tuya.
   - **Project name**: `projectm`.
   - **Database Password**: pulsa **Generate a password** y **cópiala ya** a tu hoja de valores (no se vuelve a mostrar completa).
   - **Region**: elige **East US (North Virginia)** o **West US (Oregon)**, para que quede cerca de Render (el blueprint usa Oregon).
3. Pulsa **Create new project** y espera 1–2 minutos a que termine de aprovisionar.

### 1.2 Copiar la cadena de conexión

1. En el proyecto, pulsa el botón **Connect** (arriba, junto al nombre del proyecto).
2. En la ventana:
   - Pestaña o tipo: **.NET**.
   - Método: **Session pooler**.
     - ⚠️ **No uses "Direct connection"**: es solo IPv6 y Render no puede alcanzarla.
     - ⚠️ **No uses "Transaction pooler"**: no es compatible con una API que mantiene conexiones abiertas.
3. Copia la cadena. Se ve parecida a:
   ```
   User Id=postgres.abcdefghijkl;Password=[YOUR-PASSWORD];Server=aws-0-us-west-1.pooler.supabase.com;Port=5432;Database=postgres
   ```
4. Reemplaza `[YOUR-PASSWORD]` por la contraseña del paso 1.1 y **agrega al final** `;SSL Mode=Require`. Debe quedar así:
   ```
   User Id=postgres.abcdefghijkl;Password=TuClaveReal;Server=aws-0-us-west-1.pooler.supabase.com;Port=5432;Database=postgres;SSL Mode=Require
   ```
   Guárdala como **CADENA_DB**. No crees tablas a mano: la API las crea sola al arrancar.

### 1.3 Crear el bucket de adjuntos

1. Menú izquierdo → **Storage** → **New bucket**.
2. **Name**: `attachments` (exactamente así).
3. **Public bucket**: **desactivado** (privado). La API valida permisos antes de entregar cada archivo.
4. **Create bucket**.

### 1.4 Activar el acceso S3 y crear las claves

1. Menú izquierdo → **Project Settings** (engranaje) → **Storage**.
2. En **S3 Connection**:
   - Activa **Enable connection via S3 protocol** si está apagado y guarda.
   - Copia **Endpoint** → guárdalo como **S3_ENDPOINT** (algo como `https://abcdefghijkl.supabase.co/storage/v1/s3`).
   - Copia **Region** → **S3_REGION** (p. ej. `us-west-1`).
3. En **S3 Access Keys** → **New access key** → descripción `projectm-api` → **Create access key**.
4. Copia **Access key ID** → **S3_KEY_ID** y **Secret access key** → **S3_SECRET**.
   - ⚠️ El secreto se muestra **una sola vez**.

✅ Supabase listo. Tienes: CADENA_DB, S3_ENDPOINT, S3_REGION, S3_KEY_ID, S3_SECRET.

---

## Paso 2. Render (API .NET)

### 2.1 Crear el servicio desde el blueprint

1. Entra a <https://dashboard.render.com> → **New** → **Blueprint**.
2. Conecta GitHub si te lo pide y selecciona el repositorio **`ludwigramirez-source/projectM`**.
3. **Branch**: selecciona **`vercel`**.
   - ⚠️ `main` no tiene el blueprint ni PostgreSQL.
4. **Blueprint Name**: `projectm`. Render detecta el archivo `render.yaml` y muestra el servicio **`projectm-api`** (Docker, plan Starter).

### 2.2 Completar las variables secretas

Render muestra un formulario con las variables marcadas como secretas. Completa:

| Variable | Valor |
|---|---|
| `ConnectionStrings__Default` | **CADENA_DB** (paso 1.2) |
| `Cors__AllowedOrigins` | Por ahora `https://localhost` (se corrige en el paso 4) |
| `Seed__AdminEmail` | El correo con el que entrarás como Admin |
| `Seed__AdminPassword` | La contraseña del paso 0 |
| `Storage__S3__ServiceUrl` | **S3_ENDPOINT** |
| `Storage__S3__Region` | **S3_REGION** |
| `Storage__S3__AccessKeyId` | **S3_KEY_ID** |
| `Storage__S3__SecretAccessKey` | **S3_SECRET** |

El resto ya viene definido en el blueprint:
- `Jwt__Key` (se genera sola)
- puerto 8080
- zona horaria `America/Bogota`
- cookies seguras
- `Proxy__ForwardLimit=2`
- almacenamiento S3 con el bucket `attachments`

> ¿Otra zona horaria? Después del despliegue: servicio → **Environment** → `App__TimeZone` (formato IANA, p. ej. `America/Mexico_City`).

Pulsa **Apply** (o **Deploy Blueprint**).

### 2.3 Esperar el primer despliegue

1. Entra al servicio **projectm-api** → pestaña **Logs**. La primera compilación de Docker tarda 5–10 minutos.
2. Al final deberías ver líneas como:
   ```
   Applying migration '..._Inicial'
   Admin inicial creado: tu-correo@...
   Now listening on: http://[::]:8080
   ```
   La línea `Failed executing DbCommand ... __EFMigrationsHistory` del primer arranque es normal: la tabla todavía no existía.
3. Copia la URL del servicio (arriba, bajo el nombre), por ejemplo `https://projectm-api.onrender.com` → **URL_API**.
   - Si el nombre estaba ocupado, Render agrega un sufijo (p. ej. `projectm-api-x1y2.onrender.com`). Anota la URL real.
4. Abre en el navegador `URL_API/health`. Debe responder:
   ```json
   {"status":"ok"}
   ```

### 2.4 Solo si la URL_API no es `https://projectm-api.onrender.com`

El frontend reenvía `/api` a esa dirección y está escrita en `frontend/vercel.json`. Si tu URL es distinta:

1. En GitHub abre `frontend/vercel.json` **en la rama `vercel`** → ícono de lápiz (Edit).
2. Cambia la línea:
   ```json
   { "source": "/api/:path*", "destination": "https://projectm-api.onrender.com/api/:path*" },
   ```
   por tu URL real (conserva `/api/:path*` al final).
3. **Commit changes** directamente en la rama `vercel`.

✅ Render listo. Tienes: **URL_API**.

---

## Paso 3. Vercel (frontend)

### 3.1 Importar el proyecto

1. Entra a <https://vercel.com/new> → **Import Git Repository** → elige **`projectM`** → **Import**.
2. Configura:
   - **Project Name**: `projectm`. Tu URL será `https://projectm.vercel.app` o, si está ocupado, una variante.
   - **Framework Preset**: **Vite**.
   - **Root Directory**: pulsa **Edit** → selecciona la carpeta **`frontend`** → **Continue**.
   - Build Command / Output Directory: déjalos por defecto (los define `frontend/vercel.json`).
3. Despliega **Environment Variables** y agrega **antes** del primer despliegue:

   | Name | Value |
   |---|---|
   | `VITE_DIRECT_API_URL` | **URL_API**, sin barra al final (p. ej. `https://projectm-api.onrender.com`) |

   **No** crees `VITE_API_URL`: vacía hace que las llamadas pasen por el proxy `/api` del mismo dominio, que es lo que permite que la sesión funcione.
4. Pulsa **Deploy**. Este primer despliegue puede salir de `main`: no importa, en el siguiente paso cambias la rama.

### 3.2 Publicar la rama `vercel` en producción

1. Proyecto → **Settings** → **Environments** → **Production** → en **Branch Tracking** escribe `vercel` → **Save**.
   - En versiones anteriores del panel está en **Settings → Git → Production Branch**.
2. Pestaña **Deployments** → menú **⋯** del último despliegue → **Redeploy**. O espera al siguiente push a `vercel`.
3. Cuando quede en **Ready**, copia el dominio de producción (pestaña **Overview** → **Domains**), p. ej. `https://projectm.vercel.app` → **URL_WEB**.

> Las variables `VITE_*` se incrustan al compilar. Si las cambias después, **haz Redeploy** para que tomen efecto.

✅ Vercel listo. Tienes: **URL_WEB**.

---

## Paso 4. Conectar Render con Vercel (CORS)

1. Render → servicio **projectm-api** → **Environment**.
2. Edita `Cors__AllowedOrigins` = **URL_WEB**, sin barra final (p. ej. `https://projectm.vercel.app`).
   - Para permitir varios dominios, sepáralos con coma.
3. **Save Changes**. Render redespliega solo (1–3 minutos).

Sin este paso el login funciona (va por el proxy de Vercel), pero **fallan las notificaciones en vivo y los adjuntos**, que van directo a Render.

---

## Paso 5. Verificar

1. Abre **URL_WEB** → inicia sesión con `Seed__AdminEmail` / `Seed__AdminPassword`.
2. Recarga la página (F5): la sesión debe mantenerse.
3. Crea un espacio y un proyecto con la plantilla "Proyecto estándar"; abre una tarea y **adjunta un archivo**.
   - En Supabase → Storage → `attachments` debe aparecer el archivo.
   - Descárgalo desde la tarea.
4. **Notificaciones en vivo**: DevTools (F12) → **Network** → filtro **WS** → debe haber una conexión a `wss://<URL_API>/hubs/notifications` con estado **101**.
5. **Instalar como app**:
   - **Android/escritorio**: menú de usuario → **Instalar app**.
   - **iPhone**: Safari → Compartir → **Agregar a pantalla de inicio**.
6. Cambia la contraseña del Admin si usaste una temporal, crea los usuarios del equipo desde **Administración → Usuarios** y, si quieres, borra `Seed__AdminPassword` de Render (el Admin ya existe y no se vuelve a crear).

### Datos de demostración (opcional)

Para una instancia de prueba, agrega en Render `Seed__Demo=true` y `Seed__DemoPassword=<clave>`. Se cargan una sola vez si no existen; los usuarios demo son `@projectm.demo` (ver README).

---

## Problemas frecuentes

| Síntoma | Causa probable | Solución |
|---|---|---|
| Render: `Failed to connect` / timeout a la base | Cadena de **Direct connection** (IPv6) o contraseña sin reemplazar | Usa la del **Session pooler** (paso 1.2) con la contraseña real y `;SSL Mode=Require` |
| Render: `Jwt:Key debe configurarse` | Variable vacía | Environment → `Jwt__Key` → **Generate** |
| `/health` responde 503 | La API no llega a la base | Revisa `ConnectionStrings__Default`; el proyecto de Supabase podría estar pausado |
| Al entrar dice "No se pudo conectar con el servidor" | `vercel.json` apunta a otra URL de Render | Paso 2.4 y redeploy en Vercel |
| Login OK, pero al recargar vuelve al login | Se definió `VITE_API_URL` con la URL de Render | Elimina `VITE_API_URL` en Vercel y haz Redeploy |
| Consola: error de **CORS** al conectar `/hubs` o subir archivos | `Cors__AllowedOrigins` no coincide exactamente con URL_WEB | Paso 4 (sin barra final, con `https://`) |
| Subir adjunto falla con 500 | Claves S3, región o bucket incorrectos | Revisa `Storage__S3__*` y que el bucket se llame `attachments` |
| Tras unos minutos sin uso tarda ~1 min en responder | Plan **Free** de Render (se duerme) | Plan **Starter** |
| Login bloqueado tras varios intentos | Bloqueo de cuenta (5 fallos → 15 min) | Esperar, o que un Admin restablezca la contraseña |

**Logs útiles**: Render → servicio → **Logs**; Vercel → Deployments → despliegue → **Build Logs** / **Runtime Logs**; Supabase → **Logs**.

---

## Actualizaciones futuras

- Cada `git push` a la rama `vercel` redespliega **Render** (API) y **Vercel** (frontend) automáticamente.
- Las migraciones de base de datos se aplican solas al arrancar la API.
- La PWA instalada muestra "Hay una nueva versión" y se actualiza al pulsar **Actualizar**.

## Costos y límites a tener en cuenta

- **Vercel Hobby** es para uso personal y no comercial; para una herramienta interna de empresa corresponde **Pro**.
- **Render Free** se suspende tras ~15 minutos sin tráfico: corta SignalR y el aviso de vencimientos. **Starter** mantiene la API encendida.
- **Supabase Free**:
  - 500 MB de base y 1 GB de Storage.
  - Los proyectos inactivos una semana se **pausan**; se reactivan desde el panel.
  - Para producción conviene **Pro**, que además incluye copias de seguridad diarias.

## Seguridad

- No hay secretos en el repositorio: todos viven en Render y Vercel.
- El bucket es privado; los archivos solo se descargan a través de la API, que valida permisos.
- El token de SignalR viaja en la URL del WebSocket (limitación del navegador). Dura 15 minutos y nginx no lo registra; no actives logs que guarden URLs completas.
- El rate limiting del login usa la IP real gracias a `Proxy__ForwardLimit=2` (Vercel + balanceador de Render). Si cambias la cadena de proxies, ajústalo.

## Más adelante: dominio propio

Con `app.tuempresa.com` en Vercel y `api.tuempresa.com` en Render:

1. Vercel → **Settings → Domains** → agrega `app.tuempresa.com`. Render → **Settings → Custom Domains** → agrega `api.tuempresa.com`. Crea los registros DNS que cada panel indica.
2. Vercel:
   - `VITE_API_URL=https://api.tuempresa.com` y `VITE_DIRECT_API_URL=https://api.tuempresa.com`.
   - Quita el rewrite de `/api` en `frontend/vercel.json` y haz Redeploy.
3. Render: `Cors__AllowedOrigins=https://app.tuempresa.com` y `Proxy__ForwardLimit=1`.

La cookie de sesión sigue funcionando (mismo sitio) y desaparece el salto extra por el proxy de Vercel.

---

## Anexo A. Hoja de valores

Copia esto en una nota privada (no en el repositorio) y complétala a medida que avanzas:

```
Supabase
  CADENA_DB    = User Id=...;Password=...;Server=...pooler.supabase.com;Port=5432;Database=postgres;SSL Mode=Require
  S3_ENDPOINT  = https://<ref>.supabase.co/storage/v1/s3
  S3_REGION    =
  S3_KEY_ID    =
  S3_SECRET    =
Render
  URL_API      = https://
Vercel
  URL_WEB      = https://
Admin
  correo       =
  contraseña   = (guárdala en tu gestor de contraseñas)
```

## Anexo B. Desarrollo local

Sin cambios respecto a `main`, salvo que usa PostgreSQL en lugar de SQL Server:
- `docker compose up -d --build` levanta `db` (postgres:16), `api` y `web` en <http://localhost:8080>.
- `powershell -File scripts/test.ps1` corre las pruebas contra ese PostgreSQL.
