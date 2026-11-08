import { useEffect, useState } from 'react'
import {
  Body1,
  Button,
  Caption1,
  Card,
  Field,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Textarea,
  Title3,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from '@fluentui/react-components'
import { apiClient } from '../services/apiClient'
import type { EngagementComment, EngagementKind } from '../services/types'

const MAX_COMMENT_LENGTH = 1000

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  list: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  item: {
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
  },
  meta: {
    color: tokens.colorNeutralForeground3,
  },
  counter: {
    textAlign: 'right',
    color: tokens.colorNeutralForeground3,
  },
  counterOverLimit: {
    color: tokens.colorPaletteRedForeground1,
  },
})

interface CommentsSectionProps {
  kind: EngagementKind
  id: string
}

/**
 * Free-text comments list (newest first) + add form (issue #60). Comments are attributed via the
 * app's existing anonymous-write pattern (shared "Anonymous contributor" user, or a real display
 * name if a client principal is ever present) and reuse the "Content policy" warning banner
 * pattern already used on the task/workflow editors. No edit/delete controls are provided.
 */
export function CommentsSection({ kind, id }: CommentsSectionProps) {
  const styles = useStyles()
  const [comments, setComments] = useState<EngagementComment[] | null>(null)
  const [draft, setDraft] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    apiClient
      .listComments(kind, id)
      .then((data) => {
        if (!cancelled) setComments(data.items)
      })
      .catch(() => {
        if (!cancelled) setComments([])
      })
    return () => {
      cancelled = true
    }
  }, [kind, id])

  const isOverLimit = draft.length > MAX_COMMENT_LENGTH
  const canSubmit = draft.trim().length > 0 && !isOverLimit && !isSubmitting

  async function handleSubmit() {
    if (!canSubmit) return
    setIsSubmitting(true)
    setError(null)
    try {
      const created = await apiClient.addComment(kind, id, draft.trim())
      setComments((prev) => [created, ...(prev ?? [])])
      setDraft('')
    } catch {
      setError('Could not post your comment right now. Please try again.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className={styles.root}>
      <Title3 as="h2" style={{ fontSize: tokens.fontSizeBase500 }}>
        Comments{comments && comments.length > 0 ? ` (${comments.length})` : ''}
      </Title3>

      <MessageBar intent="warning">
        <MessageBarBody>
          <MessageBarTitle>Content policy</MessageBarTitle>{' '}
          Don&rsquo;t include customer-identifiable or confidential customer data in comments.
        </MessageBarBody>
      </MessageBar>

      <Field
        validationState={isOverLimit ? 'error' : 'none'}
        validationMessage={
          isOverLimit ? `Comment must be ${MAX_COMMENT_LENGTH} characters or fewer.` : undefined
        }
      >
        <Textarea
          value={draft}
          onChange={(_, data) => setDraft(data.value)}
          placeholder="Share your experience with this..."
          resize="vertical"
          rows={3}
          disabled={isSubmitting}
        />
      </Field>
      <div className={mergeClasses(styles.counter, isOverLimit && styles.counterOverLimit)}>
        {draft.length} / {MAX_COMMENT_LENGTH}
      </div>
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      <div>
        <Button
          appearance="primary"
          onClick={() => void handleSubmit()}
          disabled={!canSubmit}
          icon={isSubmitting ? <Spinner size="tiny" /> : undefined}
        >
          {isSubmitting ? 'Posting...' : 'Post comment'}
        </Button>
      </div>

      {comments === null ? (
        <Spinner size="tiny" label="Loading comments..." />
      ) : comments.length === 0 ? (
        <Body1>No comments yet. Be the first to share your thoughts.</Body1>
      ) : (
        <div className={styles.list}>
          {comments.map((comment) => (
            <Card key={comment.id} className={styles.item}>
              <Body1>{comment.body}</Body1>
              <Caption1 className={styles.meta}>
                {comment.createdByDisplayName} · {new Date(comment.createdAt).toLocaleString()}
              </Caption1>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}
