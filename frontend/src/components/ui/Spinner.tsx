import { cn } from '@/lib/cn'

export function Spinner({ size = 'md', className }: { size?: 'sm' | 'md' | 'lg'; className?: string }) {
  const s = { sm: 'size-3.5 border-2', md: 'size-5 border-2', lg: 'size-8 border-3' }[size]
  return (
    <span
      role="status"
      aria-label="Cargando"
      className={cn('inline-block animate-spin rounded-full border-current border-t-transparent', s, className)}
    />
  )
}

export function PageLoader() {
  return (
    <div className="flex h-full min-h-40 items-center justify-center text-primary-700">
      <Spinner size="lg" />
    </div>
  )
}
