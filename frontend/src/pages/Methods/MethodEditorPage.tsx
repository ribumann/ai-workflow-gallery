import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import {
  Body1,
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Spinner,
  Textarea,
  Title1,
} from '@fluentui/react-components'
import { ArrowLeftRegular } from '@fluentui/react-icons'
import { MarkdownContentEditor } from '../../components/MarkdownContentEditor'
import { apiClient, ApiError } from '../../services/apiClient'
import type { MethodInput } from '../../services/types'
import {
  COPY_AS_NEW_VERSION_HINT,
  COPY_FROM_PARAM,
  nextVersionName,
} from '../../utils/versionCopy'
import {
  METHOD_LIMITS,
  METHODS_BROWSE_PATH,
  methodError,
  useMethodStyles,
  validateMethod,
} from './methodUi'

export function MethodEditorPage() {
  const { id } = useParams<{ id: string }>()
  const [params] = useSearchParams()
  const copyFrom = id ? null : params.get(COPY_FROM_PARAM)
  return <MethodEditor key={id ?? (copyFrom ? `copy-${copyFrom}` : 'new')} id={id} />
}

function MethodEditor({ id }: { id?: string }) {
  const styles = useMethodStyles()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  // Interim "create new version as copy": prefill a brand-new draft from an existing method.
  const copyFromId = id ? null : params.get(COPY_FROM_PARAM)
  const sourceId = id ?? copyFromId
  const [form, setForm] = useState<MethodInput>({ title: '', summary: '', approach: '' })
  const [version, setVersion] = useState('')
  const [loading, setLoading] = useState(Boolean(sourceId))
  const [loaded, setLoaded] = useState(!sourceId)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Partial<Record<keyof MethodInput, string>>>({})
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
      .getMethod(sourceId)
      .then((item) => {
        if (!cancelled) {
          setForm({
            title: copyFromId ? nextVersionName(item.title, METHOD_LIMITS.title) : item.title,
            summary: item.summary,
            approach: item.approach,
          })
          // A copy is saved as a new method, so there is no expected version to guard against.
          if (!copyFromId) setVersion(item.version)
          setLoaded(true)
          setError('')
          setFieldErrors({})
          setConflict(false)
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(methodError(err, 'Could not load this method. Please retry.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [sourceId, copyFromId, revision])

  function reload() {
    setLoading(true)
    setRevision((value) => value + 1)
  }

  // Leaving an edit returns to the method; leaving a copy returns to the method it was copied from.
  const exitPath = sourceId ? `/methods/${sourceId}` : METHODS_BROWSE_PATH

  async function save(event: FormEvent) {
    event.preventDefault()
    const errors = validateMethod(form)
    setFieldErrors(errors)
    if (Object.keys(errors).length || busy || conflict) return
    setBusy(true)
    setError('')
    try {
      const payload = {
        title: form.title.trim(),
        summary: form.summary.trim(),
        approach: form.approach.trim(),
      }
      const item = id
        ? await apiClient.updateMethod(id, { ...payload, expectedVersion: version })
        : await apiClient.createMethod(payload)
      if (active.current) navigate(`/methods/${item.id}`)
    } catch (err) {
      if (active.current) {
        const stale = err instanceof ApiError && err.status === 409
        setConflict(stale)
        setError(
          stale
            ? 'Someone else changed this method. Your draft is still here. Compare the latest method before reloading.'
            : methodError(
                err,
                'Could not save this method. Your draft is still here. Please retry.',
              ),
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
          disabled={busy}
          onClick={() => navigate(exitPath)}
        >
          Back to {sourceId ? 'method' : 'methods'}
        </Button>
      </div>
      <div className={styles.intro}>
        <Title1 as="h1">
          {id ? 'Edit method' : copyFromId ? 'Create new version as copy' : 'Create method'}
        </Title1>
        <Body1 className={styles.measure}>
          Describe when this approach helps and how to think through it. Methods explain the
          reasoning; tasks and workflows contain the executable steps.
        </Body1>
      </div>
      {copyFromId && !error && (
        <MessageBar intent="info">
          <MessageBarBody>{COPY_AS_NEW_VERSION_HINT}</MessageBarBody>
        </MessageBar>
      )}
      {error && (
        <MessageBar intent={conflict ? 'warning' : 'error'}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      {conflict && (
        <div className={styles.actions}>
          <RouterLink
            className={styles.link}
            to={`/methods/${id}`}
            target="_blank"
            rel="noopener noreferrer"
          >
            Compare latest method in a new tab
          </RouterLink>
          <Button disabled={loading} onClick={reload}>
            Reload latest version (replaces your draft)
          </Button>
        </div>
      )}
      {loading ? (
        <Spinner label="Loading method..." />
      ) : !loaded ? (
        <div>
          <Button onClick={reload}>Retry</Button>
        </div>
      ) : (
        <form className={styles.form} onSubmit={(event) => void save(event)} noValidate>
          <Field
            label="Title"
            required
            validationState={fieldErrors.title ? 'error' : 'none'}
            validationMessage={fieldErrors.title}
          >
            <Input
              value={form.title}
              maxLength={METHOD_LIMITS.title}
              disabled={busy}
              onChange={(_, data) => setForm({ ...form, title: data.value })}
            />
          </Field>
          <Field
            label="Short summary"
            required
            hint="Help colleagues recognize when to use this approach."
            validationState={fieldErrors.summary ? 'error' : 'none'}
            validationMessage={fieldErrors.summary}
          >
            <Textarea
              value={form.summary}
              maxLength={METHOD_LIMITS.summary}
              rows={3}
              resize="vertical"
              disabled={busy}
              onChange={(_, data) => setForm({ ...form, summary: data.value })}
            />
          </Field>
          <Field
            label="Written approach"
            required
            hint="Use Markdown headings, lists, links and code. Preview to check formatting."
            validationState={fieldErrors.approach ? 'error' : 'none'}
            validationMessage={fieldErrors.approach}
          >
            {(control) => (
              <MarkdownContentEditor
                {...control}
                value={form.approach}
                rows={16}
                maxLength={METHOD_LIMITS.approach}
                disabled={busy}
                onChange={(approach) => setForm({ ...form, approach })}
              />
            )}
          </Field>
          <Body1 className={styles.muted}>
            Author, editor, timestamps and version are recorded automatically. Do not include
            customer-identifiable or confidential customer data.
          </Body1>
          <div className={styles.actions}>
            <Button appearance="primary" type="submit" disabled={busy || conflict}>
              {busy ? 'Saving...' : 'Save method'}
            </Button>
            <Button
              type="button"
              disabled={busy}
              onClick={() => navigate(exitPath)}
            >
              Cancel
            </Button>
          </div>
        </form>
      )}
    </div>
  )
}
