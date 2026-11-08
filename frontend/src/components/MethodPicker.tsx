import { useEffect, useId, useState } from 'react'
import {
  Body1,
  Button,
  MessageBar,
  MessageBarBody,
  Spinner,
  Subtitle2,
} from '@fluentui/react-components'
import { apiClient } from '../services/apiClient'
import type { MethodRef } from '../services/types'
import { methodError, useMethodStyles } from '../pages/Methods/methodUi'
import { MethodBrowser } from './MethodBrowser'

export function MethodPicker({
  methodId,
  method,
  onChange,
  disabled = false,
}: {
  methodId: string | null
  method?: MethodRef | null
  onChange: (method: MethodRef | null) => void
  disabled?: boolean
}) {
  const styles = useMethodStyles()
  const headingId = useId()
  const resultsId = useId()
  const [open, setOpen] = useState(false)
  const [resolved, setResolved] = useState<MethodRef | null>(null)
  const [error, setError] = useState('')
  const [revision, setRevision] = useState(0)
  const selected = method?.id === methodId ? method : resolved?.id === methodId ? resolved : null

  useEffect(() => {
    if (!methodId || method?.id === methodId) return
    let cancelled = false
    void apiClient
      .getMethod(methodId)
      .then((value) => {
        if (!cancelled) {
          setResolved(value)
          setError('')
        }
      })
      .catch((err: unknown) => {
        if (!cancelled)
          setError(methodError(err, 'Could not load the selected method. Please retry.'))
      })
    return () => {
      cancelled = true
    }
  }, [methodId, method?.id, revision])

  return (
    <section className={styles.picker} aria-labelledby={headingId}>
      <Subtitle2 as="h2" id={headingId}>
        Primary method (optional)
      </Subtitle2>
      <Body1 className={styles.muted}>
        Explain the underlying approach. This does not add or change executable steps.
      </Body1>
      {selected ? (
        <div className={styles.intro}>
          <Body1>
            <strong>{selected.title}</strong>
          </Body1>
          <Body1 className={styles.measure}>{selected.summary}</Body1>
        </div>
      ) : methodId ? (
        error ? (
          <MessageBar intent="warning">
            <MessageBarBody>
              {error} Your selection is unchanged. Retry, choose another method, or clear it.
              <Button
                type="button"
                disabled={disabled}
                onClick={() => {
                  setError('')
                  setRevision((value) => value + 1)
                }}
              >
                Retry selected method
              </Button>
            </MessageBarBody>
          </MessageBar>
        ) : (
          <Spinner size="tiny" label="Loading selected method..." />
        )
      ) : (
        <Body1>No primary method selected.</Body1>
      )}
      <div className={styles.actions}>
        <Button
          type="button"
          disabled={disabled}
          aria-expanded={open}
          aria-controls={resultsId}
          onClick={() => setOpen(!open)}
        >
          {open ? 'Close method picker' : methodId ? 'Change method' : 'Choose method'}
        </Button>
        {methodId && (
          <Button
            type="button"
            disabled={disabled}
            onClick={() => {
              onChange(null)
              setError('')
            }}
          >
            Clear method
          </Button>
        )}
      </div>
      {open && (
        <div id={resultsId}>
          <MethodBrowser
            selectedId={methodId}
            disabled={disabled}
            onSelect={(value) => {
              onChange(value)
              setOpen(false)
              setError('')
            }}
          />
        </div>
      )}
    </section>
  )
}
