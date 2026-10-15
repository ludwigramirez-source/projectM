import { PageHeader } from '@/components/ui'
import { useAuth } from '@/app/auth'

/** Página de inicio (se reemplaza por el Dashboard en la fase 6). */
export function HomePage() {
  const { user } = useAuth()
  return <PageHeader title={`Hola, ${user?.fullName.split(' ')[0] ?? ''}`} subtitle="Bienvenido a ProjectM" />
}
