import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import {
  Body1,
  Button,
  Caption1,
  Dropdown,
  Field,
  Input,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Option,
  Spinner,
  Title1,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components'
import { ArrowLeftRegular, DismissRegular, DocumentArrowUpRegular } from '@fluentui/react-icons'
import { apiClient, ApiError } from '../../services/apiClient'
import { CaughtYouToast } from '../../components/CaughtYouToast'
import { MarkdownContentEditor } from '../../components/MarkdownContentEditor'
import { MethodPicker } from '../../components/MethodPicker'
import { TargetPicker } from '../../components/TargetPicker'
import { catalog, formatsFor, targetLabel, targetsFor, validateClassification } from '../../services/catalog'
import { isSecurityRejectionMessage } from '../../utils/securityRejection'
import {
  COPY_AS_NEW_VERSION_HINT,
  COPY_FROM_PARAM,
  nextVersionName,
} from '../../utils/versionCopy'
import {
  TASK_TYPES,
  TASK_TYPE_LABELS,
  type TaskDetail,
  type TaskFile,
  type TaskSummary,
  type TaskType,
  type ErrorResponse,
  type MethodRef,
} from '../../services/types'

/** Mirrors `FileValidationService` (T028) so obviously-invalid files are rejected before upload
 * instead of round-tripping to the API first. */
const ALLOWED_FILE_EXTENSIONS = ['.md', '.markdown', '.txt', '.json', '.yaml', '.yml', '.zip']
const MAX_FILE_SIZE_BYTES = 5 * 1024 * 1024

const useStyles = makeStyles({
  back: {
    marginBottom: tokens.spacingVerticalM,
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalL,
  },
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    maxWidth: '44rem',
  },
  row: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
    '& > *': {
      flex: '1 1 14rem',
    },
  },
  fileRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  fileChip: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground3,
  },
  duplicateList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalXS,
  },
  actions: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalS,
  },
  loading: {
    display: 'flex',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalXXL, 0),
  },
})

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function getExtension(fileName: string): string {
  const dot = fileName.lastIndexOf('.')
  return dot === -1 ? '' : fileName.slice(dot).toLowerCase()
}

function parseTags(text: string): string[] {
  const seen = new Set<string>()
  const tags: string[] = []
  for (const raw of text.split(',')) {
    const tag = raw.trim().toLowerCase()
    if (tag && !seen.has(tag)) {
      seen.add(tag)
      tags.push(tag)
    }
  }
  return tags
}

/**
 * Shared create/edit form for tasks (T033). Handles client-side validation, FR-013 duplicate
 * detection on create, FR-015 file pre-checks, FR-016's content-policy notice, and (on edit) the
 * FR-009 workflow-reference awareness is left to the detail page's delete guard — this page only
 * owns the T037 optimistic-concurrency conflict flow.
 */

/** Router navigation state shape read on mount when navigating to `/tasks/new` from a gap
 * in the AI composer's proposal (T086, US3, issue #59). Only applied in the "new" case. */
export interface TaskComposerPrefill {
  suggestedName: string
  suggestedDescription: string
}

export function TaskEditorPage() {
  const { id } = useParams<{ id: string }>()
  const [params] = useSearchParams()
  const copyFrom = id ? null : params.get(COPY_FROM_PARAM)
  return <TaskEditor key={id ?? (copyFrom ? `copy-${copyFrom}` : 'new')} />
}

function TaskEditor() {
  const styles = useStyles()
  const navigate = useNavigate()
  const location = useLocation()
  const { id } = useParams<{ id: string }>()
  const [params] = useSearchParams()
  const isEditMode = Boolean(id)
  // Interim "create new version as copy": prefill a brand-new draft from an existing task.
  const copyFromId = isEditMode ? null : params.get(COPY_FROM_PARAM)
  const sourceId = id ?? copyFromId
  const fileInputRef = useRef<HTMLInputElement>(null)

  const [isLoading, setIsLoading] = useState(Boolean(id) || Boolean(copyFromId))
  const [loadError, setLoadError] = useState(false)
  // Set when copying a task that had an attachment: blobs are not copied, so the draft says so.
  const [sourceFileName, setSourceFileName] = useState<string | null>(null)

  // AI composer hand-off (T086, US3, issue #59): prefill name/description from a gap the
  // composer couldn't map to an existing task. Read once as the initial state value (rather
  // than in an effect) — edit mode's load-by-id effect below overwrites both fields anyway, so
  // this only ever takes effect in the "new" case.
  const composerPrefill = (location.state as { taskComposerPrefill?: TaskComposerPrefill } | null)
    ?.taskComposerPrefill

  const [name, setName] = useState(composerPrefill?.suggestedName ?? '')
  const [description, setDescription] = useState(composerPrefill?.suggestedDescription ?? '')
  const [taskType, setTaskType] = useState<TaskType>('prompt')
  const [targetIds, setTargetIds] = useState<string[]>([])
  const [format, setFormat] = useState('')
  const [setup, setSetup] = useState('')
  const [prerequisites, setPrerequisites] = useState('')
  const [tagsText, setTagsText] = useState('')
  const [referenceLink, setReferenceLink] = useState('')
  const [inlineContent, setInlineContent] = useState('')
  const [dataSource, setDataSource] = useState('')
  const [dataOutput, setDataOutput] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [existingFile, setExistingFile] = useState<TaskFile | null>(null)
  const [lastEditedAt, setLastEditedAt] = useState<string | undefined>(undefined)
  const [methodId, setMethodId] = useState<string | null>(null)
  const [method, setMethod] = useState<MethodRef | null>(null)
  const [methodChanged, setMethodChanged] = useState(false)

  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const [conflict, setConflict] = useState<TaskDetail | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [duplicates, setDuplicates] = useState<TaskSummary[]>([])
  const [showCaughtYou, setShowCaughtYou] = useState(false)

  useEffect(() => {
    if (!sourceId) return
    let cancelled = false

    const load = async () => {
      setIsLoading(true)
      setLoadError(false)
      try {
        const data = await apiClient.getTask(sourceId)
        if (cancelled) return
        setName(copyFromId ? nextVersionName(data.name) : data.name)
        setDescription(data.description)
        setTaskType(data.taskType)
        setTargetIds(data.targetIds)
        setFormat(data.format ?? '')
        setSetup(data.setup ?? '')
        setPrerequisites(data.prerequisites ?? '')
        setTagsText(data.tags.join(', '))
        setReferenceLink(data.referenceLink ?? '')
        setInlineContent(data.inlineContent ?? '')
        setDataSource(data.dataSource ?? '')
        setDataOutput(data.dataOutput ?? '')
        // A copy is saved as a new task: it starts without the source's attachment (the blob is
        // not duplicated) and has no prior edit to guard with optimistic concurrency.
        setExistingFile(copyFromId ? null : data.attachedFile)
        setSourceFileName(copyFromId ? (data.attachedFile?.fileName ?? null) : null)
        setLastEditedAt(copyFromId ? undefined : data.lastEditedAt)
        setMethodId(data.methodId ?? data.method?.id ?? null)
        setMethod(data.method ?? null)
        // A create must send methodId explicitly, while an edit only sends it when the picker
        // was touched — so a copy has to mark the association as changed to carry it over.
        setMethodChanged(Boolean(copyFromId))
      } catch (err) {
        if (!cancelled && err instanceof ApiError && err.status === 404) setLoadError(true)
      } finally {
        if (!cancelled) setIsLoading(false)
      }
    }

    void load()

    return () => {
      cancelled = true
    }
  }, [sourceId, copyFromId])

  // FR-013: while creating a new task, warn about likely duplicates (same tool/surface, close
  // name match) as the contributor types — non-blocking.
  useEffect(() => {
    if (isEditMode) return
    let cancelled = false
    const trimmed = name.trim()

    const handle = window.setTimeout(
      () => {
        if (trimmed.length < 2) {
          if (!cancelled) setDuplicates([])
          return
        }
        const check = async () => {
          try {
            const result = await apiClient.listTasks({ q: trimmed, targetId: targetIds[0], pageSize: 5 })
            if (!cancelled) setDuplicates(result.items)
          } catch {
            if (!cancelled) setDuplicates([])
          }
        }
        void check()
      },
      trimmed.length < 2 ? 0 : 400,
    )

    return () => {
      cancelled = true
      window.clearTimeout(handle)
    }
  }, [name, targetIds, isEditMode])

  const onFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    const selected = event.target.files?.[0] ?? null
    event.target.value = ''
    if (!selected) return

    const extension = getExtension(selected.name)
    if (!ALLOWED_FILE_EXTENSIONS.includes(extension)) {
      setFieldErrors((prev) => ({
        ...prev,
        file: `"${extension || selected.name}" isn't a supported file type. Allowed types: ${ALLOWED_FILE_EXTENSIONS.join(', ')}.`,
      }))
      return
    }
    if (selected.size > MAX_FILE_SIZE_BYTES) {
      setFieldErrors((prev) => ({
        ...prev,
        file: `That file is ${formatBytes(selected.size)}, which is over the ${formatBytes(MAX_FILE_SIZE_BYTES)} limit.`,
      }))
      return
    }

    setFieldErrors((prev) => {
      const next = { ...prev }
      delete next.file
      return next
    })
    setFile(selected)
  }

  const clearSelectedFile = () => setFile(null)

  const validate = (): Record<string, string> => {
    const errors: Record<string, string> = { ...fieldErrors }
    delete errors.name
    delete errors.description
    delete errors.content
    delete errors.classification
    const classificationError = validateClassification(taskType, targetIds, format || null)
    if (classificationError) errors.classification = classificationError

    if (!name.trim()) errors.name = 'Name is required.'
    if (!description.trim()) errors.description = 'Description is required.'

    const hasContent =
      Boolean(referenceLink.trim()) ||
      Boolean(inlineContent.trim()) ||
      Boolean(file) ||
      (isEditMode && Boolean(existingFile))
    if (!hasContent) {
      errors.content =
        'Provide at least one of a reference link, inline content, or an attached file.'
    }

    return errors
  }

  const submit = async (bypassConcurrencyGuard: boolean) => {
    setIsSubmitting(true)
    setServerError(null)
    try {
      const input = {
        name: name.trim(),
        description: description.trim(),
        taskType,
        targetIds,
        format: format || null,
        setup: setup.trim() || null,
        prerequisites: prerequisites.trim() || null,
        tags: parseTags(tagsText),
        referenceLink: referenceLink.trim(),
        inlineContent: inlineContent.trim(),
        dataSource: dataSource.trim(),
        dataOutput: dataOutput.trim(),
        file,
        expectedLastEditedAt: isEditMode && !bypassConcurrencyGuard ? lastEditedAt : undefined,
        ...(methodChanged ? { methodId } : {}),
      }
      const result =
        isEditMode && id ? await apiClient.updateTask(id, input) : await apiClient.createTask(input)
      navigate(`/tasks/${result.id}`)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409 && isEditMode) {
        setConflict(err.body as TaskDetail)
      } else if (err instanceof ApiError && err.status === 400) {
        const body = err.body as ErrorResponse | undefined
        const message = body?.message ?? 'This task could not be saved. Check the fields above.'
        setServerError(message)
        if (isSecurityRejectionMessage(message)) setShowCaughtYou(true)
      } else {
        setServerError('Something went wrong saving this task. Try again.')
      }
    } finally {
      setIsSubmitting(false)
    }
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    const errors = validate()
    setFieldErrors(errors)
    if (Object.keys(errors).length > 0) return
    void submit(false)
  }

  const reloadConflictingVersion = () => {
    if (!conflict) return
    setName(conflict.name)
    setDescription(conflict.description)
    setTaskType(conflict.taskType)
    setTargetIds(conflict.targetIds)
    setFormat(conflict.format ?? '')
    setSetup(conflict.setup ?? '')
    setPrerequisites(conflict.prerequisites ?? '')
    setTagsText(conflict.tags.join(', '))
    setReferenceLink(conflict.referenceLink ?? '')
    setInlineContent(conflict.inlineContent ?? '')
    setDataSource(conflict.dataSource ?? '')
    setDataOutput(conflict.dataOutput ?? '')
    setExistingFile(conflict.attachedFile)
    setLastEditedAt(conflict.lastEditedAt)
    setMethodId(conflict.methodId ?? conflict.method?.id ?? null)
    setMethod(conflict.method ?? null)
    setMethodChanged(false)
    setFile(null)
    setConflict(null)
  }

  const saveAnyway = () => {
    setConflict(null)
    void submit(true)
  }

  if (isLoading) {
    return (
      <div className={styles.loading}>
        <Spinner label="Loading task..." />
      </div>
    )
  }

  if (loadError) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>This task could not be found. It may have been deleted.</MessageBarBody>
      </MessageBar>
    )
  }

  return (
    <div>
      <Button
        className={styles.back}
        appearance="subtle"
        icon={<ArrowLeftRegular />}
        onClick={() => navigate(sourceId ? `/tasks/${sourceId}` : '/')}
      >
        Back
      </Button>

      <div className={styles.header}>
        <Title1 as="h1">
          {isEditMode ? 'Edit task' : copyFromId ? 'Create new version as copy' : 'New task'}
        </Title1>
        <Body1>
          {isEditMode ? (
            <>Update this task&rsquo;s details, content, or attached file.</>
          ) : copyFromId ? (
            'Everything has been copied from the original. Adjust what changed, then save it as a new task.'
          ) : (
            'Contribute an agent, skill, plugin, prompt, or MCP tool to the catalog.'
          )}
        </Body1>
      </div>

      {copyFromId && (
        <MessageBar
          intent="info"
          style={{ marginBottom: tokens.spacingVerticalL, maxWidth: '44rem' }}
        >
          <MessageBarBody>
            {COPY_AS_NEW_VERSION_HINT}
            {sourceFileName
              ? ` The attached file "${sourceFileName}" was not copied — upload it again if this version needs it.`
              : ''}
          </MessageBarBody>
        </MessageBar>
      )}

      <MessageBar
        intent="warning"
        style={{ marginBottom: tokens.spacingVerticalL, maxWidth: '44rem' }}
      >
        <MessageBarBody>
          <MessageBarTitle>Content policy</MessageBarTitle>
          Don&rsquo;t include customer-identifiable or confidential customer data in this
          task&rsquo;s inline content or uploaded files.
        </MessageBarBody>
      </MessageBar>

      {conflict && (
        <MessageBar
          intent="warning"
          style={{ marginBottom: tokens.spacingVerticalL, maxWidth: '44rem' }}
        >
          <MessageBarBody>
            <MessageBarTitle>This task changed since you loaded it</MessageBarTitle>
            {conflict.lastEditedBy.displayName} edited it on{' '}
            {new Date(conflict.lastEditedAt).toLocaleString()}.
          </MessageBarBody>
          <MessageBarActions>
            <Button appearance="primary" onClick={reloadConflictingVersion}>
              Reload their version
            </Button>
            <Button onClick={saveAnyway}>Save my changes anyway</Button>
          </MessageBarActions>
        </MessageBar>
      )}

      {serverError && (
        <MessageBar
          intent="error"
          style={{ marginBottom: tokens.spacingVerticalL, maxWidth: '44rem' }}
        >
          <MessageBarBody>{serverError}</MessageBarBody>
        </MessageBar>
      )}

      <form className={styles.form} onSubmit={onSubmit} noValidate>
        <Field
          label="Name"
          required
          validationState={fieldErrors.name ? 'error' : 'none'}
          validationMessage={fieldErrors.name}
        >
          <Input value={name} onChange={(_, data) => setName(data.value)} maxLength={200} />
        </Field>

        {duplicates.length > 0 && (
          <MessageBar intent="warning">
            <MessageBarBody>
              <MessageBarTitle>Possible duplicate</MessageBarTitle>
              These existing tasks have a similar name:
              <div className={styles.duplicateList}>
                {duplicates.slice(0, 5).map((d) => (
                  <Caption1 key={d.id}>
                    <strong>{d.name}</strong> — {TASK_TYPE_LABELS[d.taskType]}
                  </Caption1>
                ))}
              </div>
            </MessageBarBody>
          </MessageBar>
        )}

        <Field
          label="Description"
          required
          validationState={fieldErrors.description ? 'error' : 'none'}
          validationMessage={fieldErrors.description}
          hint="Use Markdown for headings, lists, links and code."
        >
          {(control) => (
            <MarkdownContentEditor
              {...control}
              value={description}
              onChange={setDescription}
              rows={10}
              disabled={isSubmitting}
            />
          )}
        </Field>

        <div className={styles.row}>
          <Field label="Type" required>
            <Dropdown
              value={TASK_TYPE_LABELS[taskType]}
              selectedOptions={[taskType]}
              onOptionSelect={(_, data) => {
                if (!data.optionValue) return
                setTaskType(data.optionValue)
                setTargetIds((ids) => ids.filter((id) =>
                  targetsFor(data.optionValue).some((target) => target.id === id)))
                setFormat('')
              }}
            >
              {TASK_TYPES.map((t) => (
                <Option key={t} value={t}>
                  {TASK_TYPE_LABELS[t]}
                </Option>
              ))}
            </Dropdown>
          </Field>

          <Field label="Works in" required validationState={fieldErrors.classification ? 'error' : 'none'} validationMessage={fieldErrors.classification}>
            <TargetPicker kind={taskType} value={targetIds} multiple disabled={isSubmitting}
              onChange={(ids) => { setTargetIds(ids); setFormat('') }} />
          </Field>
        </div>

        {targetIds.length > 0 && (
          <details>
            <summary>Format, setup &amp; access guidance (optional)</summary>
            <Field label="Format" hint="Only formats documented for every chosen target appear. Unspecified does not promise package compatibility.">
              <Dropdown value={catalog.formats.find((item) => item.id === format)?.label ?? 'Unspecified'}
                selectedOptions={format ? [format] : []} onOptionSelect={(_, data) => setFormat(data.optionValue ?? '')}>
                <Option value="">Unspecified</Option>
                {formatsFor(taskType, targetIds).map((item) => <Option key={item.id} value={item.id}>{item.label}</Option>)}
              </Dropdown>
            </Field>
            <Field label="Setup guidance"><Input maxLength={2000} value={setup} onChange={(_, data) => setSetup(data.value)} /></Field>
            <Field label="Additional prerequisites"><Input maxLength={2000} value={prerequisites} onChange={(_, data) => setPrerequisites(data.value)} /></Field>
            {targetIds.map((id) => <Body1 key={id}><strong>{targetLabel(id)}:</strong> {catalog.targets.find((target) => target.id === id)?.prerequisites}</Body1>)}
            <Caption1>Documented compatibility is not a verified gallery connection. Account, license and administrator approval are separate.</Caption1>
          </details>
        )}

        <MethodPicker
          methodId={methodId}
          method={method}
          disabled={isSubmitting}
          onChange={(value) => {
            setMethod(value)
            setMethodId(value?.id ?? null)
            setMethodChanged(true)
          }}
        />

        <Field label="Tags" hint="Comma-separated, e.g. reporting, automation">
          <Input value={tagsText} onChange={(_, data) => setTagsText(data.value)} />
        </Field>

        <Field
          label="Reference link"
          hint="A URL or location where this task lives"
          validationState={fieldErrors.content ? 'error' : 'none'}
        >
          <Input
            type="url"
            value={referenceLink}
            onChange={(_, data) => setReferenceLink(data.value)}
            placeholder="https://..."
          />
        </Field>

        <Field
          label="Inline content"
          hint="Paste prompt or code content. Use the Markdown toolbar and Preview to check formatting."
        >
          {(control) => (
            <MarkdownContentEditor
              {...control}
              value={inlineContent}
              onChange={setInlineContent}
              rows={14}
              disabled={isSubmitting}
            />
          )}
        </Field>

        <Field label="Data source (optional)" hint="What this task expects as input">
          <Input
            value={dataSource}
            onChange={(_, data) => setDataSource(data.value)}
            placeholder="e.g. Meeting transcript, ticket ID"
          />
        </Field>

        <Field label="Data output (optional)" hint="What this task produces">
          <Input
            value={dataOutput}
            onChange={(_, data) => setDataOutput(data.value)}
            placeholder="e.g. Summary document, draft email"
          />
        </Field>

        <Field
          label="Attached file"
          hint={`Optional. Allowed types: ${ALLOWED_FILE_EXTENSIONS.join(', ')}. Max ${formatBytes(MAX_FILE_SIZE_BYTES)}.`}
          validationState={fieldErrors.file ? 'error' : 'none'}
          validationMessage={fieldErrors.file}
        >
          <div className={styles.fileRow}>
            <Button
              type="button"
              icon={<DocumentArrowUpRegular />}
              onClick={() => fileInputRef.current?.click()}
            >
              Choose file
            </Button>
            <input
              ref={fileInputRef}
              type="file"
              hidden
              accept={ALLOWED_FILE_EXTENSIONS.join(',')}
              onChange={onFileChange}
            />
            {file && (
              <span className={styles.fileChip}>
                {file.name} ({formatBytes(file.size)})
                <Button
                  type="button"
                  appearance="transparent"
                  size="small"
                  icon={<DismissRegular />}
                  onClick={clearSelectedFile}
                  aria-label="Remove selected file"
                />
              </span>
            )}
            {!file && existingFile && (
              <Caption1>
                Current file: {existingFile.fileName} ({formatBytes(existingFile.sizeBytes)}) —
                choose a new file to replace it.
              </Caption1>
            )}
          </div>
        </Field>

        {fieldErrors.content && !fieldErrors.file && (
          <MessageBar intent="error">
            <MessageBarBody>{fieldErrors.content}</MessageBarBody>
          </MessageBar>
        )}

        <div className={styles.actions}>
          <Button appearance="primary" type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner size="tiny" /> : isEditMode ? 'Save changes' : 'Create task'}
          </Button>
          <Button
            appearance="secondary"
            type="button"
            onClick={() => navigate(sourceId ? `/tasks/${sourceId}` : '/')}
          >
            Cancel
          </Button>
        </div>
      </form>

      <CaughtYouToast visible={showCaughtYou} onHide={() => setShowCaughtYou(false)} />
    </div>
  )
}
