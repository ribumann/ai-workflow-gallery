import { useEffect, useRef, useState } from 'react'
import { useAuth } from '../../auth/useAuth'
import { canWriteCollection } from '../../services/deployment'
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom'
import {
  Body1,
  Button,
  Caption1,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  MessageBar,
  MessageBarBody,
  Spinner,
  Title1,
  Title3,
} from '@fluentui/react-components'
import { ArrowLeftRegular, CopyRegular, DeleteRegular, EditRegular } from '@fluentui/react-icons'
import { MarkdownContent } from '../../components/MarkdownContent'
import { CommentsSection } from '../../components/CommentsSection'
import { RatingWidget } from '../../components/RatingWidget'
import { apiClient, ApiError } from '../../services/apiClient'
import type { MethodDeleteConflict, MethodDetail } from '../../services/types'
import { COPY_AS_NEW_VERSION_LABEL, copyAsNewVersionPath } from '../../utils/versionCopy'
import { METHODS_BROWSE_PATH, methodError, methodReferences, useMethodStyles } from './methodUi'

export function MethodDetailPage() {
  const { id } = useParams<{ id: string }>()
  return <MethodDetailView key={id} id={id!} />
}

function MethodDetailView({ id }: { id: string }) {
  const styles = useMethodStyles()
  const navigate = useNavigate()
  const auth = useAuth()
  const canWrite = canWriteCollection(auth.publicDemo, auth.collectionCapabilities)
  const [item, setItem] = useState<MethodDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [revision, setRevision] = useState(0)
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)
  const [deleteError, setDeleteError] = useState('')
  const [references, setReferences] = useState<MethodDeleteConflict | null>(null)
  const active = useRef(true)

  useEffect(() => {
    active.current = true
    return () => {
      active.current = false
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    void apiClient
      .getMethod(id)
      .then((value) => {
        if (!cancelled) {
          setItem(value)
          setError('')
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setItem(null)
          setError(methodError(err, 'Could not load this method. Please retry.'))
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [id, revision])

  async function remove() {
    setBusy(true)
    setDeleteError('')
    setReferences(null)
    try {
      await apiClient.deleteMethod(id)
      if (active.current) {
        setConfirm(false)
        navigate(METHODS_BROWSE_PATH, { replace: true })
      }
    } catch (err) {
      if (active.current) {
        if (err instanceof ApiError && err.status === 404) {
          setConfirm(false)
          setItem(null)
          setError(methodError(err, ''))
        } else {
          setReferences(methodReferences(err))
          setDeleteError(methodError(err, 'Could not delete this method. Please retry.'))
        }
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
          onClick={() => navigate(METHODS_BROWSE_PATH)}
        >
          Back to search
        </Button>
      </div>
      {loading ? (
        <Spinner label="Loading method..." />
      ) : !item ? (
        <>
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
          <div>
            <Button
              onClick={() => {
                setLoading(true)
                setRevision((value) => value + 1)
              }}
            >
              Retry
            </Button>
          </div>
        </>
      ) : (
        <>
          <div className={styles.header}>
            <div className={styles.intro}>
              <Title1 as="h1" className={styles.heading}>
                {item.title}
              </Title1>
              <Body1 className={styles.measure}>{item.summary}</Body1>
            </div>
            {canWrite && <div className={styles.actions}>
              <Button icon={<EditRegular />} onClick={() => navigate(`/methods/${id}/edit`)}>
                Edit method
              </Button>
              <Button
                icon={<CopyRegular />}
                onClick={() => navigate(copyAsNewVersionPath('/methods/new', id))}
              >
                {COPY_AS_NEW_VERSION_LABEL}
              </Button>
              {item.canDelete && (
                <Button
                  icon={<DeleteRegular />}
                  onClick={() => {
                    setConfirm(true)
                    setReferences(null)
                    setDeleteError('')
                  }}
                >
                  Delete method
                </Button>
              )}
            </div>}
          </div>
          <div className={styles.intro}>
            <Caption1 className={styles.muted}>
              Created by {item.createdBy.displayName} on {new Date(item.createdAt).toLocaleString()}
            </Caption1>
            <Caption1 className={styles.muted}>
              Last edited by {item.lastEditedBy.displayName} on{' '}
              {new Date(item.lastEditedAt).toLocaleString()}
            </Caption1>
            <Caption1 className={styles.measure}>Version: {item.version}</Caption1>
            {canWrite && <RatingWidget kind="methods" id={item.id} />}
          </div>
          <section className={styles.intro} aria-label="Written approach">
            <Title3 as="h2">Written approach</Title3>
            <MarkdownContent content={item.approach} />
          </section>
          {canWrite && <CommentsSection kind="methods" id={item.id} />}
          <Dialog
            open={confirm}
            onOpenChange={(_, data) => {
              if (!busy) setConfirm(data.open)
            }}
          >
            <DialogSurface>
              <DialogBody>
                <DialogTitle>Delete this method?</DialogTitle>
                <DialogContent className={styles.page}>
                  <Body1 className={styles.measure}>
                    Delete "{item.title}"? This cannot be undone. Methods linked to tasks or
                    workflows cannot be deleted.
                  </Body1>
                  {deleteError && (
                    <MessageBar intent="error">
                      <MessageBarBody>{references?.message ?? deleteError}</MessageBarBody>
                    </MessageBar>
                  )}
                  {references && (
                    <div className={styles.intro}>
                      <Body1>
                        Open these items and clear or change their primary method before retrying.
                        Nothing has been deleted.
                      </Body1>
                      {references.referencedByTasks.map((ref) => (
                        <RouterLink
                          key={ref.id}
                          className={styles.link}
                          to={`/tasks/${encodeURIComponent(ref.id)}`}
                        >
                          Task: {ref.name}
                        </RouterLink>
                      ))}
                      {references.referencedByWorkflows.map((ref) => (
                        <RouterLink
                          key={ref.id}
                          className={styles.link}
                          to={`/workflows/${encodeURIComponent(ref.id)}`}
                        >
                          Workflow: {ref.name}
                        </RouterLink>
                      ))}
                    </div>
                  )}
                </DialogContent>
                <DialogActions>
                  <Button disabled={busy} onClick={() => setConfirm(false)}>
                    Cancel
                  </Button>
                  <Button appearance="primary" disabled={busy} onClick={() => void remove()}>
                    {busy ? 'Deleting...' : 'Confirm delete'}
                  </Button>
                </DialogActions>
              </DialogBody>
            </DialogSurface>
          </Dialog>
        </>
      )}
    </div>
  )
}
