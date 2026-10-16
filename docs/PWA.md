# ProjectM como app móvil: evaluación PWA

## Resumen y recomendación

**Recomendación: publicar ProjectM como PWA (Progressive Web App) instalable**, reutilizando el mismo código del SPA. Solo si más adelante se exige estar en App Store / Google Play o usar APIs nativas no disponibles en la web, envolver la misma app con **Capacitor**.

| Opción | Qué es | Ventajas | Desventajas | Esfuerzo |
|---|---|---|---|---|
| **PWA** (elegida) | El SPA actual + manifest + service worker; se instala desde el navegador | Un solo código y despliegue; actualizaciones inmediatas sin tiendas; ícono en el inicio y ventana sin barra del navegador; funciona en Android, iOS y escritorio | En iOS la instalación es manual y algunas capacidades llegan más tarde; sin presencia en tiendas | Bajo (implementado en esta iteración) |
| Capacitor | El mismo SPA empaquetado en una app nativa | Publicación en tiendas; acceso a APIs nativas (biometría, archivos, push nativo) | Dos pipelines de publicación (Xcode/Android Studio), revisión de tiendas, firmas, cuentas de desarrollador | Medio |
| App nativa / React Native | Reescribir la interfaz | Máxima integración con el sistema | Duplica el desarrollo y el mantenimiento de la UI | Alto |

Para una herramienta interna de un solo equipo, la PWA cubre el caso de uso principal: consultar y actualizar tareas, recibir avisos dentro de la app y trabajar con el Kanban o la agenda desde el celular. La revisión responsive de esta iteración deja la interfaz preparada para pantallas táctiles.

## Requisitos para instalar

1. **HTTPS obligatorio** fuera de `localhost` (en la rama `vercel`, Vercel ya lo provee: ver [DEPLOY.md](DEPLOY.md)). Los navegadores solo registran service workers e instalan PWAs en orígenes seguros.
   - Producción: terminar TLS delante del contenedor `web`. La forma más simple es un proxy con certificados automáticos (por ejemplo Caddy o Traefik con Let's Encrypt) o el balanceador de la infraestructura. Otra opción es agregar `listen 443 ssl` con el certificado corporativo al nginx actual.
   - Mantén `AUTH_SECURE_COOKIES=true`: la cookie de sesión `Secure` exige HTTPS.
2. **Manifest web** (`manifest.webmanifest`) con nombre, íconos de 192 y 512 px (incluido uno *maskable* para Android), colores, `display: standalone` y `start_url`.
3. **Service worker** que controle la app (lo genera Workbox mediante `vite-plugin-pwa`).

## Estrategia de caché (segura para datos de negocio)

| Recurso | Estrategia | Motivo |
|---|---|---|
| JS, CSS, íconos, fuentes del build | *Precache* versionado | Arranque instantáneo y offline del "cascarón" de la app |
| Navegación (`/`, `/projects/...`) | Responde con `index.html` precacheado | Es un SPA; las rutas viven en el cliente |
| `/api/*`, `/hubs/*`, `/health` | **NetworkOnly** (nunca en caché) | Datos y sesión siempre frescos; nada sensible queda guardado por el service worker |

- **Sin conexión** se muestra un aviso ("Sin conexión") y la interfaz sigue abierta; las acciones que necesitan la API muestran su error habitual.
- **Actualizaciones**: al desplegar una versión nueva, la app muestra "Hay una nueva versión" con el botón *Actualizar* (no recarga sola para no perder lo que el usuario está escribiendo).

## Sesión y tiempo real en modo app

- La app instalada usa el mismo origen, así que la cookie de refresh (`HttpOnly`, `SameSite=Strict`) funciona igual que en el navegador.
- SignalR se reconecta solo al volver del segundo plano (`withAutomaticReconnect`). Mientras la app está cerrada no hay conexión: los avisos quedan en la campana y se ven al abrirla.

## Particularidades por plataforma

| | Android (Chrome/Edge) | iOS / iPadOS (Safari) | Escritorio (Chrome/Edge) |
|---|---|---|---|
| Instalación | Botón **Instalar app** del menú de usuario o aviso del navegador | Manual: Compartir → **Agregar a pantalla de inicio** (la app lo explica) | Botón **Instalar app** o ícono en la barra de direcciones |
| Ventana propia, ícono, splash | Sí | Sí | Sí |
| Push del sistema (fase siguiente) | Sí | Solo iOS 16.4+ y con la app instalada | Sí |
| Persistencia | Estable | Safari puede borrar datos de sitios poco usados; la sesión se renueva desde el servidor | Estable |

## Implementado en esta iteración

- `vite-plugin-pwa` (Workbox) con `registerType: 'prompt'`, precache del build y las reglas de caché anteriores.
- Manifest en español con atajos a **Mis tareas** y **Proyectos**, colores de la paleta e íconos (192, 512, maskable, `apple-touch-icon`) generados desde `public/favicon.svg`.
- Componentes: aviso de nueva versión, aviso sin conexión y botón **Instalar app** (con instrucciones para iOS).
- nginx: `sw.js`, `index.html` y el manifest se sirven sin caché, para que las actualizaciones lleguen enseguida.
- `index.html`: `theme-color` por tema, `viewport-fit=cover` y metadatos para iOS.

## Fases siguientes propuestas

1. **Notificaciones push (Web Push + VAPID)**: un nuevo `INotificationChannel` (`backend/src/ProjectM.Api/Infrastructure/Notifications/Channels.cs`) que envíe a las suscripciones guardadas por usuario, con una pantalla de preferencias. Encaja en la arquitectura actual sin tocar el despachador de eventos.
2. **Lectura offline**: persistir la caché de TanStack Query (IndexedDB) para ver "Mis tareas" y el último estado de los proyectos sin conexión. La edición offline se dejaría para más adelante: requiere cola de cambios y resolución de conflictos con `rowVersion`.
3. **Share target / adjuntos desde el celular**: recibir fotos o archivos compartidos y adjuntarlos a una tarea.
4. **Capacitor**: solo si se requiere publicar en tiendas o usar APIs nativas.

## Cómo probarlo

- Local: `docker compose up -d --build` y abre <http://localhost:8080> en Chrome. Ve a DevTools → Application → Manifest / Service Workers; el menú de usuario muestra **Instalar app**.
- En un celular: publica detrás de HTTPS (ver Requisitos) y abre la URL; en Android usa **Instalar app**; en iOS, Compartir → **Agregar a pantalla de inicio**.
