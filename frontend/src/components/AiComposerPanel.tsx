import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Badge,
  Body1,
  Button,
  Caption1,
  Divider,
  Field,
  MessageBar,
  MessageBarBody,
  OverlayDrawer,
  DrawerBody,
  DrawerHeader,
  DrawerHeaderTitle,
  Spinner,
  Subtitle2,
  Textarea,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components'
import { AddRegular, DismissRegular, SparkleRegular } from '@fluentui/react-icons'
import { apiClient, ApiError } from '../services/apiClient'
import {
  TASK_TYPE_LABELS,
  type ComposeResponse,
} from '../services/types'
import { targetLabel } from '../services/catalog'
import type { AiComposerPrefill } from '../pages/WorkflowEditor/WorkflowEditorPage'
import type { TaskComposerPrefill } from '../pages/TaskEditor/TaskEditorPage'

const MAX_GOAL_LENGTH = 1000

const useStyles = makeStyles({
  drawer: {
    width: '28rem',
    maxWidth: '90vw',
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  counter: {
    textAlign: 'right',
    color: tokens.colorNeutralForeground3,
  },
  counterOverLimit: {
    color: tokens.colorPaletteRedForeground1,
  },
  resultSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  stepList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    ...shorthands.margin(0),
    ...shorthands.padding(0),
    listStyleType: 'none',
  },
  stepItem: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  stepBadges: {
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
  },
  gapItem: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.border('1px', 'dashed', tokens.colorPaletteMarigoldBorder2),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorPaletteMarigoldBackground1,
  },
})

type ComposeState =
  | { status: 'idle' }
  | { status: 'submitting' }
  | { status: 'success'; result: ComposeResponse }
  | { status: 'error'; message: string }

/**
 * Copilot-style fly-in AI assistant: describe a goal and get a task-mapping plus gap proposal
 * grounded on the current catalog. Closing the drawer without acting on a proposal discards all
 * state — nothing is saved or cached.
 */
export function AiComposerPanel({ open, onOpenChange, readOnly = false, canWrite = true, maxGoalLength = MAX_GOAL_LENGTH }: {
  open: boolean; onOpenChange: (open: boolean) => void; readOnly?: boolean; canWrite?: boolean; maxGoalLength?: number
}) {
  const styles = useStyles()
  const navigate = useNavigate()
  const [goal, setGoal] = useState('')
  const [state, setState] = useState<ComposeState>({ status: 'idle' })
  const generation = useRef(0)
  useEffect(() => () => { generation.current++ }, [])

  const canCreateFromProposal = canWrite && !readOnly
  const isOverLimit = goal.length > maxGoalLength
  const canSubmit = goal.trim().length > 0 && !isOverLimit && state.status !== 'submitting'

  function handleOpenChange(nextOpen: boolean) {
    onOpenChange(nextOpen)
    if (!nextOpen) {
      generation.current++
      // FR-013: discard everything on close, so reopening always starts a fresh proposal.
      setGoal('')
      setState({ status: 'idle' })
    }
  }

  async function handleSubmit() {
    if (!canSubmit) return
    const requestGeneration = ++generation.current
    setState({ status: 'submitting' })
    try {
      const result = await apiClient.composeWorkflow({ goal: goal.trim() })
      if (requestGeneration !== generation.current) return
      setState({ status: 'success', result })
    } catch (err) {
      if (requestGeneration !== generation.current) return
      if (err instanceof ApiError && err.status === 429) {
        const body = err.body as { retryAfterSeconds?: number } | undefined
        const retryAfter = body?.retryAfterSeconds
        setState({
          status: 'error',
          message: retryAfter
            ? `${readOnly ? 'The shared demo budget or concurrency limit has been reached.' : "You've reached the request limit."} Try again in about ${retryAfter} seconds.`
            : 'The request limit has been reached. Please try again later.',
        })
      } else if (err instanceof ApiError && (err.status === 404 || err.status === 503)) {
        setState({
          status: 'error',
          message: readOnly
            ? 'The Workflow composer is unavailable. You can still browse published tasks; try again later.'
            : err.status === 404
            ? 'The Workflow composer is not enabled in this environment. Create a workflow manually or contact the gallery owner.'
            : 'The Workflow composer is currently unavailable. Create a workflow manually or contact the gallery owner.',
        })
      } else if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        setState({ status: 'error', message: readOnly
          ? 'Anonymous proposals are not enabled. You can still browse published tasks.'
          : 'Your session or permissions could not be verified. Sign in again before requesting a proposal.' })
      } else if (err instanceof ApiError && err.status === 502) {
        setState({
          status: 'error',
          message: 'Could not get a proposal from the AI model right now. Please try again shortly.',
        })
      } else if (err instanceof ApiError && err.status === 400) {
        setState({ status: 'error', message: `Please describe a goal of 1-${maxGoalLength} characters.` })
      } else if (err instanceof ApiError && err.status === 422) {
        setState({ status: 'error', message: 'No relevant published tasks in this collection. Try a more specific goal.' })
      } else {
        setState({
          status: 'error',
          message: 'Could not reach the server. Please check your connection and try again.',
        })
      }
    }
  }

  function handleReset() {
    setGoal('')
    setState({ status: 'idle' })
  }

  // T083 (US2): hand the current proposal off to the workflow editor as a prefilled draft. The
  // AI's proposal is a starting point, not a saved workflow — the contributor still reviews and
  // saves it explicitly on the editor page (FR-006).
  function handleCreateWorkflow() {
    if (!canCreateFromProposal || state.status !== 'success') return
    const prefill: AiComposerPrefill = {
      name: state.result.suggestedName,
      problemStatement: state.result.problemStatement,
      desiredOutcome: state.result.suggestedOutcome,
      steps: state.result.steps.map((step) => ({
        taskId: step.taskId,
        taskName: step.taskName,
        taskType: step.taskType,
        targetId: step.targetId,
        notes: step.notes,
      })),
    }
    handleOpenChange(false)
    navigate('/workflows/new', { state: { aiComposerPrefill: prefill } })
  }

  // T087 (US3): hand a single gap off to the task editor as a prefilled draft, so a
  // contributor can quickly fill the catalog hole the AI identified.
  function handleCreateTaskForGap(gap: ComposeResponse['gaps'][number]) {
    if (!canCreateFromProposal) return
    const prefill: TaskComposerPrefill = {
      suggestedName: gap.suggestedTaskName ?? '',
      suggestedDescription: gap.suggestedTaskDescription ?? '',
    }
    handleOpenChange(false)
    navigate('/tasks/new', { state: { taskComposerPrefill: prefill } })
  }

  return (
    <OverlayDrawer
      position="end"
      open={open}
      onOpenChange={(_, data) => handleOpenChange(data.open)}
      className={styles.drawer}
    >
      <DrawerHeader>
        <DrawerHeaderTitle
          action={
            <Button
              appearance="subtle"
              aria-label="Close"
              icon={<DismissRegular />}
              onClick={() => handleOpenChange(false)}
            />
          }
        >
          <SparkleRegular /> Workflow composer
        </DrawerHeaderTitle>
      </DrawerHeader>
      <DrawerBody className={styles.body}>
        <Body1>
          Describe the outcome you're trying to achieve, and get a suggested workflow built from
          tasks already in the gallery.
        </Body1>
        {readOnly && <Caption1>
          Proposal only: you can review this result here, but this read-only collection cannot save it
          or open it as an editable draft. Your goal is sent to the configured AI model and is not
          saved as gallery history. Do not include personal or confidential information.
        </Caption1>}
        <Field
          label="What are you trying to achieve?"
          validationState={isOverLimit ? 'error' : 'none'}
          validationMessage={
            isOverLimit ? `Goal must be ${maxGoalLength} characters or fewer.` : undefined
          }
          hint={
            <span className={isOverLimit ? `${styles.counter} ${styles.counterOverLimit}` : styles.counter}>
              {goal.length} / {maxGoalLength}
            </span>
          }
        >
          <Textarea
            value={goal}
            onChange={(_, data) => setGoal(data.value)}
            placeholder="e.g. Summarize weekly customer feedback and draft a status update"
            resize="vertical"
            rows={4}
            disabled={state.status === 'submitting'}
            autoFocus
          />
        </Field>
        <Button
          appearance="primary"
          onClick={() => void handleSubmit()}
          disabled={!canSubmit}
          icon={state.status === 'submitting' ? <Spinner size="tiny" /> : <SparkleRegular />}
        >
          {state.status === 'submitting' ? 'Thinking...' : 'Get a proposal'}
        </Button>

        {state.status === 'error' && (
          <MessageBar intent="error">
            <MessageBarBody>{state.message}</MessageBarBody>
          </MessageBar>
        )}

        {state.status === 'success' && (
          <>
            <Divider />
            {/* Announces the new proposal to screen reader users, matching the error MessageBar's
                built-in auto-announce so a status change is never silent (T093). */}
            <MessageBar intent="success">
              <MessageBarBody>Proposal ready. Review the suggested steps and gaps below.</MessageBarBody>
            </MessageBar>
            <div className={styles.resultSection}>
              <Subtitle2>{state.result.suggestedName}</Subtitle2>
              <Body1>{state.result.problemStatement}</Body1>
              <Caption1>Suggested outcome: {state.result.suggestedOutcome}</Caption1>
            </div>

            {state.result.steps.length > 0 && (
              <div className={styles.resultSection}>
                <Subtitle2>Proposed steps</Subtitle2>
                <ol className={styles.stepList}>
                  {state.result.steps.map((step) => (
                    <li key={step.sequenceOrder} className={styles.stepItem}>
                      <Body1>
                        {step.sequenceOrder}. {step.taskName}
                      </Body1>
                      <div className={styles.stepBadges}>
                        <Badge appearance="tint">{TASK_TYPE_LABELS[step.taskType]}</Badge>
                        <Badge appearance="outline" color="brand">
                          {targetLabel(step.targetId)}
                        </Badge>
                      </div>
                      {step.notes && <Caption1>{step.notes}</Caption1>}
                    </li>
                  ))}
                </ol>
              </div>
            )}

            {state.result.gaps.length > 0 && (
              <div className={styles.resultSection}>
                <Subtitle2>Gaps — no matching task yet</Subtitle2>
                {/* Unordered: gaps have no sequence relationship to each other (unlike steps above). */}
                <ul className={styles.stepList}>
                  {state.result.gaps.map((gap, index) => (
                    <li key={index} className={styles.gapItem}>
                      <Body1>{gap.description}</Body1>
                      {gap.suggestedTaskName && (
                        <Caption1>Suggested task: {gap.suggestedTaskName}</Caption1>
                      )}
                      {canCreateFromProposal && gap.suggestedTaskName && gap.suggestedTaskDescription && (
                        <Button
                          appearance="outline"
                          size="small"
                          icon={<AddRegular />}
                          onClick={() => handleCreateTaskForGap(gap)}
                        >
                          Add task
                        </Button>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {canCreateFromProposal && state.result.steps.length > 0 && (
              <Button appearance="primary" onClick={handleCreateWorkflow}>
                Review and save workflow
              </Button>
            )}

            <Button appearance="secondary" onClick={handleReset}>
              Start a new proposal
            </Button>
          </>
        )}
      </DrawerBody>
    </OverlayDrawer>
  )
}
