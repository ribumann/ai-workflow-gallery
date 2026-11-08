import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useAuth } from '../../auth/useAuth'
import { canWriteCollection } from '../../services/deployment'
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
  Link,
  MessageBar,
  MessageBarBody,
  Spinner,
  Title1,
  Title3,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components'
import {
  ArrowLeftRegular,
  CopyRegular,
  DeleteRegular,
  DocumentArrowDownRegular,
  EditRegular,
  OpenRegular,
} from '@fluentui/react-icons'
import { apiClient, ApiError } from '../../services/apiClient'
import { SignInRequiredError } from '../../auth/entraClient'
import {
  TASK_TYPE_LABELS,
  type TaskDetail,
  type WorkflowRef,
} from '../../services/types'
import { catalog, targetLabel } from '../../services/catalog'
import { isSafeHttpUrl } from '../../utils/url'
import { COPY_AS_NEW_VERSION_LABEL, copyAsNewVersionPath } from '../../utils/versionCopy'
import { CommentsSection } from '../../components/CommentsSection'
import { MarkdownContent } from '../../components/MarkdownContent'
import { RatingWidget } from '../../components/RatingWidget'
import { ShareMenu } from '../../components/ShareMenu'
import { PrimaryMethod } from '../../components/PrimaryMethod'

const useStyles = makeStyles({
  back: {
    marginBottom: tokens.spacingVerticalM,
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalL,
  },
  headerTop: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  headerActions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  badges: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  meta: {
    color: tokens.colorNeutralForeground3,
  },
  evidenceParagraph: {
    display: 'block',
    marginTop: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalS,
  },
  content: {
    ...shorthands.padding(tokens.spacingVerticalL),
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  dataFields: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXXL,
    rowGap: tokens.spacingVerticalM,
    '& > div': {
      flex: '1 1 16rem',
      minWidth: 0,
      overflowWrap: 'anywhere',
    },
  },
  fieldHeading: {
    display: 'block',
    marginTop: 0,
    marginBottom: tokens.spacingVerticalXS,
    fontSize: tokens.fontSizeBase400,
  },
  referenceLink: {
    overflowWrap: 'anywhere',
  },
  loading: {
    display: 'flex',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalXXL, 0),
  },
  dialogContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  conflictList: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalL,
  },
})

/** Read-only Task detail page (T025): full metadata, inline content viewer, and an attached-file
 * download link. Editing is added by User Story 2. */
export function TaskDetailPage() {
  const styles = useStyles()
  const navigate = useNavigate()
  const auth = useAuth()
  const canWrite = canWriteCollection(auth.publicDemo, auth.collectionCapabilities)
  const { id } = useParams<{ id: string }>()

  const [task, setTask] = useState<TaskDetail | null>(null)
  const [notFound, setNotFound] = useState(false)
  const [loadError, setLoadError] = useState(false)
  const [isLoading, setIsLoading] = useState(true)
  const [isDownloading, setIsDownloading] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const downloadRequest = useRef<AbortController | null>(null)

  useEffect(() => () => downloadRequest.current?.abort(), [id])

  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false)
  const [deleteConflict, setDeleteConflict] = useState<WorkflowRef[] | null>(null)
  const [isDeleting, setIsDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    let cancelled = false

    const load = async () => {
      setIsLoading(true)
      setNotFound(false)
      setLoadError(false)
      try {
        const data = await apiClient.getTask(id)
        if (!cancelled) setTask(data)
      } catch (err) {
        if (!cancelled) {
          if (err instanceof ApiError && err.status === 404) setNotFound(true)
          else setLoadError(true)
          setTask(null)
        }
      } finally {
        if (!cancelled) setIsLoading(false)
      }
    }

    void load()

    return () => {
      cancelled = true
    }
  }, [id])

  const openDeleteDialog = () => {
    setDeleteConflict(null)
    setDeleteError(null)
    setIsDeleteDialogOpen(true)
  }

  const downloadAttachment = async () => {
    if (!task?.attachedFile || downloadRequest.current) return
    const controller = new AbortController()
    downloadRequest.current = controller
    setIsDownloading(true)
    setDownloadError(null)
    try {
      const blob = await apiClient.getTaskFile(task.id, controller.signal)
      if (controller.signal.aborted) return
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      try {
        link.href = url
        link.download = task.attachedFile.fileName
        document.body.appendChild(link)
        link.click()
      } finally {
        link.remove()
        // Let the browser consume the download before releasing its backing bytes.
        setTimeout(() => URL.revokeObjectURL(url), 0)
      }
    } catch (err) {
      if (!controller.signal.aborted) {
        setDownloadError(
          err instanceof SignInRequiredError || err instanceof ApiError && err.status === 401
            ? 'Sign in again, then retry downloading this file.'
            : 'This file could not be downloaded. Try again.',
        )
      }
    } finally {
      downloadRequest.current = null
      setIsDownloading(false)
    }
  }

  const requestDelete = async (confirm: boolean) => {
    if (!id) return
    setIsDeleting(true)
    setDeleteError(null)
    try {
      await apiClient.deleteTask(id, confirm)
      navigate('/')
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        const body = err.body as { referencedByWorkflows: WorkflowRef[] } | undefined
        setDeleteConflict(body?.referencedByWorkflows ?? [])
      } else {
        setDeleteError('This task could not be deleted. Try again.')
      }
    } finally {
      setIsDeleting(false)
    }
  }

  if (isLoading) {
    return (
      <div className={styles.loading}>
        <Spinner label="Loading task..." />
      </div>
    )
  }

  if (notFound) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>This task could not be found. It may have been deleted.</MessageBarBody>
      </MessageBar>
    )
  }

  if (loadError || !task) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>The task could not be loaded. Please try again.</MessageBarBody>
      </MessageBar>
    )
  }

  return (
    <div>
      <Button
        className={styles.back}
        appearance="subtle"
        icon={<ArrowLeftRegular />}
        onClick={() => navigate('/')}
      >
        Back to search
      </Button>

      <div className={styles.header}>
        <div className={styles.headerTop}>
          <Title1 as="h1">{task.name}</Title1>
          <div className={styles.headerActions}>
            <ShareMenu title={task.name} />
            {canWrite && <>
            <Button icon={<EditRegular />} onClick={() => navigate(`/tasks/${task.id}/edit`)}>
              Edit
            </Button>
            <Button
              icon={<CopyRegular />}
              onClick={() => navigate(copyAsNewVersionPath('/tasks/new', task.id))}
            >
              {COPY_AS_NEW_VERSION_LABEL}
            </Button>
            <Button icon={<DeleteRegular />} onClick={openDeleteDialog}>
              Delete
            </Button>
            </>}
          </div>
        </div>
        <div className={styles.badges}>
          <Badge appearance="tint" size="large">
            {TASK_TYPE_LABELS[task.taskType]}
          </Badge>
          {task.targetIds.map((id) => <Badge key={id} appearance="outline" color="brand" size="large">{targetLabel(id)}</Badge>)}
          {task.tags.map((tag) => (
            <Badge key={tag} appearance="ghost">
              {tag}
            </Badge>
          ))}
        </div>
        <MarkdownContent content={task.description} />
        <Caption1 className={styles.meta}>
          Created by {task.createdBy.displayName} · Last edited by {task.lastEditedBy.displayName}{' '}
          on {new Date(task.lastEditedAt).toLocaleString()}
        </Caption1>
        {canWrite && <RatingWidget kind="tasks" id={task.id} />}
      </div>

      <PrimaryMethod method={task.method} methodId={task.methodId} />

      <details>
        <summary>Compatibility, setup &amp; access</summary>
        {task.format && <Body1 as="p" className={styles.evidenceParagraph}>Format: {catalog.formats.find((format) => format.id === task.format)?.label ?? task.format}</Body1>}
        {task.setup && <Body1 as="p" className={styles.evidenceParagraph}>Setup: {task.setup}</Body1>}
        {task.prerequisites && <Body1 as="p" className={styles.evidenceParagraph}>Additional prerequisites: {task.prerequisites}</Body1>}
        {task.targetIds.map((id) => {
          const target = catalog.targets.find((candidate) => candidate.id === id)
          return <div key={id}>
            <Body1 as="p" className={styles.evidenceParagraph}><strong>{targetLabel(id)}:</strong> {target?.prerequisites}</Body1>
            {target?.profiles.filter((profile) => profile.kind === task.taskType &&
              (!task.format || profile.formats.includes(task.format))).map((profile) =>
              <Body1 as="p" className={styles.evidenceParagraph} key={profile.kind + profile.sourceUrl}>
                <Link href={profile.sourceUrl} target="_blank" rel="noreferrer">Public compatibility source</Link>
                {' '}(reviewed {profile.reviewedOn}). {profile.note}
              </Body1>)}
          </div>
        })}
        <Body1 as="p" className={styles.evidenceParagraph}>Documented compatibility is not a verified gallery connection or an installed integration. Unknown combinations are not supported.</Body1>
      </details>

      <Card className={styles.content}>
        {task.referenceLink && isSafeHttpUrl(task.referenceLink) && (
          <div>
            <Title3 as="h3" className={styles.fieldHeading}>
              Reference link
            </Title3>
            <Link className={styles.referenceLink} href={task.referenceLink} target="_blank" rel="noreferrer">
              <OpenRegular fontSize={16} /> {task.referenceLink}
            </Link>
          </div>
        )}

        {task.inlineContent && (
          <div>
            <Title3 as="h3" className={styles.fieldHeading}>
              Content
            </Title3>
            <MarkdownContent content={task.inlineContent} />
          </div>
        )}

        {(task.dataSource || task.dataOutput) && (
          <div className={styles.dataFields}>
            {task.dataSource && (
              <div>
                <Title3 as="h3" className={styles.fieldHeading}>
                  Data source
                </Title3>
                <Body1>{task.dataSource}</Body1>
              </div>
            )}
            {task.dataOutput && (
              <div>
                <Title3 as="h3" className={styles.fieldHeading}>
                  Data output
                </Title3>
                <Body1>{task.dataOutput}</Body1>
              </div>
            )}
          </div>
        )}

        {task.attachedFile && (
          <div>
            <Title3 as="h3" className={styles.fieldHeading}>
              Attached file
            </Title3>
            <Button
              appearance="transparent"
              icon={<DocumentArrowDownRegular />}
              disabled={isDownloading}
              onClick={() => void downloadAttachment()}
            >
              {task.attachedFile.fileName} (
              {formatBytes(task.attachedFile.sizeBytes)})
            </Button>
            {isDownloading && <Spinner size="tiny" label="Downloading file..." />}
            {downloadError && (
              <MessageBar intent="error">
                <MessageBarBody>{downloadError}</MessageBarBody>
              </MessageBar>
            )}
          </div>
        )}

        {!task.referenceLink && !task.inlineContent && !task.attachedFile && (
          <Body1>This task has no content attached yet.</Body1>
        )}
      </Card>

      {task.referencedByWorkflowIds.length > 0 && (
        <MessageBar intent="info" style={{ marginTop: tokens.spacingVerticalL }}>
          <MessageBarBody>
            Used in {task.referencedByWorkflowIds.length} workflow
            {task.referencedByWorkflowIds.length === 1 ? '' : 's'}.
          </MessageBarBody>
        </MessageBar>
      )}

      <div style={{ marginTop: tokens.spacingVerticalXXL }}>
        {canWrite && <CommentsSection kind="tasks" id={task.id} />}
      </div>

      <Dialog
        open={canWrite && isDeleteDialogOpen}
        onOpenChange={(_, data) => setIsDeleteDialogOpen(data.open)}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete this task?</DialogTitle>
            <DialogContent className={styles.dialogContent}>
              {!deleteConflict && (
                <Body1>
                  This will permanently remove &ldquo;{task.name}&rdquo;. This can&rsquo;t be
                  undone.
                </Body1>
              )}
              {deleteConflict && deleteConflict.length > 0 && (
                <>
                  <MessageBar intent="warning">
                    <MessageBarBody>
                      This task is used in {deleteConflict.length} workflow
                      {deleteConflict.length === 1 ? '' : 's'}. Deleting it will mark those steps as
                      unavailable rather than remove the workflows.
                    </MessageBarBody>
                  </MessageBar>
                  <ul className={styles.conflictList}>
                    {deleteConflict.map((workflow) => (
                      <li key={workflow.id}>{workflow.name}</li>
                    ))}
                  </ul>
                </>
              )}
              {deleteError && (
                <MessageBar intent="error">
                  <MessageBarBody>{deleteError}</MessageBarBody>
                </MessageBar>
              )}
            </DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                disabled={isDeleting}
                onClick={() => setIsDeleteDialogOpen(false)}
              >
                Cancel
              </Button>
              <Button
                appearance="primary"
                disabled={isDeleting}
                onClick={() => void requestDelete(Boolean(deleteConflict))}
              >
                {isDeleting ? <Spinner size="tiny" /> : deleteConflict ? 'Delete anyway' : 'Delete'}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  )
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
