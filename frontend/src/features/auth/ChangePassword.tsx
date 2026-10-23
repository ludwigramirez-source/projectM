import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { AlertTriangle, KeyRound, LogOut } from 'lucide-react'
import { Button, Input, Modal, useToast } from '@/components/ui'
import { useAuth } from '@/app/auth'
import { ApiError } from '@/api/client'
import { ThemeSwitch } from '@/app/theme'

// Mismas reglas que la API (Identity): ≥ 10 caracteres con mayúscula, minúscula y número; máximo 128.
const schema = z
  .object({
    currentPassword: z.string().min(1, 'Ingresa tu contraseña actual'),
    newPassword: z
      .string()
      .min(10, 'Debe tener al menos 10 caracteres')
      .max(128, 'No puede superar 128 caracteres')
      .regex(/[A-Z]/, 'Debe incluir una mayúscula')
      .regex(/[a-z]/, 'Debe incluir una minúscula')
      .regex(/[0-9]/, 'Debe incluir un número'),
    confirm: z.string(),
  })
  .refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: 'Las contraseñas no coinciden' })
  .refine((v) => v.newPassword !== v.currentPassword, {
    path: ['newPassword'],
    message: 'Debe ser distinta de la actual',
  })
type Form = z.infer<typeof schema>

/** Formulario de cambio de contraseña. Al terminar, la sesión queda renovada (las demás se cierran). */
export function ChangePasswordForm({ onDone, submitLabel = 'Cambiar contraseña' }: { onDone?: () => void; submitLabel?: string }) {
  const { changePassword } = useAuth()
  const [error, setError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError: setFieldError,
    formState: { errors, isSubmitting },
  } = useForm<Form>({ resolver: zodResolver(schema) })

  const onSubmit = async (data: Form) => {
    setError(null)
    try {
      await changePassword(data.currentPassword, data.newPassword)
      onDone?.()
    } catch (e) {
      if (e instanceof ApiError && (e.field('currentPassword') || e.field('newPassword'))) {
        if (e.field('currentPassword')) setFieldError('currentPassword', { message: e.field('currentPassword') })
        if (e.field('newPassword')) setFieldError('newPassword', { message: e.field('newPassword') })
      } else setError(e instanceof ApiError ? e.message : 'No se pudo conectar con el servidor.')
    }
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
      {error && (
        <div role="alert" className="flex items-start gap-2 rounded-md bg-danger-subtle p-3 text-sm text-danger-subtle-fg">
          <AlertTriangle className="mt-0.5 size-4 shrink-0" />
          {error}
        </div>
      )}
      <Input
        label="Contraseña actual"
        type="password"
        autoComplete="current-password"
        error={errors.currentPassword?.message}
        {...register('currentPassword')}
      />
      <Input
        label="Nueva contraseña"
        type="password"
        autoComplete="new-password"
        hint="Mínimo 10 caracteres, con mayúscula, minúscula y número."
        error={errors.newPassword?.message}
        {...register('newPassword')}
      />
      <Input
        label="Confirmar nueva contraseña"
        type="password"
        autoComplete="new-password"
        error={errors.confirm?.message}
        {...register('confirm')}
      />
      <Button type="submit" className="w-full" loading={isSubmitting} icon={<KeyRound className="size-4" />}>
        {submitLabel}
      </Button>
    </form>
  )
}

/**
 * Pantalla obligatoria cuando la contraseña fue asignada por otra persona (Admin inicial, usuario nuevo o
 * restablecimiento). La API rechaza el resto de operaciones (403 CAMBIO_CONTRASENA_REQUERIDO) hasta cambiarla.
 */
export function ForcedPasswordChange() {
  const { user, logout } = useAuth()
  return (
    <div className="relative flex min-h-full items-center justify-center bg-bg p-6 pt-safe pb-safe">
      <ThemeSwitch className="absolute top-4 right-4" />
      <div className="w-full max-w-sm space-y-5">
        <div className="flex items-center gap-2">
          <span className="flex size-9 items-center justify-center rounded-md bg-accent-solid font-bold text-on-accent-solid">PM</span>
          <span className="text-lg font-semibold">ProjectM</span>
        </div>
        <div>
          <h1 className="text-xl font-semibold">Cambia tu contraseña</h1>
          <p className="mt-1 text-sm text-fg-muted">
            Hola{user ? `, ${user.fullName}` : ''}. Tu contraseña fue asignada por un administrador; por seguridad,
            elige una nueva antes de continuar.
          </p>
        </div>
        <ChangePasswordForm submitLabel="Guardar y continuar" />
        <button onClick={logout} className="flex items-center gap-2 text-sm text-fg-muted hover:text-fg">
          <LogOut className="size-4" /> Cerrar sesión
        </button>
      </div>
    </div>
  )
}

/** Cambio de contraseña voluntario desde el menú de usuario. */
export function ChangePasswordModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast()
  return (
    <Modal open={open} onClose={onClose} title="Cambiar contraseña" size="sm">
      <ChangePasswordForm
        onDone={() => {
          toast.success('Contraseña actualizada. Se cerraron tus otras sesiones.')
          onClose()
        }}
      />
    </Modal>
  )
}
