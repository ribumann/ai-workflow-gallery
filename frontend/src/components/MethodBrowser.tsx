import { useEffect, useState } from 'react'
import {
  Body1,
  Button,
  Caption1,
  MessageBar,
  MessageBarBody,
  SearchBox,
  Spinner,
  Subtitle2,
} from '@fluentui/react-components'
import { apiClient } from '../services/apiClient'
import type { MethodRef, MethodSummary, PagedResult } from '../services/types'
import { methodError, useMethodStyles } from '../pages/Methods/methodUi'

/** Bounded picker over the method catalog: a user can reach any method without the caller
 * loading the whole thing. Browsing lives on the discovery page, so this is selection-only. */
export function MethodBrowser({
  onSelect,
  selectedId,
  disabled = false,
}: {
  onSelect: (method: MethodRef) => void
  selectedId?: string | null
  disabled?: boolean
}) {
  const styles = useMethodStyles()
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PagedResult<MethodSummary> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [revision, setRevision] = useState(0)

  useEffect(() => {
    let cancelled = false
    const load = async () => {
      setLoading(true)
      setError('')
      try {
        const data = await apiClient.listMethods({
          q: query.trim() || undefined,
          page,
          pageSize: 25,
        })
        if (!cancelled) setResult(data)
      } catch (err) {
        if (!cancelled) setError(methodError(err, 'Could not load methods. Please retry.'))
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    const timer = window.setTimeout(() => void load(), query ? 300 : 0)
    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [query, page, revision])

  function search(value: string) {
    if (value === query && page === 1) return
    setQuery(value)
    setPage(1)
    setLoading(true)
  }

  function goToPage(value: number) {
    setPage(value)
    setLoading(true)
  }

  return (
    <div className={styles.page}>
      <SearchBox
        className={styles.search}
        aria-label="Search methods"
        placeholder="Search titles and summaries"
        maxLength={200}
        value={query}
        disabled={disabled}
        onChange={(_, data) => search(data.value)}
        onKeyDown={(event) => {
          if (event.key === 'Enter') event.preventDefault()
        }}
      />
      {loading ? (
        <Spinner label="Loading methods..." />
      ) : error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            {error}{' '}
            <Button
              type="button"
              disabled={disabled}
              onClick={() => setRevision((value) => value + 1)}
            >
              Retry methods
            </Button>
          </MessageBarBody>
        </MessageBar>
      ) : (
        result && (
          <>
            {result.items.length ? (
              <ul className={styles.list} aria-label="Methods">
                {result.items.map((method) => (
                  <li key={method.id} className={styles.row}>
                    <div className={styles.rowText}>
                      <Subtitle2>{method.title}</Subtitle2>
                      <Body1 className={styles.measure}>{method.summary}</Body1>
                    </div>
                    <Button
                      type="button"
                      disabled={disabled || selectedId === method.id}
                      onClick={() =>
                        onSelect({ id: method.id, title: method.title, summary: method.summary })
                      }
                      aria-label={`Select ${method.title}`}
                    >
                      {selectedId === method.id ? 'Selected' : 'Select'}
                    </Button>
                  </li>
                ))}
              </ul>
            ) : (
              <div className={styles.intro}>
                <Subtitle2>
                  {query
                    ? 'No matching methods'
                    : page > 1
                      ? 'No methods on this page'
                      : 'No methods yet'}
                </Subtitle2>
                <Body1>
                  {query
                    ? 'Try another title or a few words from the summary.'
                    : page > 1
                      ? 'Return to a previous page to browse methods.'
                      : 'Create a method to share a reusable approach with colleagues.'}
                </Body1>
              </div>
            )}
            <div className={styles.actions} aria-label="Method pages">
              <Button
                type="button"
                disabled={disabled || page <= 1}
                onClick={() => goToPage(page - 1)}
              >
                Previous
              </Button>
              <Caption1 aria-live="polite">
                Page {page} of {Math.max(1, Math.ceil(result.totalCount / 25))}
              </Caption1>
              <Button
                type="button"
                disabled={disabled || page * 25 >= result.totalCount}
                onClick={() => goToPage(page + 1)}
              >
                Next
              </Button>
            </div>
          </>
        )
      )}
    </div>
  )
}
