import {
  useId,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react'
import { cn } from '@/lib/cn'

const control =
  'w-full rounded-md border bg-surface px-3 text-sm text-fg placeholder:text-fg-muted ' +
  'transition-colors focus:border-primary-600 focus:outline-none focus:ring-2 focus:ring-primary-200 ' +
  'disabled:bg-surface-muted disabled:text-fg-muted'

interface FieldShellProps {
  label?: string
  error?: string
  hint?: string
  required?: boolean
  children: (id: string, describedBy?: string) => ReactNode
  className?: string
}

function FieldShell({ label, error, hint, required, children, className }: FieldShellProps) {
  const id = useId()
  const descId = error || hint ? `${id}-desc` : undefined
  return (
    <div className={cn('flex flex-col gap-1', className)}>
      {label && (
        <label htmlFor={id} className="text-xs font-medium text-fg">
          {label}
          {required && <span className="ml-0.5 text-danger-500">*</span>}
        </label>
      )}
      {children(id, descId)}
      {(error || hint) && (
        <p id={descId} className={cn('text-xs', error ? 'text-danger-500' : 'text-fg-muted')}>
          {error ?? hint}
        </p>
      )}
    </div>
  )
}

type Common = { label?: string; error?: string; hint?: string; wrapperClassName?: string }

export function Input({
  label,
  error,
  hint,
  wrapperClassName,
  className,
  required,
  ...rest
}: Common & InputHTMLAttributes<HTMLInputElement> & { ref?: React.Ref<HTMLInputElement> }) {
  return (
    <FieldShell label={label} error={error} hint={hint} required={required} className={wrapperClassName}>
      {(id, desc) => (
        <input
          id={id}
          aria-invalid={!!error}
          aria-describedby={desc}
          required={required}
          className={cn(control, 'h-9', error ? 'border-danger-500' : 'border-border', className)}
          {...rest}
        />
      )}
    </FieldShell>
  )
}

export function Textarea({
  label,
  error,
  hint,
  wrapperClassName,
  className,
  required,
  ...rest
}: Common & TextareaHTMLAttributes<HTMLTextAreaElement> & { ref?: React.Ref<HTMLTextAreaElement> }) {
  return (
    <FieldShell label={label} error={error} hint={hint} required={required} className={wrapperClassName}>
      {(id, desc) => (
        <textarea
          id={id}
          aria-invalid={!!error}
          aria-describedby={desc}
          required={required}
          className={cn(control, 'min-h-20 py-2', error ? 'border-danger-500' : 'border-border', className)}
          {...rest}
        />
      )}
    </FieldShell>
  )
}

export function Select({
  label,
  error,
  hint,
  wrapperClassName,
  className,
  required,
  children,
  ...rest
}: Common & SelectHTMLAttributes<HTMLSelectElement> & { ref?: React.Ref<HTMLSelectElement> }) {
  return (
    <FieldShell label={label} error={error} hint={hint} required={required} className={wrapperClassName}>
      {(id, desc) => (
        <select
          id={id}
          aria-invalid={!!error}
          aria-describedby={desc}
          required={required}
          className={cn(control, 'h-9 pr-8', error ? 'border-danger-500' : 'border-border', className)}
          {...rest}
        >
          {children}
        </select>
      )}
    </FieldShell>
  )
}

export function Checkbox({
  label,
  className,
  ...rest
}: { label: ReactNode } & InputHTMLAttributes<HTMLInputElement>) {
  return (
    <label className={cn('inline-flex cursor-pointer items-center gap-2 text-sm', className)}>
      <input type="checkbox" className="size-4 rounded border-border accent-primary-700" {...rest} />
      {label}
    </label>
  )
}
