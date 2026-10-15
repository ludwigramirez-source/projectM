import { AppShell, Logo } from '@/components/layout/AppShell'
import { DesignSystemPage } from '@/features/design/DesignSystemPage'

export default function App() {
  return (
    <AppShell sidebar={<Logo />} topbar={<span className="font-medium">Inicio</span>}>
      <DesignSystemPage />
    </AppShell>
  )
}
