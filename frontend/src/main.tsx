import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router-dom'
import './index.css'
import { ToastProvider } from '@/components/ui'
import { AuthProvider } from '@/app/auth'
import { ThemeProvider } from '@/app/theme'
import { RealtimeProvider } from '@/app/realtime'
import { router } from '@/app/router'
import { ApiError } from '@/api/client'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 15_000,
      refetchOnWindowFocus: false,
      retry: (count, err) => !(err instanceof ApiError && err.status < 500) && count < 2,
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <ToastProvider>
          <AuthProvider>
            <RealtimeProvider>
              <RouterProvider router={router} />
            </RealtimeProvider>
          </AuthProvider>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>
  </StrictMode>,
)
