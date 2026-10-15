import { useMemo, useRef, useState, type ReactNode } from 'react'
import { Pencil, Send, Trash2 } from 'lucide-react'
import { useCommentMutations, useComments, useTaskActivity, type Comment } from '@/api/collab'
import type { MemberDto } from '@/api/projects'
import { errorMessage } from '@/api/client'
import { Avatar, Button, PageLoader, Tabs, useToast } from '@/components/ui'
import { cn } from '@/lib/cn'
import { fmtDateTime, fmtRelative } from '@/lib/format'

/** Comentarios (con @menciones) e historial de una tarea. */
export function TaskConversation({ taskId, projectId, members, canComment }: { taskId: string; projectId: string; members: MemberDto[]; canComment: boolean }) {
  const [tab, setTab] = useState<'comentarios' | 'actividad'>('comentarios')
  const { data: comments } = useComments(taskId)
  return (
    <section>
      <Tabs
        value={tab}
        onChange={setTab}
        items={[
          { value: 'comentarios', label: `Comentarios${comments?.totalCount ? ` (${comments.totalCount})` : ''}` },
          { value: 'actividad', label: 'Actividad' },
        ]}
      />
      <div className="pt-3">
        {tab === 'comentarios' ? (
          <Comments taskId={taskId} projectId={projectId} members={members} canComment={canComment} />
        ) : (
          <TaskActivityList taskId={taskId} />
        )}
      </div>
    </section>
  )
}

function Comments({ taskId, projectId, members, canComment }: { taskId: string; projectId: string; members: MemberDto[]; canComment: boolean }) {
  const { data, isLoading } = useComments(taskId)
  const m = useCommentMutations(taskId, projectId)
  const toast = useToast()
  const [editing, setEditing] = useState<string | null>(null)
  if (isLoading) return <PageLoader />
  return (
    <div className="space-y-3">
      {data?.items.length === 0 && <p className="text-sm text-fg-muted">Sé el primero en comentar.</p>}
      <ul className="space-y-3">
        {data?.items.map((c) => (
          <li key={c.id} className="flex gap-2.5">
            <Avatar name={c.authorName} size="sm" className="mt-0.5" />
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-baseline gap-x-2 text-xs">
                <span className="font-semibold text-fg">{c.authorName}</span>
                <span className="text-fg-muted" title={fmtDateTime(c.createdAt)}>{fmtRelative(c.createdAt)}</span>
                {c.editedAt && <span className="text-fg-muted">(editado)</span>}
              </div>
              {editing === c.id ? (
                <Composer
                  members={members}
                  initial={c.body}
                  initialMentions={c.mentions}
                  submitLabel="Guardar"
                  loading={m.edit.isPending}
                  onCancel={() => setEditing(null)}
                  onSubmit={(body, mentionIds) =>
                    m.edit.mutate({ id: c.id, body, mentionIds }, { onSuccess: () => setEditing(null), onError: (e) => toast.error(errorMessage(e)) })
                  }
                />
              ) : (
                <div className="group mt-0.5 rounded-md bg-surface-muted px-3 py-2 text-sm">
                  <p className="break-words whitespace-pre-wrap"><CommentBody comment={c} members={members} /></p>
                  {c.canEdit && (
                    <div className="mt-1 flex gap-2 text-xs text-fg-muted opacity-0 transition-opacity group-focus-within:opacity-100 group-hover:opacity-100">
                      <button onClick={() => setEditing(c.id)} className="inline-flex items-center gap-1 hover:text-fg"><Pencil className="size-3" /> Editar</button>
                      <button onClick={() => confirm('¿Eliminar el comentario?') && m.remove.mutate(c.id)} className="inline-flex items-center gap-1 hover:text-danger-500"><Trash2 className="size-3" /> Eliminar</button>
                    </div>
                  )}
                </div>
              )}
            </div>
          </li>
        ))}
      </ul>
      {canComment && (
        <Composer
          members={members}
          submitLabel="Comentar"
          loading={m.add.isPending}
          onSubmit={(body, mentionIds, reset) => m.add.mutate({ body, mentionIds }, { onSuccess: reset, onError: (e) => toast.error(errorMessage(e)) })}
        />
      )}
    </div>
  )
}

/** Resalta las menciones (@Nombre) de usuarios mencionados. */
function CommentBody({ comment, members }: { comment: Comment; members: MemberDto[] }) {
  const names = members.filter((m) => comment.mentions.includes(m.userId)).map((m) => m.fullName)
  if (!names.length) return <>{comment.body}</>
  const pattern = new RegExp(`(@(?:${names.map((n) => n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')}))`, 'g')
  // Con un grupo de captura, split deja las menciones en las posiciones impares.
  const parts: ReactNode[] = comment.body.split(pattern).map((part, i) =>
    i % 2 === 1 ? (
      <span key={i} className="rounded bg-primary-100 px-0.5 font-medium text-primary-800">{part}</span>
    ) : (
      part
    ),
  )
  return <>{parts}</>
}

function Composer({
  members,
  initial = '',
  initialMentions = [],
  submitLabel,
  loading,
  onSubmit,
  onCancel,
}: {
  members: MemberDto[]
  initial?: string
  initialMentions?: string[]
  submitLabel: string
  loading?: boolean
  onSubmit: (body: string, mentionIds: string[], reset: () => void) => void
  onCancel?: () => void
}) {
  const [body, setBody] = useState(initial)
  const [mentions, setMentions] = useState<string[]>(initialMentions)
  const [query, setQuery] = useState<string | null>(null)
  const [highlight, setHighlight] = useState(0)
  const ref = useRef<HTMLTextAreaElement>(null)

  const suggestions = useMemo(
    () => (query === null ? [] : members.filter((m) => m.fullName.toLowerCase().includes(query.toLowerCase())).slice(0, 6)),
    [query, members],
  )

  const onChange = (value: string) => {
    setBody(value)
    const caret = ref.current?.selectionStart ?? value.length
    const match = /(^|\s)@([\p{L}\s]{0,30})$/u.exec(value.slice(0, caret))
    setQuery(match ? match[2]! : null)
    setHighlight(0)
  }

  const pick = (m: MemberDto) => {
    const el = ref.current
    const caret = el?.selectionStart ?? body.length
    const before = body.slice(0, caret).replace(/@([\p{L}\s]{0,30})$/u, `@${m.fullName} `)
    setBody(before + body.slice(caret))
    setMentions((x) => (x.includes(m.userId) ? x : [...x, m.userId]))
    setQuery(null)
    requestAnimationFrame(() => el?.focus())
  }

  const submit = () => {
    const text = body.trim()
    if (!text) return
    // Solo cuenta como mención si el texto conserva "@Nombre".
    const ids = mentions.filter((id) => text.includes(`@${members.find((m) => m.userId === id)?.fullName}`))
    onSubmit(text, ids, () => {
      setBody('')
      setMentions([])
    })
  }

  return (
    <div className="relative">
      <textarea
        ref={ref}
        aria-label="Escribe un comentario"
        rows={3}
        placeholder="Escribe un comentario… usa @ para mencionar"
        className="w-full rounded-md border border-border bg-surface px-3 py-2 text-sm placeholder:text-fg-muted focus:border-primary-600 focus:ring-2 focus:ring-primary-200 focus:outline-none"
        value={body}
        onChange={(e) => onChange(e.target.value)}
        onKeyDown={(e) => {
          if (suggestions.length) {
            if (e.key === 'ArrowDown') return e.preventDefault(), setHighlight((h) => (h + 1) % suggestions.length)
            if (e.key === 'ArrowUp') return e.preventDefault(), setHighlight((h) => (h - 1 + suggestions.length) % suggestions.length)
            if (e.key === 'Enter' || e.key === 'Tab') return e.preventDefault(), pick(suggestions[highlight]!)
            if (e.key === 'Escape') return setQuery(null)
          }
          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) submit()
        }}
      />
      {suggestions.length > 0 && (
        <ul role="listbox" aria-label="Mencionar" className="absolute bottom-full left-0 z-10 mb-1 w-64 overflow-hidden rounded-md border border-border bg-surface shadow-lg">
          {suggestions.map((m, i) => (
            <li key={m.userId} role="option" aria-selected={i === highlight}>
              <button type="button" onMouseDown={(e) => (e.preventDefault(), pick(m))} className={cn('flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm', i === highlight && 'bg-primary-50')}>
                <Avatar name={m.fullName} size="sm" /> {m.fullName}
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className="mt-1 flex items-center justify-between gap-2">
        <span className="text-xs text-fg-muted">Ctrl + Enter para enviar</span>
        <div className="flex gap-2">
          {onCancel && <Button size="sm" variant="secondary" onClick={onCancel}>Cancelar</Button>}
          <Button size="sm" icon={<Send className="size-3.5" />} disabled={!body.trim()} loading={loading} onClick={submit}>{submitLabel}</Button>
        </div>
      </div>
    </div>
  )
}

function TaskActivityList({ taskId }: { taskId: string }) {
  const { data, isLoading } = useTaskActivity(taskId)
  if (isLoading) return <PageLoader />
  if (!data?.length) return <p className="text-sm text-fg-muted">Sin actividad registrada.</p>
  return <ActivityFeed items={data} />
}

export function ActivityFeed({ items }: { items: { id: string; actorName: string; message: string; createdAt: string; type: string }[] }) {
  return (
    <ol className="relative space-y-3 border-l border-border pl-4">
      {items.map((a) => (
        <li key={a.id} className="relative text-sm">
          <span className={cn('absolute top-1.5 -left-[21px] size-2.5 rounded-full border-2 border-surface',
            a.type.includes('Closed') || a.type === 'TaskDeleted' ? 'bg-danger-500' : a.type.includes('Status') ? 'bg-accent-500' : 'bg-primary-500')} />
          <span className="font-medium">{a.actorName}</span> <span>{a.message}</span>
          <div className="text-xs text-fg-muted" title={fmtDateTime(a.createdAt)}>{fmtRelative(a.createdAt)}</div>
        </li>
      ))}
    </ol>
  )
}
