import { format, formatDistanceToNow, parseISO } from 'date-fns'
import { es } from 'date-fns/locale'

export function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toUpperCase())
    .join('')
}

const toDate = (d: string | Date) => (typeof d === 'string' ? parseISO(d) : d)

/** 15 oct 2026 */
export const fmtDate = (d?: string | Date | null) => (d ? format(toDate(d), 'd MMM yyyy', { locale: es }) : '—')
/** 15 oct */
export const fmtShortDate = (d?: string | Date | null) => (d ? format(toDate(d), 'd MMM', { locale: es }) : '—')
/** 15 oct 2026, 14:30 */
export const fmtDateTime = (d?: string | Date | null) =>
  d ? format(toDate(d), "d MMM yyyy, HH:mm", { locale: es }) : '—'
/** hace 5 minutos */
export const fmtRelative = (d: string | Date) => formatDistanceToNow(toDate(d), { locale: es, addSuffix: true })

export const fmtMoney = (v?: number | null, currency = 'USD') =>
  v == null ? '—' : new Intl.NumberFormat('es', { style: 'currency', currency, maximumFractionDigits: 0 }).format(v)

/** Fecha local yyyy-MM-dd (para inputs type=date y DateOnly del backend). */
export const toDateInput = (d: Date) => format(d, 'yyyy-MM-dd')

export const isOverdue = (due?: string | null, status?: string) =>
  !!due && status !== 'Completada' && due < toDateInput(new Date())
