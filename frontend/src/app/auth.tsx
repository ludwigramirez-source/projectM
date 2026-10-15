import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import {
  api,
  refreshSession,
  setAccessToken,
  setOnSessionLost,
  type AuthResponse,
  type Role,
  type UserInfo,
} from '@/api/client'

interface AuthState {
  user: UserInfo | null
  loading: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
  hasRole: (...roles: Role[]) => boolean
}

const AuthCtx = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null)
  const [loading, setLoading] = useState(true)
  const qc = useQueryClient()

  // Restaura la sesión desde la cookie de refresh al cargar la app.
  useEffect(() => {
    refreshSession().then((r) => {
      setUser(r?.user ?? null)
      setLoading(false)
    })
    setOnSessionLost(() => {
      setAccessToken(null)
      setUser(null)
    })
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const r = await api<AuthResponse>('/auth/login', { method: 'POST', body: { email, password } })
    setAccessToken(r.accessToken)
    setUser(r.user)
  }, [])

  const logout = useCallback(async () => {
    try {
      await api('/auth/logout', { method: 'POST' })
    } finally {
      setAccessToken(null)
      setUser(null)
      qc.clear()
    }
  }, [qc])

  const hasRole = useCallback((...roles: Role[]) => !!user && roles.includes(user.role), [user])

  const value = useMemo(() => ({ user, loading, login, logout, hasRole }), [user, loading, login, logout, hasRole])
  return <AuthCtx.Provider value={value}>{children}</AuthCtx.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthCtx)
  if (!ctx) throw new Error('useAuth fuera de AuthProvider')
  return ctx
}

export const roleLabel: Record<Role, string> = {
  Admin: 'Administrador',
  Gestor: 'Gestor',
  Miembro: 'Miembro',
  Observador: 'Observador',
}
