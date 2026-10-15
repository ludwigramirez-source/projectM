import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { getAccessToken, refreshSession } from '@/api/client'
import { collabKeys, type AppNotification } from '@/api/collab'
import { invalidateTasks } from '@/api/tasks'
import { useToast } from '@/components/ui'
import { useAuth } from './auth'

const RealtimeCtx = createContext<HubConnection | null>(null)

/**
 * Conexión SignalR del usuario autenticado: recibe notificaciones personales y cambios
 * de los proyectos abiertos (invalida la caché para refrescar vistas en vivo).
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const qc = useQueryClient()
  const toast = useToast()
  const [connection, setConnection] = useState<HubConnection | null>(null)

  useEffect(() => {
    if (!user) return
    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/notifications', {
        accessTokenFactory: async () => getAccessToken() ?? (await refreshSession())?.accessToken ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    conn.on('notification', (n: AppNotification) => {
      qc.invalidateQueries({ queryKey: collabKeys.notifications })
      toast.info(n.title)
    })
    conn.on('projectChanged', (c: { projectId: string; taskId?: string }) => {
      invalidateTasks(qc, c.projectId, c.taskId)
      qc.invalidateQueries({ queryKey: ['project', c.projectId, 'activity'] })
      if (c.taskId) {
        qc.invalidateQueries({ queryKey: collabKeys.comments(c.taskId) })
        qc.invalidateQueries({ queryKey: collabKeys.taskActivity(c.taskId) })
      }
    })

    let cancelled = false
    conn.start().then(
      () => !cancelled && setConnection(conn),
      () => undefined, // sin tiempo real: el resto de la app sigue funcionando
    )
    return () => {
      cancelled = true
      setConnection(null)
      void conn.stop()
    }
  }, [user, qc, toast])

  return <RealtimeCtx.Provider value={connection}>{children}</RealtimeCtx.Provider>
}

/** Suscribe la vista a los cambios en vivo de un proyecto mientras está montada. */
export function useProjectRealtime(projectId?: string) {
  const conn = useContext(RealtimeCtx)
  const joined = useRef<string | null>(null)
  useEffect(() => {
    if (!conn || !projectId || conn.state !== HubConnectionState.Connected) return
    conn.invoke('JoinProject', projectId).then(() => (joined.current = projectId), () => undefined)
    return () => {
      if (joined.current && conn.state === HubConnectionState.Connected) void conn.invoke('LeaveProject', joined.current).catch(() => undefined)
      joined.current = null
    }
  }, [conn, projectId])
}
