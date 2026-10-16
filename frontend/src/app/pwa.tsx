import { useEffect, useState, useSyncExternalStore } from 'react'
import { useRegisterSW } from 'virtual:pwa-register/react'
import { RefreshCw, WifiOff, X } from 'lucide-react'
import { Button, Modal } from '@/components/ui'

// ---------------- Instalación ----------------

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

let deferred: BeforeInstallPromptEvent | null = null
const listeners = new Set<() => void>()
const notify = () => listeners.forEach((l) => l())

if (typeof window !== 'undefined') {
  // Chrome/Edge/Android: guardamos el evento para mostrar nuestro propio botón "Instalar app".
  window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault()
    deferred = e as BeforeInstallPromptEvent
    notify()
  })
  window.addEventListener('appinstalled', () => {
    deferred = null
    notify()
  })
}

const isStandalone = () =>
  typeof window !== 'undefined' &&
  (matchMedia('(display-mode: standalone)').matches || (navigator as Navigator & { standalone?: boolean }).standalone === true)

const isIos = () => typeof navigator !== 'undefined' && /iphone|ipad|ipod/i.test(navigator.userAgent)

/** Estado de instalación: si se puede ofrecer el botón y cómo instalar. */
export function useInstallApp() {
  const canPrompt = useSyncExternalStore(
    (cb) => {
      listeners.add(cb)
      return () => listeners.delete(cb)
    },
    () => deferred !== null,
    () => false,
  )
  const installed = isStandalone()
  return {
    /** Mostrar la opción "Instalar app" (Android/escritorio con prompt, o iOS con instrucciones). */
    available: !installed && (canPrompt || isIos()),
    needsIosInstructions: !installed && !canPrompt && isIos(),
    install: async () => {
      if (!deferred) return false
      await deferred.prompt()
      const { outcome } = await deferred.userChoice
      deferred = null
      notify()
      return outcome === 'accepted'
    },
  }
}

export function IosInstallHelp({ onClose }: { onClose: () => void }) {
  return (
    <Modal open size="sm" onClose={onClose} title="Instalar ProjectM" footer={<Button onClick={onClose}>Entendido</Button>}>
      <ol className="list-decimal space-y-2 pl-5 text-sm">
        <li>Abre ProjectM en <strong>Safari</strong>.</li>
        <li>
          Toca el botón <strong>Compartir</strong> (cuadro con flecha hacia arriba).
        </li>
        <li>
          Elige <strong>Agregar a pantalla de inicio</strong> y confirma.
        </li>
      </ol>
    </Modal>
  )
}

// ---------------- Actualización y conexión ----------------

function useOnline() {
  const [online, setOnline] = useState(() => (typeof navigator === 'undefined' ? true : navigator.onLine))
  useEffect(() => {
    const on = () => setOnline(true)
    const off = () => setOnline(false)
    window.addEventListener('online', on)
    window.addEventListener('offline', off)
    return () => {
      window.removeEventListener('online', on)
      window.removeEventListener('offline', off)
    }
  }, [])
  return online
}

/** Registra el service worker y muestra los avisos de nueva versión y sin conexión. */
export function PwaPrompts() {
  const {
    needRefresh: [needRefresh, setNeedRefresh],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(_url, registration) {
      // Revisa si hay una versión nueva cada hora mientras la app está abierta.
      if (registration) setInterval(() => void registration.update(), 60 * 60 * 1000)
    },
  })
  const online = useOnline()

  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-0 z-[55] flex flex-col items-center gap-2 p-3 pb-safe" aria-live="polite">
      {!online && (
        <div role="status" className="pointer-events-auto flex items-center gap-2 rounded-full bg-danger-solid px-4 py-2 text-sm text-on-danger-solid shadow-lg">
          <WifiOff className="size-4" /> Sin conexión. Los cambios no se guardarán hasta reconectar.
        </div>
      )}
      {needRefresh && (
        <div role="alert" className="pointer-events-auto flex w-full max-w-md items-center gap-3 rounded-lg border border-border bg-surface p-3 text-sm shadow-xl">
          <RefreshCw className="size-4 shrink-0 text-primary-fg" />
          <span className="flex-1">Hay una nueva versión de ProjectM.</span>
          <Button size="sm" onClick={() => void updateServiceWorker(true)}>
            Actualizar
          </Button>
          <button onClick={() => setNeedRefresh(false)} aria-label="Más tarde" className="rounded p-1 text-fg-muted hover:bg-surface-muted touch:p-2">
            <X className="size-4" />
          </button>
        </div>
      )}
    </div>
  )
}

