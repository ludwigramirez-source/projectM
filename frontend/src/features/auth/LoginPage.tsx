import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { AlertTriangle } from 'lucide-react'
import { Button, Input } from '@/components/ui'
import { useAuth } from '@/app/auth'
import { ApiError } from '@/api/client'
import { ThemeSwitch } from '@/app/theme'

const schema = z.object({
  email: z.email('Ingresa un correo válido'),
  password: z.string().min(1, 'Ingresa tu contraseña'),
})
type Form = z.infer<typeof schema>

export function LoginPage() {
  const { user, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Form>({ resolver: zodResolver(schema) })

  const from = (location.state as { from?: string } | null)?.from ?? '/'
  if (user) return <Navigate to={from} replace />

  const onSubmit = async (data: Form) => {
    setError(null)
    try {
      await login(data.email, data.password)
      navigate(from, { replace: true })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'No se pudo conectar con el servidor.')
    }
  }

  return (
    <div className="flex min-h-full">
      <div className="hidden w-1/2 flex-col justify-between bg-sidebar p-10 text-sidebar-fg lg:flex">
        <div className="flex items-center gap-2">
          <span className="flex size-9 items-center justify-center rounded-md bg-accent-solid font-bold text-on-accent-solid">PM</span>
          <span className="text-lg font-semibold">ProjectM</span>
        </div>
        <div>
          <h1 className="text-3xl leading-tight font-semibold">
            Planifica, ejecuta y cierra
            <br />
            tus proyectos en un solo lugar.
          </h1>
          <p className="mt-3 max-w-md text-sidebar-fg-muted">
            Espacios, listas, tableros Kanban, calendario y un ciclo de vida completo desde la apertura hasta el acta
            de cierre.
          </p>
        </div>
        <div className="flex gap-2">
          {['bg-primary-mark', 'bg-accent-mark', 'bg-success-mark', 'bg-secondary-mark', 'bg-danger-mark'].map((c) => (
            <span key={c} className={`h-1.5 w-10 rounded-full ${c}`} />
          ))}
        </div>
      </div>
      <div className="relative flex flex-1 items-center justify-center p-6">
        <ThemeSwitch className="absolute top-4 right-4" />
        <form onSubmit={handleSubmit(onSubmit)} className="w-full max-w-sm space-y-4" noValidate>
          <div>
            <h2 className="text-xl font-semibold">Iniciar sesión</h2>
            <p className="text-sm text-fg-muted">Usa tu correo corporativo.</p>
          </div>
          {error && (
            <div role="alert" className="flex items-start gap-2 rounded-md bg-danger-subtle p-3 text-sm text-danger-subtle-fg">
              <AlertTriangle className="mt-0.5 size-4 shrink-0" />
              {error}
            </div>
          )}
          <Input label="Correo" type="email" autoComplete="username" autoFocus error={errors.email?.message} {...register('email')} />
          <Input
            label="Contraseña"
            type="password"
            autoComplete="current-password"
            error={errors.password?.message}
            {...register('password')}
          />
          <Button type="submit" className="w-full" loading={isSubmitting}>
            Entrar
          </Button>
        </form>
      </div>
    </div>
  )
}
