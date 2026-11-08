import { useState } from 'react'
import { useLocation } from 'react-router-dom'
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DialogTrigger,
  Field,
  MessageBar,
  MessageBarBody,
  Spinner,
  Textarea,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components'
import { ChatRegular, CheckmarkCircleRegular } from '@fluentui/react-icons'
import { apiClient, ApiError } from '../services/apiClient'
import type { FeedbackResponse } from '../services/types'

const MAX_MESSAGE_LENGTH = 2000

const useStyles = makeStyles({
  content: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minWidth: '24rem',
  },
  counter: {
    textAlign: 'right',
    color: tokens.colorNeutralForeground3,
  },
  counterOverLimit: {
    color: tokens.colorPaletteRedForeground1,
  },
  errorCode: {
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground3,
  },
})

type SubmitState =
  | { status: 'idle' }
  | { status: 'submitting' }
  | { status: 'success'; result: FeedbackResponse }
  | { status: 'error'; message: string; code?: number }

/**
 * Turns a failed submission into copy that tells the user what to do next, and keeps the HTTP
 * status visible so a report back to the maintainers is actionable without opening devtools.
 */
function describeFailure(err: unknown): { message: string; code?: number } {
  if (!(err instanceof ApiError)) {
    return { message: 'Could not reach the server. Check your connection and try again.' }
  }
  const { status } = err
  if (status === 401 || status === 403) {
    return { message: 'Your session expired. Refresh the page to sign in again, then resend.', code: status }
  }
  if (status === 429) {
    return { message: 'Too many submissions in a short time. Wait a moment and resend.', code: status }
  }
  if (status === 404 || status === 501 || (status === 503 && (err.body as { error?: string } | undefined)?.error === 'feedback_disabled')) {
    return {
      message: 'Feedback delivery is not available in this environment yet. Please tell the maintainers directly.',
      code: status,
    }
  }
  if (status === 502 || status === 503) {
    return {
      message: 'Feedback could not be confirmed as queued. Your text is kept below. It may already have been accepted; contact the gallery owner before retrying.',
      code: status,
    }
  }
  return { message: 'Could not submit feedback right now. Please try again shortly.', code: status }
}

/**
 * Global "Give feedback" entry point (T049/T050), reachable from every authenticated page via
 * AppShell. Sends free-text feedback to the maintainer mailbox. On failure, the typed message is
 * preserved (never cleared) so the user can retry without retyping it.
 */
export function FeedbackDialog() {
  const styles = useStyles()
  const location = useLocation()

  const [open, setOpen] = useState(false)
  const [message, setMessage] = useState('')
  const [state, setState] = useState<SubmitState>({ status: 'idle' })

  const isOverLimit = message.length > MAX_MESSAGE_LENGTH
  const canSubmit = message.trim().length > 0 && !isOverLimit && state.status !== 'submitting'

  function handleOpenChange(nextOpen: boolean) {
    setOpen(nextOpen)
    if (nextOpen) {
      // Reopening after a completed submission starts a clean form; reopening mid-error keeps the
      // typed message so the user can fix and retry without retyping it (FR-020).
      if (state.status === 'success') {
        setMessage('')
      }
      setState({ status: 'idle' })
    }
  }

  async function handleSubmit() {
    if (!canSubmit) return
    setState({ status: 'submitting' })
    try {
      const result = await apiClient.submitFeedback({
        message: message.trim(),
        pageContext: location.pathname + location.search,
      })
      setState({ status: 'success', result })
    } catch (err) {
      const failure = describeFailure(err)
      setState({ status: 'error', ...failure })
    }
  }

  return (
    <Dialog open={open} onOpenChange={(_, data) => handleOpenChange(data.open)}>
      <DialogTrigger disableButtonEnhancement>
        <Button appearance="subtle" icon={<ChatRegular />}>
          Give feedback
        </Button>
      </DialogTrigger>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Provide feedback</DialogTitle>
          <DialogContent className={styles.content}>
            {state.status === 'success' ? (
              <MessageBar intent="success" icon={<CheckmarkCircleRegular />}>
                <MessageBarBody>
                  Thanks! Your feedback was queued for delivery. Mailbox delivery is not yet confirmed.
                  {state.result.operationId ? ` Tracking id: ${state.result.operationId}.` : ''}
                </MessageBarBody>
              </MessageBar>
            ) : (
              <>
                <Field
                  label="What's on your mind?"
                  hint="Bugs, ideas, or anything else — this is emailed to the maintainers for triage."
                  validationState={isOverLimit ? 'error' : 'none'}
                  validationMessage={
                    isOverLimit
                      ? `Message must be ${MAX_MESSAGE_LENGTH} characters or fewer.`
                      : undefined
                  }
                >
                  <Textarea
                    value={message}
                    onChange={(_, data) => setMessage(data.value)}
                    placeholder="Tell us what happened, what you'd like to see, or what's not working..."
                    resize="vertical"
                    rows={6}
                    disabled={state.status === 'submitting'}
                  />
                </Field>
                <div
                  className={mergeClasses(styles.counter, isOverLimit && styles.counterOverLimit)}
                >
                  {message.length} / {MAX_MESSAGE_LENGTH}
                </div>
                {state.status === 'error' && (
                  <MessageBar intent="error">
                    <MessageBarBody>
                      {state.message}
                      {state.code ? (
                        <>
                          {' '}
                          <span className={styles.errorCode}>(error {state.code})</span>
                        </>
                      ) : null}
                    </MessageBarBody>
                  </MessageBar>
                )}
              </>
            )}
          </DialogContent>
          <DialogActions>
            <DialogTrigger disableButtonEnhancement>
              <Button appearance="secondary">
                {state.status === 'success' ? 'Close' : 'Cancel'}
              </Button>
            </DialogTrigger>
            {state.status !== 'success' && (
              <Button
                appearance="primary"
                onClick={() => void handleSubmit()}
                disabled={!canSubmit}
                icon={state.status === 'submitting' ? <Spinner size="tiny" /> : undefined}
              >
                {state.status === 'submitting' ? 'Submitting...' : 'Submit feedback'}
              </Button>
            )}
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
