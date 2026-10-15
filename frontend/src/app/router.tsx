import { createBrowserRouter } from 'react-router-dom'
import { MainLayout, RequireRole } from '@/components/layout/MainLayout'
import { LoginPage } from '@/features/auth/LoginPage'
import { HomePage } from '@/features/home/HomePage'
import { UsersPage } from '@/features/admin/UsersPage'
import { DesignSystemPage } from '@/features/design/DesignSystemPage'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    element: <MainLayout />,
    children: [
      { index: true, element: <HomePage /> },
      {
        path: 'admin/users',
        element: (
          <RequireRole roles={['Admin']}>
            <UsersPage />
          </RequireRole>
        ),
      },
      {
        path: 'admin/design',
        element: (
          <RequireRole roles={['Admin']}>
            <DesignSystemPage />
          </RequireRole>
        ),
      },
      { path: '*', element: <HomePage /> },
    ],
  },
])
