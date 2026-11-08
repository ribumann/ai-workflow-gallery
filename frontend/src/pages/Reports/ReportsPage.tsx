import { useEffect, useState } from 'react'
import { useAuth } from '../../auth/useAuth'
import { canWriteCollection } from '../../services/deployment'
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom'
import {
  Badge,
  Body1,
  Button,
  Caption1,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  MessageBar,
  MessageBarBody,
  SearchBox,
  Spinner,
  Tab,
  TabList,
  type SelectTabData,
  type SelectTabEvent,
  Title1,
} from '@fluentui/react-components'
import {
  AddRegular,
  ArrowUpRightRegular,
  DeleteRegular,
  EditRegular,
  WindowRegular,
} from '@fluentui/react-icons'
import { apiClient, ApiError } from '../../services/apiClient'
import { MarkdownExcerpt } from '../../components/MarkdownExcerpt'
import type { PagedResult, ReportLink, ReportLinkKind } from '../../services/types'
import { isSafeReportUrl } from '../../utils/url'
import { reportError, useReportStyles } from './reportUi'

export function ReportsPage() {
  const styles = useReportStyles()
  const navigate = useNavigate()
  const auth = useAuth()
  const canWrite = canWriteCollection(auth.publicDemo, auth.collectionCapabilities)
  const [params, setParams] = useSearchParams()
  const q = (params.get('q') ?? '').slice(0, 200)
  const kind: ReportLinkKind | undefined =
    params.get('kind') === 'Report'
      ? 'Report'
      : params.get('kind') === 'AiApp'
        ? 'AiApp'
        : undefined
  const rawPage = Number(params.get('page') ?? 1)
  const page = Number.isSafeInteger(rawPage) && rawPage > 0 && rawPage <= 1000000 ? rawPage : 1
  const [result, setResult] = useState<PagedResult<ReportLink> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [revision, setRevision] = useState(0)
  const [deleting, setDeleting] = useState<ReportLink | null>(null)
  const [deleteError, setDeleteError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let cancelled = false
    const timeout = window.setTimeout(
      async () => {
        setLoading(true)
        setError('')
        try {
          const next = await apiClient.listReports({ q: q || undefined, kind, page, pageSize: 25 })
          if (!cancelled) setResult(next)
        } catch (err) {
          if (!cancelled) setError(reportError(err, 'Could not load the directory. Please retry.'))
        } finally {
          if (!cancelled) setLoading(false)
        }
      },
      q ? 250 : 0,
    )
    return () => {
      cancelled = true
      window.clearTimeout(timeout)
    }
  }, [q, kind, page, revision])

  function changeParams(name: string, value: string, replace = false) {
    const next = new URLSearchParams(params)
    if (value) next.set(name, value)
    else next.delete(name)
    if (name !== 'page') next.delete('page')
    if (next.toString() === params.toString()) return
    setParams(next, { replace })
  }

  function onKindTabSelect(_: SelectTabEvent, data: SelectTabData) {
    const value = data.value
    if (value === '' || value === 'Report' || value === 'AiApp') {
      changeParams('kind', value)
    }
  }

  async function remove() {
    if (!deleting) return
    setBusy(true)
    setDeleteError('')
    try {
      await apiClient.deleteReport(deleting.id)
      setDeleting(null)
      if (result?.items.length === 1 && page > 1) changeParams('page', String(page - 1))
      else setRevision((value) => value + 1)
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        setDeleting(null)
        setRevision((value) => value + 1)
      } else setDeleteError(reportError(err, 'Could not delete this link. Please retry.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <div className={styles.intro}>
          <Title1 as="h1" className={styles.heading}>
            Reports &amp; AI Dashboards
          </Title1>
          <Body1 className={styles.description}>
            A shared home for the reports and AI dashboards your team uses. Open a link here
            or in a new tab.
          </Body1>
        </div>
        {canWrite && <Button
          appearance="primary"
          icon={<AddRegular />}
          onClick={() => navigate(`/reports/new${kind ? `?kind=${kind}` : ''}`)}
        >
          Add link
        </Button>}
      </div>
      <div className={styles.tabRow}>
        <TabList
          selectedValue={kind ?? ''}
          onTabSelect={onKindTabSelect}
          aria-label="Link type"
        >
          <Tab value="">All links</Tab>
          <Tab value="Report">Reports</Tab>
          <Tab value="AiApp">AI Dashboards</Tab>
        </TabList>
      </div>
      <div className={styles.toolbar}>
        <SearchBox
          className={styles.search}
          aria-label="Search reports and AI dashboards"
          placeholder="Search by name or description"
          maxLength={200}
          value={q}
          onChange={(_, data) => changeParams('q', data.value, true)}
        />
      </div>
      {loading ? (
        <Spinner label="Loading directory..." />
      ) : error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            {error} <Button onClick={() => setRevision((value) => value + 1)}>Retry</Button>
          </MessageBarBody>
        </MessageBar>
      ) : (
        result && (
          <>
            <Caption1 aria-live="polite">
              {result.totalCount} {result.totalCount === 1 ? 'link' : 'links'}
              {kind ? ` in ${kind === 'Report' ? 'Reports' : 'AI Dashboards'}` : ''}
            </Caption1>
            {result.items.length === 0 ? (
              <div className={styles.empty}>
                <Body1>
                  <strong>
                    {q || kind
                      ? 'No matching links'
                      : page > 1
                        ? 'No links on this page'
                        : 'Your shared directory starts here'}
                  </strong>
                </Body1>
                <Body1>
                  {q || kind
                    ? 'Try another search or show all links.'
                    : 'Add a report or AI dashboard so your team can find it in one place.'}
                </Body1>
                {q || kind || page > 1 ? (
                  <Button
                    onClick={() => {
                      setLoading(true)
                      setParams({})
                    }}
                  >
                    Show all links
                  </Button>
                ) : null}
              </div>
            ) : (
              <ul className={styles.list} aria-label="Reports and AI dashboards">
                {result.items.map((item) => (
                  <li key={item.id} className={styles.row}>
                    <div className={styles.intro}>
                      <RouterLink className={styles.title} to={`/reports/${item.id}`}>
                        {item.name}
                      </RouterLink>
                      <MarkdownExcerpt
                        content={item.description}
                        lines={3}
                        className={styles.description}
                      />
                      <div className={styles.metadata}>
                        <Badge appearance="tint">
                          {item.kind === 'Report' ? 'Report' : 'AI Dashboard'}
                        </Badge>
                        <Caption1>
                          {isSafeReportUrl(item.url)
                            ? new URL(item.url).hostname
                            : 'Invalid destination'}
                        </Caption1>
                      </div>
                    </div>
                    <div className={styles.actions}>
                      <Button
                        icon={<WindowRegular />}
                        onClick={() => navigate(`/reports/${item.id}`)}
                      >
                        View in app
                      </Button>
                      {isSafeReportUrl(item.url) && (
                        <Button
                          as="a"
                          href={item.url}
                          target="_blank"
                          rel="noopener noreferrer"
                          icon={<ArrowUpRightRegular />}
                        >
                          Open externally
                        </Button>
                      )}
                      {canWrite && <><Button
                        appearance="subtle"
                        icon={<EditRegular />}
                        aria-label={`Edit ${item.name}`}
                        onClick={() => navigate(`/reports/${item.id}/edit`)}
                      />
                      <Button
                        appearance="subtle"
                        icon={<DeleteRegular />}
                        aria-label={`Delete ${item.name}`}
                        onClick={() => {
                          setDeleteError('')
                          setDeleting(item)
                        }}
                      />
                      </>}
                    </div>
                  </li>
                ))}
              </ul>
            )}
            {result.totalCount > 25 || page > 1 ? (
              <div className={styles.pagination}>
                <Body1>
                  Page {page} of {Math.max(1, Math.ceil(result.totalCount / 25))}
                </Body1>
                <div className={styles.actions}>
                  <Button
                    disabled={page <= 1}
                    onClick={() => changeParams('page', String(page - 1))}
                  >
                    Previous
                  </Button>
                  <Button
                    disabled={page * 25 >= result.totalCount}
                    onClick={() => changeParams('page', String(page + 1))}
                  >
                    Next
                  </Button>
                </div>
              </div>
            ) : null}
          </>
        )
      )}
      <Dialog
        open={Boolean(deleting)}
        onOpenChange={(_, data) => {
          if (!busy && !data.open) setDeleting(null)
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete this link?</DialogTitle>
            <DialogContent>
              <Body1 className={styles.description}>
                Remove &ldquo;{deleting?.name}&rdquo; from the shared directory? This does not
                delete the report or dashboard itself.
              </Body1>
              {deleteError && (
                <MessageBar intent="error">
                  <MessageBarBody>{deleteError}</MessageBarBody>
                </MessageBar>
              )}
            </DialogContent>
            <DialogActions>
              <Button disabled={busy} onClick={() => setDeleting(null)}>
                Cancel
              </Button>
              <Button appearance="primary" disabled={busy} onClick={() => void remove()}>
                {busy ? 'Deleting...' : 'Delete link'}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  )
}
