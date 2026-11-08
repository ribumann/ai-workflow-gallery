import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useAuth } from '../../auth/useAuth'
import { canWriteCollection } from '../../services/deployment'
import {
  Badge,
  Button,
  Caption1,
  MessageBar,
  MessageBarBody,
  Spinner,
  Title1,
} from '@fluentui/react-components'
import {
  ArrowLeftRegular,
  ArrowUpRightRegular,
  CopyRegular,
  EditRegular,
} from '@fluentui/react-icons'
import { apiClient } from '../../services/apiClient'
import { MarkdownContent } from '../../components/MarkdownContent'
import type { ReportLink } from '../../services/types'
import { canEmbedReportUrl, getReportEmbedCaution, isSafeReportUrl } from '../../utils/url'
import { COPY_AS_NEW_VERSION_LABEL, copyAsNewVersionPath } from '../../utils/versionCopy'
import { reportError, useReportStyles } from './reportUi'

export function ReportViewerPage() {
  const { id } = useParams<{ id: string }>()
  return <ReportViewer key={id} id={id!} />
}

function ReportViewer({ id }: { id: string }) {
  const styles = useReportStyles()
  const navigate = useNavigate()
  const auth = useAuth()
  const canWrite = canWriteCollection(auth.publicDemo, auth.collectionCapabilities)
  const [item, setItem] = useState<ReportLink | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [revision, setRevision] = useState(0)

  useEffect(() => {
    let cancelled = false
    void apiClient
      .getReport(id)
      .then((next) => {
        if (!cancelled) {
          setItem(next)
          setError('')
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(reportError(err, 'Could not load this link. Please retry.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [id, revision])

  return (
    <div className={styles.page}>
      <div>
        <Button
          appearance="subtle"
          icon={<ArrowLeftRegular />}
          onClick={() => navigate('/reports')}
        >
          Back to directory
        </Button>
      </div>
      {loading ? (
        <Spinner label="Loading link..." />
      ) : error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            {error}{' '}
            <Button
              onClick={() => {
                setLoading(true)
                setRevision((value) => value + 1)
              }}
            >
              Retry
            </Button>
          </MessageBarBody>
        </MessageBar>
      ) : (
        item && (
          <>
            <div className={styles.header}>
              <div className={styles.intro}>
                <Title1 as="h1" className={styles.heading}>
                  {item.name}
                </Title1>
                <MarkdownContent content={item.description} />
                <div className={styles.metadata}>
                  <Badge appearance="tint">
                    {item.kind === 'Report' ? 'Report' : 'AI Dashboard'}
                  </Badge>
                  <Caption1>
                    Added by {item.createdByDisplayName} · Last edited by{' '}
                    {item.lastEditedByDisplayName}
                  </Caption1>
                </div>
              </div>
              <div className={styles.actions}>
                {isSafeReportUrl(item.url) && (
                  <Button
                    as="a"
                    appearance="primary"
                    href={item.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    icon={<ArrowUpRightRegular />}
                  >
                    Open externally
                  </Button>
                )}
                {canWrite && <><Button icon={<EditRegular />} onClick={() => navigate(`/reports/${item.id}/edit`)}>
                  Edit link
                </Button>
                <Button
                  icon={<CopyRegular />}
                  onClick={() => navigate(copyAsNewVersionPath('/reports/new', item.id))}
                >
                  {COPY_AS_NEW_VERSION_LABEL}
                </Button>
                </>}
              </div>
            </div>
            {canEmbedReportUrl(item.url, window.location.origin) ? (
              <ReportPreview key={item.url} name={item.name} url={item.url} />
            ) : (
              <MessageBar intent="warning">
                <MessageBarBody>
                  {isSafeReportUrl(item.url)
                    ? 'Links to this gallery cannot be embedded inside itself. Open this link externally.'
                    : 'This destination is not a valid HTTPS URL. Edit the link to fix it before opening.'}
                </MessageBarBody>
              </MessageBar>
            )}
          </>
        )
      )}
    </div>
  )
}

function ReportPreview({ name, url }: { name: string; url: string }) {
  const styles = useReportStyles()
  const destination = new URL(url)
  const caution = getReportEmbedCaution(destination)
  const [previewVisible, setPreviewVisible] = useState(!caution)

  if (!previewVisible) {
    return (
      <MessageBar intent="info">
        <MessageBarBody>
          {caution ??
            'Preview hidden. Choose Open externally to view the destination in a new tab.'}{' '}
          <Button appearance="subtle" onClick={() => setPreviewVisible(true)}>
            Try preview
          </Button>
        </MessageBarBody>
      </MessageBar>
    )
  }

  return (
    <>
      <MessageBar intent="info">
        <MessageBarBody>
          Viewing content from <strong>{destination.hostname}</strong>. If the frame is blank,
          refuses to connect or sign-in does not work, choose Open externally or hide the preview.
          Gallery sign-in does not grant access to the destination.{' '}
          <Button appearance="subtle" onClick={() => setPreviewVisible(false)}>
            Hide preview
          </Button>
        </MessageBarBody>
      </MessageBar>
      <iframe
        title={name}
        src={url}
        className={styles.frame}
        referrerPolicy="no-referrer"
        sandbox="allow-scripts allow-same-origin allow-forms allow-popups allow-popups-to-escape-sandbox allow-downloads"
      />
    </>
  )
}
