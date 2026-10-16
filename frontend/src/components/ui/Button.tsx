import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { cn } from '@/lib/cn'
import { Spinner } from './Spinner'

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'success'
type Size = 'sm' | 'md'

const variants: Record<Variant, string> = {
  primary: 'bg-primary-solid text-on-primary-solid hover:bg-primary-solid-hover',
  secondary: 'bg-surface text-fg border border-border hover:bg-surface-muted',
  ghost: 'text-fg hover:bg-surface-muted',
  danger: 'bg-danger-solid text-on-danger-solid hover:bg-danger-solid-hover',
  success: 'bg-success-solid text-on-success-solid hover:bg-success-solid-hover',
}

const sizes: Record<Size, string> = {
  sm: 'h-8 px-3 text-xs gap-1.5',
  md: 'h-9 px-4 text-sm gap-2',
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  loading?: boolean
  icon?: ReactNode
}

export function Button({
  variant = 'primary',
  size = 'md',
  loading,
  icon,
  className,
  children,
  disabled,
  type = 'button',
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      disabled={disabled || loading}
      className={cn(
        'inline-flex items-center justify-center rounded-md font-medium whitespace-nowrap transition-colors',
        'disabled:cursor-not-allowed disabled:opacity-50',
        variants[variant],
        sizes[size],
        !children && 'px-2',
        className,
      )}
      {...rest}
    >
      {loading ? <Spinner size="sm" /> : icon}
      {children}
    </button>
  )
}
