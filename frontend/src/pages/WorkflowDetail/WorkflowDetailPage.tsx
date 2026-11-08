import { useEffect, useState } from 'react'
import { useNavigate, useParams, Link as RouterLink } from 'react-router-dom'
import { useAuth } from '../../auth/useAuth'
import { canWriteCollection } from '../../services/deployment'
import {
  Badge,
  Body1,
  Button,
  Caption1,
  Card,
  MessageBar,
  MessageBarBody,
  Spinner,
  Title1,
  Title3,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components'
import { ArrowLeftRegular, CopyRegular, EditRegular, WarningRegular } from '@fluentui/react-icons'
import { apiClient, ApiError } from '../../services/apiClient'
import { TASK_TYPE_LABELS, type WorkflowDetail } from '../../services/types'
import { targetLabel } from '../../services/catalog'
import { CommentsSection } from '../../components/CommentsSection'
import { MarkdownContent } from '../../components/MarkdownContent'
import { RatingWidget } from '../../components/RatingWidget'
import { ShareMenu } from '../../components/ShareMenu'
import { PrimaryMethod } from '../../components/PrimaryMethod'
import { COPY_AS_NEW_VERSION_LABEL, copyAsNewVersionPath } from '../../utils/versionCopy'

const useStyles = makeStyles({
  topRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalM,
    marginBottom: tokens.spacingVerticalM,
  },
  back: {
    marginBottom: 0,
  },
  headerActions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalL,
  },
  meta: {
    color: tokens.colorNeutralForeground3,
  },
  section: {
    marginBottom: tokens.spacingVerticalL,
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
  },
  stepBody: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    flexGrow: 1,
    minWidth: 0,
    overflowWrap: 'anywhere',
  },
  stepHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  unavailable: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorPaletteMarigoldForeground1,
  },
  dataFields: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalXS,
  },
  taskLink: {
    color: tokens.colorBrandForegroundLink,
    textDecorationLine: 'none',
    ':hover': {
      textDecorationLine: 'underline',
    },
  },
  loading: {
    display: 'flex',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalXXL, 0),
  },
})

/** Workflow detail page (T026, T044): problem statement, desired outcome, ordered steps with
 * each step's tool/surface, an "task no longer available" indicator per step when
 * `taskAvailable: false` (FR-008, FR-010), and createdBy/lastEditedBy attribution (FR-011).
 * Links into the shared `WorkflowEditor` page (US3) for editing. */
export function WorkflowDetailPage() {
  const styles = useStyles()
  const navigate = useNavigate()
  const auth = useAuth()
  const canWrite = canWriteCollection(auth.publicDemo, auth.collectionCapabilities)
  const { id } = useParams<{ id: string }>()

  const [workflow, setWorkflow] = useState<WorkflowDetail | null>(null)
  const [notFound, setNotFound] = useState(false)
  const [loadError, setLoadError] = useState(false)
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    if (!id) return
    let cancelled = false

    const load = async () => {
      setIsLoading(true)
      setNotFound(false)
      setLoadError(false)
      try {
        const data = await apiClient.getWorkflow(id)
        if (!cancelled) setWorkflow(data)
      } catch (err) {
        if (!cancelled) {
          if (err instanceof ApiError && err.status === 404) setNotFound(true)
          else setLoadError(true)
          setWorkflow(null)
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

  if (loadError || !workflow) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>The workflow could not be loaded. Please try again.</MessageBarBody>
      </MessageBar>
    )
  }

  return (
    <div>
      <div className={styles.topRow}>
        <Button
          className={styles.back}
          appearance="subtle"
          icon={<ArrowLeftRegular />}
          onClick={() => navigate('/')}
        >
          Back to search
        </Button>
        <div className={styles.headerActions}>
          <ShareMenu title={workflow.name} />
          {canWrite && <>
          <Button
            appearance="secondary"
            icon={<EditRegular />}
            onClick={() => navigate(`/workflows/${workflow.id}/edit`)}
          >
            Edit workflow
          </Button>
          <Button
            appearance="secondary"
            icon={<CopyRegular />}
            onClick={() => navigate(copyAsNewVersionPath('/workflows/new', workflow.id))}
          >
            {COPY_AS_NEW_VERSION_LABEL}
          </Button>
          </>}
        </div>
      </div>

      <div className={styles.header}>
        <Title1 as="h1">{workflow.name}</Title1>
        <Caption1 className={styles.meta}>
          Created by {workflow.createdBy.displayName} · Last edited by{' '}
          {workflow.lastEditedBy.displayName} on {new Date(workflow.lastEditedAt).toLocaleString()}
        </Caption1>
        {canWrite && <RatingWidget kind="workflows" id={workflow.id} />}
      </div>

      <div className={styles.section}>
        <Title3 as="h2" style={{ fontSize: tokens.fontSizeBase500 }}>
          Problem
        </Title3>
        <MarkdownContent content={workflow.problemStatement} />
      </div>

      <div className={styles.section}>
        <Title3 as="h2" style={{ fontSize: tokens.fontSizeBase500 }}>
          Desired outcome
        </Title3>
        <MarkdownContent content={workflow.desiredOutcome} />
      </div>

      <PrimaryMethod method={workflow.method} methodId={workflow.methodId} />

      <div className={styles.section}>
        <Title3
          as="h2"
          style={{ fontSize: tokens.fontSizeBase500, marginBottom: tokens.spacingVerticalM }}
        >
          Steps
        </Title3>
        <div className={styles.steps}>
          {workflow.steps.map((step) => (
            <Card key={step.sequenceOrder} className={styles.step}>
              <div className={styles.stepNumber}>{step.sequenceOrder}</div>
              <div className={styles.stepBody}>
                <div className={styles.stepHeader}>
                  {step.isManualStep ? (
                    <strong>{step.taskName}</strong>
                  ) : step.taskAvailable && step.taskId ? (
                    <RouterLink to={`/tasks/${step.taskId}`} className={styles.taskLink}>
                      <strong>{step.taskName}</strong>
                    </RouterLink>
                  ) : (
                    <strong>{step.taskName}</strong>
                  )}
                  {step.isManualStep ? (
                    <Badge appearance="tint" color="informative">
                      Manual step
                    </Badge>
                  ) : (
                    <Badge appearance="tint">{TASK_TYPE_LABELS[step.taskType ?? '']}</Badge>
                  )}
                  {!step.isManualStep && (
                    <Badge appearance="outline" color="brand">
                      {targetLabel(step.targetId)}
                    </Badge>
                  )}
                </div>
                {!step.isManualStep && !step.taskAvailable && (
                  <span className={styles.unavailable}>
                    <WarningRegular fontSize={16} /> This task is no longer available; showing its
                    last-known name and type.
                  </span>
                )}
                {step.isManualStep && step.manualStepInstructions && (
                  <MarkdownContent content={step.manualStepInstructions} />
                )}
                {step.notes && <Body1>{step.notes}</Body1>}
                {(step.dataSource || step.dataOutput) && (
                  <div className={styles.dataFields}>
                    {step.dataSource && (
                      <Caption1 className={styles.meta}>Input: {step.dataSource}</Caption1>
                    )}
                    {step.dataOutput && (
                      <Caption1 className={styles.meta}>Output: {step.dataOutput}</Caption1>
                    )}
                  </div>
                )}
              </div>
            </Card>
          ))}
        </div>
      </div>

      <div style={{ marginTop: tokens.spacingVerticalXXL }}>
        {canWrite && <CommentsSection kind="workflows" id={workflow.id} />}
      </div>
    </div>
  )
}
