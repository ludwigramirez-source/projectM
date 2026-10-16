/**
 * Cliente HTTP de la API.
 * - El access token vive solo en memoria; el refresh token es una cookie HttpOnly.
 * - Ante un 401 intenta un único refresh (compartido entre peticiones concurrentes) y reintenta.
 * - Los errores ProblemDetails se convierten en ApiError con `code` estable.
 */

const BASE = '/api/v1'

let accessToken: string | null = null
let onSessionLost: (() => void) | null = null
let refreshing: Promise<AuthResponse | null> | null = null

export interface UserInfo {
  id: string
  email: string
  fullName: string
  role: Role
}
export type Role = 'Admin' | 'Gestor' | 'Miembro' | 'Observador'

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  user: UserInfo
}

export class ApiError extends Error {
  status: number
  code: string
  errors?: Record<string, string[]>
  constructor(status: number, code: string, message: string, errors?: Record<string, string[]>) {
    super(message)
    this.status = status
    this.code = code
    this.errors = errors
  }
  /** Primer mensaje de error de un campo (para formularios). */
  field(name: string) {
    return this.errors?.[name]?.[0]
  }
}

export const setAccessToken = (t: string | null) => {
  accessToken = t
}
export const getAccessToken = () => accessToken
export const setOnSessionLost = (fn: () => void) => {
  onSessionLost = fn
}

/** POST de refresh; ante 429 (límite por IP) reintenta con espera en vez de cerrar la sesión. */
async function fetchRefresh(attempt = 0): Promise<Response> {
  const res = await fetch(`${BASE}/auth/refresh`, { method: 'POST', credentials: 'include' })
  if (res.status !== 429 || attempt >= 3) return res
  const wait = Number(res.headers.get('Retry-After')) * 1000 || 1500 * (attempt + 1)
  await new Promise((r) => setTimeout(r, wait))
  return fetchRefresh(attempt + 1)
}

/** Renueva la sesión con la cookie de refresh. Single-flight. */
export function refreshSession(): Promise<AuthResponse | null> {
  refreshing ??= fetchRefresh()
    .then(async (r) => {
      if (!r.ok) return null
      const body = (await r.json()) as AuthResponse
      accessToken = body.accessToken
      return body
    })
    .catch(() => null)
    .finally(() => {
      refreshing = null
    })
  return refreshing
}

type Query = Record<string, string | number | boolean | null | undefined | string[]>

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  body?: unknown
  query?: Query
  signal?: AbortSignal
  /** Devuelve la Response sin parsear (descargas). */
  raw?: boolean
}

export function buildUrl(path: string, query?: Query) {
  const url = new URL(BASE + path, window.location.origin)
  for (const [k, v] of Object.entries(query ?? {})) {
    if (v === undefined || v === null || v === '') continue
    if (Array.isArray(v)) v.forEach((x) => url.searchParams.append(k, x))
    else url.searchParams.set(k, String(v))
  }
  return url.pathname + url.search
}

async function send(path: string, opts: RequestOptions): Promise<Response> {
  const headers: Record<string, string> = {}
  let body: BodyInit | undefined
  if (opts.body instanceof FormData) body = opts.body
  else if (opts.body !== undefined) {
    headers['Content-Type'] = 'application/json'
    body = JSON.stringify(opts.body)
  }
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`
  return fetch(buildUrl(path, opts.query), {
    method: opts.method ?? 'GET',
    headers,
    body,
    signal: opts.signal,
    credentials: 'include',
  })
}

async function toError(res: Response): Promise<ApiError> {
  try {
    const pd = await res.json()
    return new ApiError(res.status, pd.code ?? 'ERROR', pd.title ?? 'Error inesperado', pd.errors)
  } catch {
    return new ApiError(res.status, 'ERROR', res.status >= 500 ? 'Error del servidor' : 'Error inesperado')
  }
}

export async function api<T = void>(path: string, opts: RequestOptions = {}): Promise<T> {
  let res = await send(path, opts)
  if (res.status === 401 && !path.startsWith('/auth/')) {
    const renewed = await refreshSession()
    if (renewed) res = await send(path, opts)
    else onSessionLost?.()
  }
  if (!res.ok) throw await toError(res)
  if (opts.raw) return res as unknown as T
  if (res.status === 204 || res.headers.get('content-length') === '0') return undefined as T
  const type = res.headers.get('content-type') ?? ''
  return (type.includes('json') ? await res.json() : undefined) as T
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

/** Mensaje legible para mostrar en toasts. */
export function errorMessage(e: unknown): string {
  if (e instanceof ApiError) {
    const first = e.errors && Object.values(e.errors)[0]?.[0]
    return first ?? e.message
  }
  return 'No se pudo conectar con el servidor.'
}
