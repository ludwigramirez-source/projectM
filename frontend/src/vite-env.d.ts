/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Origen de la API para llamadas normales (vacío = mismo origen vía proxy). */
  readonly VITE_API_URL?: string
  /** Origen de la API para SignalR y adjuntos (por defecto VITE_API_URL). */
  readonly VITE_DIRECT_API_URL?: string
}
