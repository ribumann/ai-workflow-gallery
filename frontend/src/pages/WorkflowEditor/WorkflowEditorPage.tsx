import { useEffect, useState } from 'react'
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import {
  Badge,
  Body1,
  Button,
  Caption1,
  Card,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DialogTrigger,
  Field,
  Input,
  Link,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  SearchBox,
  Spinner,
  Title1,
  Title3,
  Toolbar,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components'
import {
  AddRegular,
  ArrowDownRegular,
  ArrowLeftRegular,
  ArrowUpRegular,
  DeleteRegular,
  WarningRegular,
} from '@fluentui/react-icons'
import { apiClient, ApiError } from '../../services/apiClient'
import { MethodPicker } from '../../components/MethodPicker'
import { TargetPicker } from '../../components/TargetPicker'
import { targetLabel } from '../../services/catalog'
import {
  COPY_AS_NEW_VERSION_HINT,
  COPY_FROM_PARAM,
  nextVersionName,
} from '../../utils/versionCopy'
import { MarkdownContentEditor } from '../../components/MarkdownContentEditor'
import {
  TASK_TYPE_LABELS,
  type TaskSummary,
  type WorkflowDetail,
  type WorkflowSaveRequest,
  type MethodRef,
} from '../../services/types'

const useStyles = makeStyles({
  back: {
    marginBottom: tokens.spacingVerticalM,
  },
  header: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    marginBottom: tokens.spacingVerticalL,
  },
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    maxWidth: '48rem',
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  sectionHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  steps: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  step: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    ...shorthands.padding(tokens.spacingVerticalM),
  },
  stepNumber: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: '2rem',
    height: '2rem',
    ...shorthands.borderRadius(tokens.borderRadiusCircular),
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground2,
    fontWeight: tokens.fontWeightSemibold,
    flexShrink: 0,
  },
  stepBody: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    flexGrow: 1,
    minWidth: 0,
  },
  stepHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  stepControls: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalXS,
  },
  stepRow: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
  },
  unavailable: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorPaletteMarigoldForeground1,
  },
  pickerResults: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    maxHeight: '20rem',
    overflowY: 'auto',
  },
  pickerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3,
    },
  },
  actions: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
  },
  loading: {
    display: 'flex',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalXXL, 0),
  },
})

/** A step being authored/edited. Mirrors `WorkflowStepInput` plus the display fields needed to
 * render it without a second lookup — `taskId: null` means the referenced task was
 * deleted since the workflow was last saved (FR-010) and the step must be fixed before saving. */
interface EditableStep {
  key: string
  taskId: string | null
  taskName: string
  taskType: string | null
  targetId: string | null
  targetIds: string[]
  notes: string
  isManualStep: boolean
  manualStepTitle: string
  manualStepInstructions: string
  dataSource: string
  dataOutput: string
}

let stepKeySeq = 0
function nextStepKey(): string {
  stepKeySeq += 1
  return `step-${stepKeySeq}`
}

/** One proposed step handed off from `AiComposerPanel`'s "Create workflow from this" action
 * (T083, issue #59) via router navigation state — see `AiComposerPrefill` below. */
export interface AiComposerPrefillStep {
  taskId: string
  taskName: string
  taskType: string
  targetId: string
  notes: string | null
}

/** Router navigation state shape read on mount when navigating to `/workflows/new` from the AI
 * composer (T081, US2, issue #59). Only applied in the "new" case (no `id` route param) — editing
 * an existing workflow behaves exactly as before. */
export interface AiComposerPrefill {
  name: string
  problemStatement: string
  desiredOutcome: string
  steps: AiComposerPrefillStep[]
}

/** Shared create/edit form for Workflows (T041), covering:
 * - name / problem statement / desired outcome fields
 * - an task picker (backed by the task search endpoint) to add ordered steps
 * - per-step tool/surface selection, notes, reordering, and removal
 * - the FR-012 "at least 2 steps" save-blocking validation (T042)
 * - "add an task first" guidance when the catalog is empty (T043)
 * - the FR-007 concurrency-conflict (409) handling on save (T045)
 */
export function WorkflowEditorPage() {
  const { id } = useParams<{ id: string }>()
  const [params] = useSearchParams()
  const copyFrom = id ? null : params.get(COPY_FROM_PARAM)
  return <WorkflowEditor key={id ?? (copyFrom ? `copy-${copyFrom}` : 'new')} />
}

function WorkflowEditor() {
  const styles = useStyles()
  const navigate = useNavigate()
  const location = useLocation()
  const { id } = useParams<{ id?: string }>()
  const [params] = useSearchParams()
  const isEditMode = Boolean(id)
  // Interim "create new version as copy": prefill a brand-new draft from an existing workflow.
  const copyFromId = isEditMode ? null : params.get(COPY_FROM_PARAM)
  const sourceId = id ?? copyFromId

  const [isLoading, setIsLoading] = useState(Boolean(sourceId))
  const [notFound, setNotFound] = useState(false)

  const [name, setName] = useState('')
  const [problemStatement, setProblemStatement] = useState('')
  const [desiredOutcome, setDesiredOutcome] = useState('')
  const [steps, setSteps] = useState<EditableStep[]>([])
  const [lastEditedAt, setLastEditedAt] = useState<string | undefined>(undefined)
  const [methodId, setMethodId] = useState<string | null>(null)
  const [method, setMethod] = useState<MethodRef | null>(null)
  const [methodChanged, setMethodChanged] = useState(false)

  const [catalogIsEmpty, setCatalogIsEmpty] = useState(false)

  const [isPickerOpen, setIsPickerOpen] = useState(false)
  const [pickerQuery, setPickerQuery] = useState('')
  const [pickerDebouncedQuery, setPickerDebouncedQuery] = useState('')
  const [pickerResults, setPickerResults] = useState<TaskSummary[]>([])
  const [pickerLoading, setPickerLoading] = useState(false)

  const [isSaving, setIsSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [conflict, setConflict] = useState<WorkflowDetail | null>(null)
  const [isDeleteConfirmOpen, setIsDeleteConfirmOpen] = useState(false)
  const [isDeleting, setIsDeleting] = useState(false)

  const applyWorkflow = (workflow: WorkflowDetail) => {
    setName(workflow.name)
    setProblemStatement(workflow.problemStatement)
    setDesiredOutcome(workflow.desiredOutcome)
    setLastEditedAt(workflow.lastEditedAt)
    setMethodId(workflow.methodId ?? workflow.method?.id ?? null)
    setMethod(workflow.method ?? null)
    setMethodChanged(false)
    setSteps(
      workflow.steps.map((s) => ({
        key: nextStepKey(),
        taskId: s.isManualStep ? null : s.taskAvailable && s.targetId && s.taskTargetIds.includes(s.targetId) ? s.taskId : null,
        taskName: s.isManualStep ? '' : s.taskName,
        taskType: s.taskType,
        targetId: s.targetId,
        targetIds: s.taskTargetIds,
        notes: s.notes ?? '',
        isManualStep: s.isManualStep,
        manualStepTitle: s.isManualStep ? s.taskName : '',
        manualStepInstructions: s.manualStepInstructions ?? '',
        dataSource: s.dataSource ?? '',
        dataOutput: s.dataOutput ?? '',
      })),
    )
  }

  useEffect(() => {
    if (!sourceId) return
    let cancelled = false

    const load = async () => {
      setIsLoading(true)
      setNotFound(false)
      try {
        const data = await apiClient.getWorkflow(sourceId)
        if (cancelled) return
        applyWorkflow(data)
        if (copyFromId) {
          // A copy is saved as a new workflow: rename it, drop the source's concurrency stamp,
          // and carry the association explicitly — a create must send methodId, while an edit
          // only sends it when the picker was touched.
          setName(nextVersionName(data.name))
          setLastEditedAt(undefined)
          setMethodChanged(true)
        }
      } catch (err) {
        if (!cancelled && err instanceof ApiError && err.status === 404) {
          setNotFound(true)
        }
      } finally {
        if (!cancelled) setIsLoading(false)
      }
    }

    void load()

    return () => {
      cancelled = true
    }
  }, [sourceId, copyFromId])

  // AI composer hand-off (T081/T082, US2, issue #59): only applies in the "new" case. Each
  // proposed step gets a fresh existence check (FR-012) — the compose call and this navigation
  // happen moments apart, but an task could still have been deleted in between; a stale id
  // must not silently save.
  useEffect(() => {
    if (isEditMode) return
    const prefill = (location.state as { aiComposerPrefill?: AiComposerPrefill } | null)
      ?.aiComposerPrefill
    if (!prefill) return
    let cancelled = false

    const hydrate = async () => {
      setName(prefill.name)
      setProblemStatement(prefill.problemStatement)
      setDesiredOutcome(prefill.desiredOutcome)

      const hydratedSteps = await Promise.all(
        prefill.steps.map(async (step): Promise<EditableStep> => {
          let stillAvailable = false
          let targetIds: string[] = []
          try {
            const task = await apiClient.getTask(step.taskId)
            targetIds = task.targetIds
            stillAvailable = task.targetIds.includes(step.targetId)
          } catch (err) {
            if (!(err instanceof ApiError && err.status === 404))
              setSaveError('Could not verify a proposed task. Reload the proposal before saving.')
          }

          return {
            key: nextStepKey(),
            taskId: stillAvailable ? step.taskId : null,
            taskName: step.taskName,
            taskType: step.taskType,
            targetId: step.targetId,
            targetIds,
            notes: step.notes ?? '',
            isManualStep: false,
            manualStepTitle: '',
            manualStepInstructions: '',
            dataSource: '',
            dataOutput: '',
          }
        }),
      )

      if (!cancelled) setSteps(hydratedSteps)
    }

    void hydrate()

    return () => {
      cancelled = true
    }
    // Intentionally runs once on mount only — location.state is a one-time hand-off, not a value
    // that should re-trigger hydration on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isEditMode])

  // "Add an task first" guidance (T043): checked once on mount, independent of edit/create mode.
  useEffect(() => {
    let cancelled = false

    const load = async () => {
      try {
        const result = await apiClient.listTasks({ pageSize: 1 })
        if (!cancelled) setCatalogIsEmpty(result.totalCount === 0)
      } catch {
        // Non-critical guidance check; leave the guidance hidden if it fails.
      }
    }

    void load()

    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    const handle = window.setTimeout(() => setPickerDebouncedQuery(pickerQuery), 300)
    return () => window.clearTimeout(handle)
  }, [pickerQuery])

  useEffect(() => {
    if (!isPickerOpen) return
    let cancelled = false

    const load = async () => {
      setPickerLoading(true)
      try {
        const result = await apiClient.listTasks({
          q: pickerDebouncedQuery || undefined,
          pageSize: 20,
        })
        if (!cancelled) setPickerResults(result.items)
      } catch {
        if (!cancelled) setPickerResults([])
      } finally {
        if (!cancelled) setPickerLoading(false)
      }
    }

    void load()

    return () => {
      cancelled = true
    }
  }, [isPickerOpen, pickerDebouncedQuery])

  const openPicker = () => {
    setPickerQuery('')
    setPickerDebouncedQuery('')
    setIsPickerOpen(true)
  }

  const addStep = (task: TaskSummary) => {
    setSteps((prev) => [
      ...prev,
      {
        key: nextStepKey(),
        taskId: task.id,
        taskName: task.name,
        taskType: task.taskType,
        targetId: task.targetIds.length === 1 ? task.targetIds[0] : null,
        targetIds: task.targetIds,
        notes: '',
        isManualStep: false,
        manualStepTitle: '',
        manualStepInstructions: '',
        dataSource: '',
        dataOutput: '',
      },
    ])
    setIsPickerOpen(false)
  }

  const addManualStep = () => {
    setSteps((prev) => [
      ...prev,
      {
        key: nextStepKey(),
        taskId: null,
        taskName: '',
        taskType: null,
        targetId: null,
        targetIds: [],
        notes: '',
        isManualStep: true,
        manualStepTitle: '',
        manualStepInstructions: '',
        dataSource: '',
        dataOutput: '',
      },
    ])
  }

  const removeStep = (key: string) => {
    setSteps((prev) => prev.filter((s) => s.key !== key))
  }

  const moveStep = (index: number, direction: -1 | 1) => {
    setSteps((prev) => {
      const next = [...prev]
      const target = index + direction
      if (target < 0 || target >= next.length) return prev
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
  }

  const updateStep = (
    key: string,
    patch: Partial<
      Pick<
        EditableStep,
        | 'targetId'
        | 'notes'
        | 'manualStepTitle'
        | 'manualStepInstructions'
        | 'dataSource'
        | 'dataOutput'
      >
    >,
  ) => {
    setSteps((prev) => prev.map((s) => (s.key === key ? { ...s, ...patch } : s)))
  }

  const hasUnavailableStep = steps.some((s) => !s.isManualStep && (s.taskId === null || !s.targetId))
  const hasInvalidManualStep = steps.some((s) => s.isManualStep && !s.manualStepTitle.trim())
  const hasEnoughSteps = steps.length >= 2
  const trimmedName = name.trim()
  const trimmedProblem = problemStatement.trim()
  const trimmedOutcome = desiredOutcome.trim()
  const canSave =
    trimmedName.length > 0 &&
    trimmedProblem.length > 0 &&
    trimmedOutcome.length > 0 &&
    hasEnoughSteps &&
    !hasUnavailableStep &&
    !hasInvalidManualStep

  const handleSave = async () => {
    if (!canSave) return

    setIsSaving(true)
    setSaveError(null)
    try {
      const payload: WorkflowSaveRequest = {
        name: trimmedName,
        problemStatement: trimmedProblem,
        desiredOutcome: trimmedOutcome,
        steps: steps.map((s) => ({
          taskId: s.isManualStep ? null : (s.taskId as string),
          targetId: s.isManualStep ? null : s.targetId,
          notes: s.notes.trim() ? s.notes.trim() : null,
          isManualStep: s.isManualStep,
          manualStepTitle: s.isManualStep ? s.manualStepTitle.trim() : null,
          manualStepInstructions:
            s.isManualStep && s.manualStepInstructions.trim()
              ? s.manualStepInstructions.trim()
              : null,
          dataSource: s.dataSource.trim() ? s.dataSource.trim() : null,
          dataOutput: s.dataOutput.trim() ? s.dataOutput.trim() : null,
        })),
        expectedLastEditedAt: isEditMode ? lastEditedAt : undefined,
        ...(methodChanged ? { methodId } : {}),
      }

      const result =
        isEditMode && id
          ? await apiClient.updateWorkflow(id, payload)
          : await apiClient.createWorkflow(payload)

      navigate(`/workflows/${result.id}`)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setConflict(err.body as WorkflowDetail)
      } else if (err instanceof ApiError) {
        const body = err.body as { message?: string } | undefined
        setSaveError(body?.message ?? 'This workflow could not be saved. Please try again.')
      } else {
        setSaveError('This workflow could not be saved. Please try again.')
      }
    } finally {
      setIsSaving(false)
    }
  }

  const handleReloadFromConflict = () => {
    if (!conflict) return
    applyWorkflow(conflict)
    setConflict(null)
  }

  const handleKeepMyChanges = () => {
    if (!conflict) return
    // Adopt the newer lastEditedAt as our new concurrency baseline so the next Save attempt is
    // allowed through — this is the "reapply their change" half of the FR-007 conflict flow.
    setLastEditedAt(conflict.lastEditedAt)
    setConflict(null)
  }

  const handleDelete = async () => {
    if (!id) return
    setIsDeleting(true)
    try {
      await apiClient.deleteWorkflow(id)
      navigate('/')
    } catch {
      setIsDeleting(false)
      setIsDeleteConfirmOpen(false)
      setSaveError('This workflow could not be deleted. Please try again.')
    }
  }

  if (isLoading) {
    return (
      <div className={styles.loading}>
        <Spinner label="Loading workflow..." />
      </div>
    )
  }

  if (notFound) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>This workflow could not be found. It may have been deleted.</MessageBarBody>
      </MessageBar>
    )
  }

  return (
    <div>
      <Button
        className={styles.back}
        appearance="subtle"
        icon={<ArrowLeftRegular />}
        onClick={() => navigate(sourceId ? `/workflows/${sourceId}` : '/')}
      >
        {sourceId ? 'Back to workflow' : 'Back to search'}
      </Button>

      <div className={styles.header}>
        <Title1 as="h1">
          {isEditMode ? 'Edit workflow' : copyFromId ? 'Create new version as copy' : 'Create workflow'}
        </Title1>
        {isEditMode && (
          <Button
            appearance="subtle"
            icon={<DeleteRegular />}
            onClick={() => setIsDeleteConfirmOpen(true)}
          >
            Delete workflow
          </Button>
        )}
      </div>

      {copyFromId && (
        <MessageBar intent="info" style={{ marginBottom: tokens.spacingVerticalL }}>
          <MessageBarBody>{COPY_AS_NEW_VERSION_HINT}</MessageBarBody>
        </MessageBar>
      )}

      {conflict && (
        <MessageBar intent="warning" style={{ marginBottom: tokens.spacingVerticalL }}>
          <MessageBarBody>
            <MessageBarTitle>This workflow changed since you loaded it</MessageBarTitle>
            Edited by {conflict.lastEditedBy.displayName} on{' '}
            {new Date(conflict.lastEditedAt).toLocaleString()}. Reload to see their version, or keep
            your changes and save again.
          </MessageBarBody>
          <div className={styles.actions}>
            <Button size="small" onClick={handleReloadFromConflict}>
              Reload their version
            </Button>
            <Button size="small" appearance="primary" onClick={handleKeepMyChanges}>
              Keep my changes
            </Button>
          </div>
        </MessageBar>
      )}

      {saveError && (
        <MessageBar intent="error" style={{ marginBottom: tokens.spacingVerticalL }}>
          <MessageBarBody>{saveError}</MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.form}>
        <Field label="Name" required>
          <Input value={name} onChange={(_, data) => setName(data.value)} />
        </Field>

        <Field label="Problem statement" required hint="Markdown is supported.">
          {(control) => (
            <MarkdownContentEditor
              {...control}
              value={problemStatement}
              onChange={setProblemStatement}
              maxLength={2000}
              rows={10}
              disabled={isSaving}
            />
          )}
        </Field>

        <Field label="Desired outcome" required hint="Markdown is supported.">
          {(control) => (
            <MarkdownContentEditor
              {...control}
              value={desiredOutcome}
              onChange={setDesiredOutcome}
              maxLength={2000}
              rows={10}
              disabled={isSaving}
            />
          )}
        </Field>

        <MethodPicker
          methodId={methodId}
          method={method}
          disabled={isSaving}
          onChange={(value) => {
            setMethod(value)
            setMethodId(value?.id ?? null)
            setMethodChanged(true)
          }}
        />

        <div className={styles.section}>
          <div className={styles.sectionHeader}>
            <Title3 as="h2" style={{ fontSize: tokens.fontSizeBase500 }}>
              Steps
            </Title3>
            <div className={styles.stepControls}>
              <Button
                appearance="secondary"
                icon={<AddRegular />}
                onClick={openPicker}
                disabled={catalogIsEmpty}
              >
                Add step
              </Button>
              <Button appearance="secondary" icon={<AddRegular />} onClick={addManualStep}>
                Add manual step
              </Button>
            </div>
          </div>

          {catalogIsEmpty && (
            <MessageBar intent="info">
              <MessageBarBody>
                No tasks exist yet, so there is nothing to add as a step.{' '}
                <Link onClick={() => navigate('/tasks/new')}>Add a task first</Link>, then come back
                to build this workflow.
              </MessageBarBody>
            </MessageBar>
          )}

          {!hasEnoughSteps && (
            <MessageBar intent="warning">
              <MessageBarBody>
                A workflow needs at least 2 steps before it can be saved. Add {2 - steps.length}{' '}
                more step{2 - steps.length === 1 ? '' : 's'}.
              </MessageBarBody>
            </MessageBar>
          )}

          <div className={styles.steps}>
            {steps.map((step, index) => (
              <Card key={step.key} className={styles.step}>
                <div className={styles.stepNumber}>{index + 1}</div>
                <div className={styles.stepBody}>
                  <div className={styles.stepRow}>
                    <div className={styles.stepHeader}>
                      {step.isManualStep ? (
                        <Badge appearance="tint" color="informative">
                          Manual step
                        </Badge>
                      ) : (
                        <>
                          <strong>{step.taskName}</strong>
                          {step.taskId !== null && (
                            <Badge appearance="tint">
                              {TASK_TYPE_LABELS[step.taskType ?? ''] ??
                                step.taskType}
                            </Badge>
                          )}
                        </>
                      )}
                    </div>
                    <div className={styles.stepControls}>
                      <Button
                        appearance="subtle"
                        icon={<ArrowUpRegular />}
                        disabled={index === 0}
                        onClick={() => moveStep(index, -1)}
                        aria-label="Move step up"
                      />
                      <Button
                        appearance="subtle"
                        icon={<ArrowDownRegular />}
                        disabled={index === steps.length - 1}
                        onClick={() => moveStep(index, 1)}
                        aria-label="Move step down"
                      />
                      <Button
                        appearance="subtle"
                        icon={<DeleteRegular />}
                        onClick={() => removeStep(step.key)}
                        aria-label="Remove step"
                      />
                    </div>
                  </div>

                  {!step.isManualStep && step.taskId === null && (
                    <span className={styles.unavailable}>
                      <WarningRegular fontSize={16} /> This task was deleted. Remove this step or
                      replace it with another task before saving.
                    </span>
                  )}

                  {step.isManualStep && (
                    <>
                      <Field label="Step title" required>
                        <Input
                          value={step.manualStepTitle}
                          onChange={(_, data) =>
                            updateStep(step.key, { manualStepTitle: data.value })
                          }
                          placeholder="e.g. Review and approve the draft"
                        />
                      </Field>
                      <Field label="Instructions (optional)" hint="Markdown is supported.">
                        {(control) => (
                          <MarkdownContentEditor
                            {...control}
                            value={step.manualStepInstructions}
                            onChange={(manualStepInstructions) =>
                              updateStep(step.key, { manualStepInstructions })
                            }
                            maxLength={2000}
                            rows={10}
                            disabled={isSaving}
                          />
                        )}
                      </Field>
                    </>
                  )}

                  {!step.isManualStep && (
                    <Field label="Works in" required hint="Choose one of this task's documented targets.">
                      <TargetPicker kind={step.taskType ?? undefined}
                        value={step.targetId ? [step.targetId] : []}
                        allowedIds={step.targetIds}
                        disabled={isSaving}
                        onChange={(ids) => updateStep(step.key, { targetId: ids[0] ?? null })} />
                    </Field>
                  )}

                  <Field label="Notes (optional)">
                    <Input
                      value={step.notes}
                      onChange={(_, data) => updateStep(step.key, { notes: data.value })}
                      placeholder="Instructions specific to this step"
                    />
                  </Field>

                  <Field label="Data source (optional)">
                    <Input
                      value={step.dataSource}
                      onChange={(_, data) => updateStep(step.key, { dataSource: data.value })}
                      placeholder="What data feeds into this step?"
                    />
                  </Field>

                  <Field label="Data output (optional)">
                    <Input
                      value={step.dataOutput}
                      onChange={(_, data) => updateStep(step.key, { dataOutput: data.value })}
                      placeholder="What does this step produce?"
                    />
                  </Field>
                </div>
              </Card>
            ))}
          </div>
        </div>

        <div className={styles.actions}>
          <Button
            appearance="primary"
            disabled={!canSave || isSaving}
            onClick={() => void handleSave()}
          >
            {isSaving ? <Spinner size="tiny" /> : isEditMode ? 'Save changes' : 'Create workflow'}
          </Button>
          <Button
            appearance="secondary"
            onClick={() => navigate(sourceId ? `/workflows/${sourceId}` : '/')}
          >
            Cancel
          </Button>
        </div>
      </div>

      <Dialog open={isPickerOpen} onOpenChange={(_, data) => setIsPickerOpen(data.open)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Add a step</DialogTitle>
            <DialogContent>
              <SearchBox
                placeholder="Search tasks by keyword..."
                value={pickerQuery}
                onChange={(_, data) => setPickerQuery(data.value)}
                style={{ width: '100%', marginBottom: tokens.spacingVerticalM }}
              />
              {pickerLoading ? (
                <div className={styles.loading}>
                  <Spinner label="Searching..." />
                </div>
              ) : pickerResults.length === 0 ? (
                <Body1>No tasks match this search.</Body1>
              ) : (
                <div className={styles.pickerResults}>
                  {pickerResults.map((task) => (
                    <div
                      key={task.id}
                      className={styles.pickerRow}
                      role="button"
                      tabIndex={0}
                      onClick={() => addStep(task)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') addStep(task)
                      }}
                    >
                      <div>
                        <Body1>
                          <strong>{task.name}</strong>
                        </Body1>
                        <Caption1 block>{task.description}</Caption1>
                      </div>
                      <Toolbar>
                        <Badge appearance="tint">{TASK_TYPE_LABELS[task.taskType]}</Badge>
                        <Badge appearance="outline" color="brand">
                          {task.targetIds.map(targetLabel).join(', ')}
                        </Badge>
                      </Toolbar>
                    </div>
                  ))}
                </div>
              )}
            </DialogContent>
            <DialogActions>
              <DialogTrigger disableButtonEnhancement>
                <Button appearance="secondary">Close</Button>
              </DialogTrigger>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <Dialog
        open={isDeleteConfirmOpen}
        onOpenChange={(_, data) => setIsDeleteConfirmOpen(data.open)}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete this workflow?</DialogTitle>
            <DialogContent>
              This permanently deletes &ldquo;{name}&rdquo; and its steps. This cannot be undone.
            </DialogContent>
            <DialogActions>
              <DialogTrigger disableButtonEnhancement>
                <Button appearance="secondary">Cancel</Button>
              </DialogTrigger>
              <Button
                appearance="primary"
                disabled={isDeleting}
                onClick={() => void handleDelete()}
              >
                {isDeleting ? <Spinner size="tiny" /> : 'Delete workflow'}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  )
}
