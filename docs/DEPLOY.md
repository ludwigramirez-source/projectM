# Despliegue en Vercel + Render + Supabase

Arquitectura de producción (rama `vercel`):

```
Navegador / PWA
   │  https://projectm.vercel.app           (frontend estático + proxy de /api)
   ├──────────────► Vercel ── /api/* ──►┐
   │                                     │
   │  SignalR (WebSocket) y adjuntos     ▼
   └──────────────────────────────► Render: API .NET (Docker)  https://projectm-api.onrender.com
                                         │
                                         ├──► Supabase Postgres  (datos)
                                         └──► Supabase Storage   (adjuntos, API S3, bucket privado)
```

**Por qué así:**
- **Vercel no ejecuta .NET**, así que la API vive en Render como contenedor Docker, siempre encendida (SignalR y el aviso de vencimientos lo requieren).
- **Con dominios gratuitos**, `vercel.app` y `onrender.com` son sitios distintos y la cookie de sesión (`HttpOnly`, `Secure`, `SameSite=Strict`) no viajaría entre ellos. Por eso Vercel **reenvía `/api/*`** a Render: para el navegador, la API está en el mismo origen y la cookie funciona sin cambios.
- **El proxy de Vercel no admite WebSockets y limita el tamaño de los cuerpos (~4,5 MB).** Por eso **SignalR y los adjuntos van directo a Render** (`VITE_DIRECT_API_URL`). Esas llamadas usan el token JWT, no la cookie.

> Con un dominio propio (`app.tuempresa.com` + `api.tuempresa.com`) todo podría ir directo; ver "Dominio propio" al final.

## 1. Supabase

1. Crea un proyecto en <https://supabase.com> y elige una región cercana a la de Render (p. ej. *East US* si Render está en *Oregon/Virginia*).
2. **Base de datos**: *Project Settings → Database → Connection string*. Elige **Session pooler** (no "Transaction pooler", que no es compatible con una API que mantiene conexiones) y el formato **.NET**. Se ve así:
   ```
   Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<tu-clave>;SSL Mode=Require
   ```
   Guárdala: va en `ConnectionStrings__Default` de Render. No crees tablas: la API aplica las migraciones al arrancar.
3. **Storage**: *Storage → New bucket* → nombre `attachments`, **privado** (no marques "Public bucket").
4. **Claves S3**: *Project Settings → Storage → S3 Connection*:
   - **Endpoint** (`https://<ref>.supabase.co/storage/v1/s3`) → `Storage__S3__ServiceUrl`
   - **Region** → `Storage__S3__Region`
   - *New access key* → `Storage__S3__AccessKeyId` y `Storage__S3__SecretAccessKey` (el secreto solo se muestra una vez).

## 2. Render (API)

1. En <https://render.com>: *New → Blueprint* → conecta el repositorio `ludwigramirez-source/projectM` y la **rama `vercel`**. Render lee `render.yaml` y crea el servicio `projectm-api`.
2. Completa las variables marcadas como secretas:

   | Variable | Valor |
   |---|---|
   | `ConnectionStrings__Default` | Cadena del *Session pooler* de Supabase (paso 1.2) |
   | `Cors__AllowedOrigins` | URL de Vercel, p. ej. `https://projectm.vercel.app` (varias separadas por coma) |
   | `Seed__AdminEmail` / `Seed__AdminPassword` | Admin inicial (contraseña ≥ 10 caracteres con mayúscula, minúscula y número) |
   | `Storage__S3__ServiceUrl`, `Storage__S3__Region`, `Storage__S3__AccessKeyId`, `Storage__S3__SecretAccessKey` | Paso 1.4 |

   `Jwt__Key` se genera solo. El resto ya viene en el blueprint: zona horaria, cookies seguras, `Proxy__ForwardLimit=2` y almacenamiento S3.
3. Despliega. Cuando termine, `https://projectm-api.onrender.com/health` debe responder `{"status":"ok"}`.
   - Si Render asignó otra URL porque el nombre estaba ocupado, anótala: la necesitas en el paso 3.
   - El plan `starter` mantiene la instancia encendida. El gratuito se suspende sin tráfico y cortaría las notificaciones en vivo.

## 3. Vercel (frontend)

1. Si la URL de la API no es `https://projectm-api.onrender.com`, edita `frontend/vercel.json`, cambia el `destination` del rewrite de `/api/:path*` y haz commit en la rama `vercel`.
2. En <https://vercel.com>: *Add New → Project* → importa el repositorio y configura:
   - **Root Directory**: `frontend`
   - **Framework**: Vite (se detecta solo; `vercel.json` ya define build y salida)
   - **Production Branch**: `vercel` (*Settings → Git*)
   - **Environment Variables**:
     - `VITE_DIRECT_API_URL` = `https://projectm-api.onrender.com` (sin barra final)
     - `VITE_API_URL`: **déjala vacía** para que las llamadas usen el proxy `/api` del mismo origen.
3. Despliega y abre la URL de Vercel. Si es distinta de la que pusiste en `Cors__AllowedOrigins` de Render, actualízala allí y reinicia el servicio.
4. Inicia sesión con el Admin del paso 2. La app ya es **instalable como PWA** (HTTPS incluido): en Android usa *Instalar app* del menú de usuario; en iOS, Safari → Compartir → *Agregar a pantalla de inicio*.

## Comprobaciones

- `https://<vercel>/api/v1/auth/me` responde 401 con JSON (el proxy funciona).
- Tras iniciar sesión, en DevTools → Network aparece la conexión WebSocket a `wss://projectm-api.onrender.com/hubs/notifications` con estado 101.
- Subir y descargar un adjunto funciona, y en Supabase Storage aparece un objeto en `attachments`.
- Recargar la página mantiene la sesión.

## Datos demo (opcional)

Para una instancia de demostración pon `Seed__Demo=true` y `Seed__DemoPassword` en Render. Los datos se cargan una sola vez y solo si no existen.

## Seguridad

- Ningún secreto vive en el repositorio. Todos están en Render o Vercel.
- El bucket es privado y los adjuntos se descargan siempre a través de la API, que valida permisos.
- El JWT de SignalR viaja en la URL del WebSocket (limitación de los navegadores). Dura 15 minutos y nginx no registra query strings. Si activas logs de acceso en Render, evita guardar las URLs completas.
- El rate limiting del login usa la IP real del cliente (`Proxy__ForwardLimit=2`: Vercel + balanceador de Render). Si cambias la cadena de proxies, ajusta ese valor.

## Dominio propio (recomendado más adelante)

Con `app.tuempresa.com` en Vercel y `api.tuempresa.com` en Render (los dos sitios comparten dominio):
1. En Vercel agrega `VITE_API_URL=https://api.tuempresa.com` y `VITE_DIRECT_API_URL=https://api.tuempresa.com`, y elimina el rewrite de `/api` en `vercel.json`.
2. En Render: `Cors__AllowedOrigins=https://app.tuempresa.com` y `Proxy__ForwardLimit=1`.

Así la cookie `SameSite=Strict` sigue funcionando (mismo sitio) y desaparece el salto extra del proxy de Vercel.

## Desarrollo local

Sin cambios respecto a `main`, pero con PostgreSQL en lugar de SQL Server: `docker compose up -d --build` levanta `db` (postgres:16), `api` y `web` en <http://localhost:8080>; `powershell -File scripts/test.ps1` corre las pruebas contra ese PostgreSQL. Para probar los adjuntos con S3 en local, define `Storage__Provider=S3` y las variables `Storage__S3__*` apuntando a un servidor compatible.
