import { useState } from 'react'
import { History } from 'lucide-react'
import { useProjectActivity } from '@/api/collab'
import type { ProjectDetail } from '@/api/projects'
import { Card, EmptyState, PageLoader, Pagination } from '@/components/ui'
import { ActivityFeed } from './TaskConversation'

/** Historial completo del proyecto (paginado). */
export function ActivityTab({ project }: { project: ProjectDetail }) {
  const [page, setPage] = useState(1)
  const { data, isLoading } = useProjectActivity(project.id, page)
  return (
    <div className="mx-auto max-w-3xl p-4 sm:p-6">
      <Card title={<span className="flex items-center gap-2"><History className="size-4" /> Historial de actividad</span>}>
        {isLoading ? (
          <PageLoader />
        ) : !data?.items.length ? (
          <EmptyState title="Sin actividad" />
        ) : (
          <>
            <ActivityFeed items={data.items} />
            <Pagination page={data.page} pageSize={data.pageSize} total={data.totalCount} onChange={setPage} />
          </>
        )}
      </Card>
    </div>
  )
}
