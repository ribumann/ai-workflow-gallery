import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import {
  Body1,
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Select,
  Spinner,
  Title1,
} from '@fluentui/react-components'
import { ArrowLeftRegular } from '@fluentui/react-icons'
import { MarkdownContentEditor } from '../../components/MarkdownContentEditor'
import { apiClient, ApiError } from '../../services/apiClient'
import type { ReportLinkInput } from '../../services/types'
import { isSafeReportUrl } from '../../utils/url'
import {
  COPY_AS_NEW_VERSION_HINT,
  COPY_FROM_PARAM,
  nextVersionName,
} from '../../utils/versionCopy'
import { reportError, useReportStyles } from './reportUi'

export function ReportEditorPage() {
  const { id } = useParams<{ id: string }>()
  const [params] = useSearchParams()
  const copyFrom = id ? null : params.get(COPY_FROM_PARAM)
  return <ReportEditor key={id ?? (copyFrom ? `copy-${copyFrom}` : 'new')} id={id} />
}

function ReportEditor({ id }: { id?: string }) {
  const styles = useReportStyles()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  // Interim "create new version as copy": prefill a brand-new draft from an existing link.
  const copyFromId = id ? null : params.get(COPY_FROM_PARAM)
  const sourceId = id ?? copyFromId
  const [form, setForm] = useState<ReportLinkInput>({
    name: '',
    description: '',
    url: '',
    kind: params.get('kind') === 'AiApp' ? 'AiApp' : 'Report',
  })
  const [version, setVersion] = useState('')
  const [loading, setLoading] = useState(Boolean(sourceId))
  const [loaded, setLoaded] = useState(!sourceId)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [conflict, setConflict] = useState(false)
  const [revision, setRevision] = useState(0)
  const active = useRef(true)

  useEffect(() => {
    active.current = true
    return () => {
      active.current = false
    }
  }, [])

  useEffect(() => {
    if (!sourceId) return
    let cancelled = false
    void apiClient
      .getReport(sourceId)
      .then((item) => {
        if (!cancelled) {
          setForm({
            name: copyFromId ? nextVersionName(item.name) : item.name,
            description: item.description,
            url: item.url,
            kind: item.kind,
          })
          // A copy is saved as a new link, so there is no expected version to guard against.
          if (!copyFromId) setVersion(item.version)
          setLoaded(true)
          setError('')
          setConflict(false)
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(reportError(err, 'Could not load this link. Please retry.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [sourceId, copyFromId, revision])

  // Leaving an edit returns to the link; leaving a copy returns to the link it was copied from.
  const exitPath = sourceId ? `/reports/${sourceId}` : '/reports'

  function reload() {
    setLoading(true)
    setRevision((value) => value + 1)
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!form.name.trim() || !form.description.trim() || !isSafeReportUrl(form.url)) {
      setError(
        'Enter a name, description and a full HTTPS URL without spaces or embedded credentials.',
      )
      return
    }
    setBusy(true)
    setError('')
    setConflict(false)
    try {
      const payload = { ...form, name: form.name.trim(), description: form.description.trim() }
      const result = id
        ? await apiClient.updateReport(id, { ...payload, expectedVersion: version })
        : await apiClient.createReport(payload)
      if (active.current) navigate(`/reports/${result.id}`)
    } catch (err) {
      if (active.current) {
        setConflict(err instanceof ApiError && err.status === 409)
        setError(
          reportError(err, 'Could not save this link. Your draft is still here. Please retry.'),
        )
      }
    } finally {
      if (active.current) setBusy(false)
    }
  }

  return (
    <div className={styles.page}>
      <div>
        <Button
          appearance="subtle"
          icon={<ArrowLeftRegular />}
          onClick={() => navigate(exitPath)}
        >
          Back to {sourceId ? 'link' : 'directory'}
        </Button>
      </div>
      <div className={styles.intro}>
        <Title1 as="h1" className={styles.heading}>
          {id ? 'Edit link' : copyFromId ? 'Create new version as copy' : 'Add a report or AI dashboard'}
        </Title1>
        <Body1 className={styles.description}>
          Use a descriptive name so colleagues know when to use it. Everyone signed in can edit or
          remove this link.
        </Body1>
      </div>
      {copyFromId && (
        <MessageBar intent="info">
          <MessageBarBody>{COPY_AS_NEW_VERSION_HINT}</MessageBarBody>
        </MessageBar>
      )}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      {conflict && (
        <div>
          <Button disabled={loading} onClick={reload}>
            Reload latest version (replaces your draft)
          </Button>
        </div>
      )}
      {loading ? (
        <Spinner label="Loading link..." />
      ) : !loaded ? (
        <div>
          <Button onClick={reload}>Retry</Button>
        </div>
      ) : (
        <form className={styles.form} onSubmit={(event) => void save(event)}>
          <Field label="Name" required hint="A verbose, recognizable name (up to 200 characters).">
            <Input
              required
              maxLength={200}
              value={form.name}
              disabled={busy}
              onChange={(_, data) => setForm({ ...form, name: data.value })}
            />
          </Field>
          <Field
            label="Description"
            required
            hint="What it helps with, who it is for, and when to use it. Markdown is supported."
          >
            {(control) => (
              <MarkdownContentEditor
                {...control}
                value={form.description}
                required
                maxLength={4000}
                rows={12}
                disabled={busy}
                onChange={(description) => setForm({ ...form, description })}
              />
            )}
          </Field>
          <Field label="Type" required>
            <Select
              value={form.kind}
              disabled={busy}
              onChange={(_, data) =>
                setForm({ ...form, kind: data.value === 'AiApp' ? 'AiApp' : 'Report' })
              }
            >
              <option value="Report">Report</option>
              <option value="AiApp">AI Dashboard</option>
            </Select>
          </Field>
          <Field
            label="Destination URL"
            required
            hint="Use an HTTPS URL; in-app viewing needs a destination-supported embed URL. Do not include passwords, secrets or access tokens."
          >
            <Input
              type="url"
              required
              maxLength={2048}
              value={form.url}
              disabled={busy}
              placeholder="https://"
              onChange={(_, data) => setForm({ ...form, url: data.value })}
            />
          </Field>
          <Body1 className={styles.description}>
            Gallery sign-in is separate from destination sign-in. Some destinations block in-app
            viewing even for signed-in users; opening externally is always available.
          </Body1>
          <div className={styles.actions}>
            <Button type="submit" appearance="primary" disabled={busy || conflict}>
              {busy ? 'Saving...' : 'Save link'}
            </Button>
            <Button disabled={busy} onClick={() => navigate(exitPath)}>
              Cancel
            </Button>
          </div>
        </form>
      )}
    </div>
  )
}
