import { createBrowserRouter } from 'react-router-dom'
import { MainLayout, RequireRole } from '@/components/layout/MainLayout'
import { LoginPage } from '@/features/auth/LoginPage'
import { DashboardPage } from '@/features/dashboard/DashboardPage'
import { UsersPage } from '@/features/admin/UsersPage'
import { TemplatesPage } from '@/features/admin/TemplatesPage'
import { ClosureChecklistPage } from '@/features/admin/ClosureChecklistPage'
import { DesignSystemPage } from '@/features/design/DesignSystemPage'
import { ProjectsPage } from '@/features/projects/ProjectsPage'
import { NewProjectPage } from '@/features/projects/NewProjectPage'
import { ProjectPage } from '@/features/projects/ProjectPage'
import { MyTasksPage } from '@/features/tasks/MyTasksPage'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    element: <MainLayout />,
    children: [
      { index: true, element: <DashboardPage /> },
      { path: 'my-tasks', element: <MyTasksPage /> },
      { path: 'projects', element: <ProjectsPage /> },
      {
        path: 'projects/new',
        element: (
          <RequireRole roles={['Admin', 'Gestor']}>
            <NewProjectPage />
          </RequireRole>
        ),
      },
      { path: 'projects/:projectId/:tab?', element: <ProjectPage /> },
      {
        path: 'admin/users',
        element: (
          <RequireRole roles={['Admin']}>
            <UsersPage />
          </RequireRole>
        ),
      },
      {
        path: 'admin/templates',
        element: (
          <RequireRole roles={['Admin', 'Gestor']}>
            <TemplatesPage />
          </RequireRole>
        ),
      },
      {
        path: 'admin/closure-checklist',
        element: (
          <RequireRole roles={['Admin']}>
            <ClosureChecklistPage />
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
      { path: '*', element: <DashboardPage /> },
    ],
  },
])
