import { useEffect, useState } from 'react'
import { Button, Caption1, makeStyles, tokens } from '@fluentui/react-components'
import {
  ThumbDislikeFilled,
  ThumbDislikeRegular,
  ThumbLikeFilled,
  ThumbLikeRegular,
} from '@fluentui/react-icons'
import { apiClient } from '../services/apiClient'
import type { EngagementKind, RatingSummary } from '../services/types'
import { getOrCreateDeviceId } from '../utils/deviceId'

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  count: {
    color: tokens.colorNeutralForeground3,
    minWidth: '1.25rem',
    textAlign: 'center',
  },
})

interface RatingWidgetProps {
  kind: EngagementKind
  id: string
}

/**
 * 👍/👎 ("Helpful" / "Not helpful") toggle rating (issue #60). One vote per browser via a
 * localStorage-persisted device id (see utils/deviceId.ts); clicking the currently-active choice
 * again removes the vote, clicking the other choice switches it. Informational only — does not
 * affect search ranking.
 */
export function RatingWidget({ kind, id }: RatingWidgetProps) {
  const styles = useStyles()
  const [summary, setSummary] = useState<RatingSummary | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    let cancelled = false
    const deviceId = getOrCreateDeviceId()
    apiClient
      .getRating(kind, id, deviceId)
      .then((data) => {
        if (!cancelled) setSummary(data)
      })
      .catch(() => {
        // Rating is a soft, informational feature — silently ignore load failures rather than
        // surfacing an error banner on the whole detail page.
      })
    return () => {
      cancelled = true
    }
  }, [kind, id])

  async function vote(value: 1 | -1) {
    if (isSubmitting) return
    setIsSubmitting(true)
    try {
      const deviceId = getOrCreateDeviceId()
      const data = await apiClient.rate(kind, id, deviceId, value)
      setSummary(data)
    } catch {
      // Best-effort; keep showing the previous summary rather than an error state.
    } finally {
      setIsSubmitting(false)
    }
  }

  if (!summary) {
    return null
  }

  return (
    <div className={styles.root}>
      <Button
        appearance="subtle"
        size="small"
        icon={summary.myVote === 1 ? <ThumbLikeFilled /> : <ThumbLikeRegular />}
        onClick={() => void vote(1)}
        disabled={isSubmitting}
        aria-pressed={summary.myVote === 1}
      >
        Helpful
      </Button>
      <Caption1 className={styles.count}>{summary.helpfulCount}</Caption1>
      <Button
        appearance="subtle"
        size="small"
        icon={summary.myVote === -1 ? <ThumbDislikeFilled /> : <ThumbDislikeRegular />}
        onClick={() => void vote(-1)}
        disabled={isSubmitting}
        aria-pressed={summary.myVote === -1}
      >
        Not helpful
      </Button>
      <Caption1 className={styles.count}>{summary.notHelpfulCount}</Caption1>
    </div>
  )
}
