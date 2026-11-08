import { useEffect, useMemo, useState } from 'react'
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom'
import {
  Body1,
  Button,
  Dropdown,
  type DropdownProps,
  Option,
  SearchBox,
  Spinner,
  Tab,
  TabList,
  type SelectTabEvent,
  type SelectTabData,
  Title1,
  Caption1,
  MessageBar,
  MessageBarBody,
  createTableColumn,
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  type TableColumnDefinition,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components'
import { AddRegular, DocumentSearchRegular, SparkleRegular } from '@fluentui/react-icons'
import { MarkdownExcerpt } from '../../components/MarkdownExcerpt'
import { AiComposerPanel } from '../../components/AiComposerPanel'
import { apiClient } from '../../services/apiClient'
import { TargetPicker } from '../../components/TargetPicker'
import { targetLabel } from '../../services/catalog'
import { useAuth } from '../../auth/useAuth'
import {
  canWriteCollection,
  publicDemo as publicBuild,
  selectedCollection,
} from '../../services/deployment'
import {
  TASK_TYPES,
  TASK_TYPE_LABELS,
  type MethodSummary,
  type TaskSummary,
  type TaskType,
  type WorkflowSummary,
} from '../../services/types'

const useStyles = makeStyles({
  header: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
    marginBottom: tokens.spacingVerticalL,
  },
  headerText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  headerActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  tabRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
  controls: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalM,
    marginBottom: tokens.spacingVerticalL,
  },
  searchBox: {
    minWidth: 0,
    width: 'min(20rem, 100%)',
    flexGrow: 1,
  },
  filter: {
    minWidth: '11rem',
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalM,
    ...shorthands.padding(tokens.spacingVerticalXXL, 0),
    color: tokens.colorNeutralForeground2,
    textAlign: 'center',
  },
  loading: {
    display: 'flex',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalXXL, 0),
  },
  clickableRow: {
    cursor: 'pointer',
  },
  nameLink: {
    color: 'inherit',
    textDecorationLine: 'none',
    ':hover': { textDecorationLine: 'underline' },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: '2px',
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
  pagination: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalL,
  },
  pageActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  nameCellDescription: {
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground2,
  },
  nameCellMetadata: {
    display: 'block',
    marginTop: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground2,
    overflowWrap: 'anywhere',
  },
  gridContainer: {
    minWidth: 0,
  },
})

type Tab = 'tasks' | 'workflows' | 'methods'

const TAB_VALUES: Tab[] = ['tasks', 'workflows', 'methods']
const PAGE_SIZE = 25
const CREATE_ACTIONS: Record<Tab, { label: string; path: string }> = {
  tasks: { label: 'New task', path: '/tasks/new' },
  workflows: { label: 'New workflow', path: '/workflows/new' },
  methods: { label: 'New method', path: '/methods/new' },
}

function isTab(value: unknown): value is Tab {
  return typeof value === 'string' && (TAB_VALUES as string[]).includes(value)
}

/** Row clicks are only a convenience, so the name itself is a real link: that keeps every listing
 * reachable by keyboard and screen reader, and keeps middle-click/open-in-new-tab working. */
function NameCell({
  to,
  title,
  description,
  metadata,
  className,
  descriptionClassName,
  metadataClassName,
}: {
  to: string
  title: string
  description: string
  metadata: string
  className: string
  descriptionClassName: string
  metadataClassName: string
}) {
  return (
    <div>
      <Body1>
        <RouterLink className={className} to={to} onClick={(event) => event.stopPropagation()}>
          <strong>{title}</strong>
        </RouterLink>
      </Body1>
      <MarkdownExcerpt content={description} lines={3} className={descriptionClassName} />
      <Caption1 className={metadataClassName}>{metadata}</Caption1>
    </div>
  )
}

/** Search/Home page (T024): keyword search across tasks, workflows and AI methods, with
 * type/tool-surface filters for the catalog tabs and empty-state guidance for an empty catalog or
 * a no-match search (Edge Cases). */
export function SearchPage() {
  const styles = useStyles()
  const navigate = useNavigate()
  const auth = useAuth()
  const publicDemo = publicBuild || auth.publicDemo === true
  const canWrite = canWriteCollection(auth.publicDemo, auth.collectionCapabilities)
  const [isComposerOpen, setIsComposerOpen] = useState(false)
  const [checkingComposer, setCheckingComposer] = useState(false)
  const [composerRefreshError, setComposerRefreshError] = useState<string | null>(null)

  const [searchParams, setSearchParams] = useSearchParams()
  const tabParam = searchParams.get('tab')
  const tab: Tab = isTab(tabParam) ? tabParam : 'tasks'
  const query = searchParams.get('q') ?? ''
  const rawType = searchParams.get('type') ?? ''
  const typeFilter = TASK_TYPES.includes(rawType as TaskType) ? (rawType as TaskType) : ''
  const targetFilter = searchParams.get('targetId') ?? ''
  const rawPage = Number(searchParams.get('page') ?? 1)
  const page = Number.isSafeInteger(rawPage) && rawPage > 0 && rawPage <= 1000000 ? rawPage : 1
  const requestKey = searchParams.toString()
  const [settledKey, setSettledKey] = useState<string | null>(null)
  const [loadError, setLoadError] = useState(false)

  const isLoading = settledKey !== requestKey
  const [tasks, setTasks] = useState<TaskSummary[] | null>(null)
  const [workflows, setWorkflows] = useState<WorkflowSummary[] | null>(null)
  const [methods, setMethods] = useState<MethodSummary[] | null>(null)
  const [catalogHasAnyTasks, setCatalogHasAnyTasks] = useState(true)
  const [totalCount, setTotalCount] = useState(0)
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))
  const showComposerControls = true

  // Methods are described in prose only, so the task-shaped filters never apply to them.
  const supportsCatalogFilters = tab !== 'methods'

  const shownCount =
    tab === 'tasks'
      ? (tasks?.length ?? 0)
      : tab === 'workflows'
        ? (workflows?.length ?? 0)
        : (methods?.length ?? 0)

  function changeParams(name: string, value: string, replace = false) {
    const next = new URLSearchParams(searchParams)
    if (value) next.set(name, value)
    else next.delete(name)
    if (name !== 'page') next.delete('page')
    if (next.toString() !== requestKey) setSearchParams(next, { replace })
  }

  async function recheckComposer() {
    if (!auth.refreshPublicSession || checkingComposer) return
    setCheckingComposer(true)
    setComposerRefreshError(null)
    try {
      await auth.refreshPublicSession()
    } catch {
      setComposerRefreshError('Composer readiness could not be checked. Try again later.')
    } finally {
      setCheckingComposer(false)
    }
  }

  useEffect(() => {
    let cancelled = false

    const load = async () => {
      try {
        const common = { q: query || undefined, page, pageSize: PAGE_SIZE }
        const result =
          tab === 'tasks'
            ? await apiClient
                .listTasks({
                  ...common,
                  type: typeFilter || undefined,
                  targetId: targetFilter || undefined,
                })
                .then((value) => ({ ...value, kind: 'tasks' as const }))
            : tab === 'workflows'
              ? await apiClient
                  .listWorkflows({
                    ...common,
                    taskType: typeFilter || undefined,
                    targetId: targetFilter || undefined,
                  })
                  .then((value) => ({ ...value, kind: 'workflows' as const }))
              : await apiClient
                  .listMethods(common)
                  .then((value) => ({ ...value, kind: 'methods' as const }))
        if (cancelled) return
        const lastPage = Math.max(1, Math.ceil(result.totalCount / PAGE_SIZE))
        if (page > lastPage) {
          const next = new URLSearchParams(searchParams)
          if (lastPage === 1) next.delete('page')
          else next.set('page', String(lastPage))
          setSearchParams(next, { replace: true })
          return
        }
        setLoadError(false)
        setTotalCount(result.totalCount)
        if (result.kind === 'tasks') {
          setTasks(result.items)
          if (!query && !typeFilter && !targetFilter) {
            setCatalogHasAnyTasks(result.totalCount > 0)
          }
        } else if (result.kind === 'workflows') {
          setWorkflows(result.items)
        } else {
          setMethods(result.items)
        }
        setSettledKey(requestKey)
      } catch {
        if (!cancelled) {
          setLoadError(true)
          setTotalCount(0)
          if (tab === 'tasks') setTasks([])
          else if (tab === 'workflows') setWorkflows([])
          else setMethods([])
          setSettledKey(requestKey)
        }
      }
    }

    const handle = window.setTimeout(() => void load(), query ? 300 : 0)

    return () => {
      cancelled = true
      window.clearTimeout(handle)
    }
  }, [tab, query, typeFilter, targetFilter, page, requestKey, searchParams, setSearchParams])

  const taskColumns = useMemo<TableColumnDefinition<TaskSummary>[]>(
    () => [
      createTableColumn<TaskSummary>({
        columnId: 'name',
        renderHeaderCell: () => 'Name',
        renderCell: (item) => (
          <NameCell
            to={`/tasks/${encodeURIComponent(item.id)}`}
            title={item.name}
            description={item.description}
            metadata={[
              TASK_TYPE_LABELS[item.taskType],
              ...item.targetIds.map(targetLabel),
              `by ${item.createdByDisplayName}`,
            ].join(' · ')}
            className={styles.nameLink}
            descriptionClassName={styles.nameCellDescription}
            metadataClassName={styles.nameCellMetadata}
          />
        ),
      }),
    ],
    [styles.nameLink, styles.nameCellDescription, styles.nameCellMetadata],
  )

  const workflowColumns = useMemo<TableColumnDefinition<WorkflowSummary>[]>(
    () => [
      createTableColumn<WorkflowSummary>({
        columnId: 'name',
        renderHeaderCell: () => 'Name',
        renderCell: (item) => (
          <NameCell
            to={`/workflows/${encodeURIComponent(item.id)}`}
            title={item.name}
            description={item.problemStatement}
            metadata={[
              `${item.stepCount} step${item.stepCount === 1 ? '' : 's'}`,
              ...item.targetIds.map(targetLabel),
              `by ${item.createdByDisplayName}`,
            ].join(' · ')}
            className={styles.nameLink}
            descriptionClassName={styles.nameCellDescription}
            metadataClassName={styles.nameCellMetadata}
          />
        ),
      }),
    ],
    [styles.nameLink, styles.nameCellDescription, styles.nameCellMetadata],
  )

  const methodColumns = useMemo<TableColumnDefinition<MethodSummary>[]>(
    () => [
      createTableColumn<MethodSummary>({
        columnId: 'name',
        renderHeaderCell: () => 'Name',
        renderCell: (item) => (
          <NameCell
            to={`/methods/${encodeURIComponent(item.id)}`}
            title={item.title}
            description={item.summary}
            metadata={`Updated ${new Date(item.lastEditedAt).toLocaleDateString()} · by ${item.createdByDisplayName}`}
            className={styles.nameLink}
            descriptionClassName={styles.nameCellDescription}
            metadataClassName={styles.nameCellMetadata}
          />
        ),
      }),
    ],
    [styles.nameLink, styles.nameCellDescription, styles.nameCellMetadata],
  )

  const onTabSelect = (_: SelectTabEvent, data: SelectTabData) => {
    if (isTab(data.value)) {
      changeParams('tab', data.value)
    }
  }

  const typeOptionText = (value: string) =>
    value ? TASK_TYPE_LABELS[value as TaskType] : 'All types'

  const onTypeChange: DropdownProps['onOptionSelect'] = (_, data) =>
    changeParams('type', data.optionValue || '')

  const items = tab === 'tasks' ? tasks : tab === 'workflows' ? workflows : methods
  const createAction = CREATE_ACTIONS[tab]
  const pageStart = totalCount === 0 ? 0 : (page - 1) * PAGE_SIZE + 1
  const pageEnd = totalCount === 0 ? 0 : (page - 1) * PAGE_SIZE + shownCount
  const pageRange = `${pageStart}\u2013${pageEnd}`
  const hasSearchCriteria = Boolean(
    query || (supportsCatalogFilters && (typeFilter || targetFilter)),
  )

  return (
    <div>
      <div className={styles.header}>
        <div className={styles.headerText}>
          <Title1 as="h1">Discover tasks, workflows &amp; methods</Title1>
        </div>
        {(canWrite || showComposerControls) && (
          <div className={styles.headerActions}>
            {canWrite && (
              <Button
                appearance="primary"
                icon={<AddRegular />}
                onClick={() => navigate(createAction.path)}
              >
                {createAction.label}
              </Button>
            )}
            {showComposerControls &&
              (publicDemo ? (
                <>
                  <Button
                    appearance="subtle"
                    icon={<SparkleRegular />}
                    disabled={!auth.composerEnabled}
                    onClick={() => setIsComposerOpen(true)}
                  >
                    {auth.composerEnabled
                      ? 'Workflow composer'
                      : auth.composerState === 'quota_exhausted'
                        ? 'Workflow composer limit reached'
                        : auth.composerState === 'busy'
                          ? 'Workflow composer busy'
                          : 'Workflow composer unavailable'}
                  </Button>
                  {!auth.composerEnabled && auth.refreshPublicSession && (
                    <Button
                      appearance="subtle"
                      disabled={checkingComposer}
                      onClick={() => void recheckComposer()}
                    >
                      {checkingComposer
                        ? 'Checking workflow composer...'
                        : 'Recheck workflow composer'}
                    </Button>
                  )}
                </>
              ) : (
                <Button
                  appearance="subtle"
                  icon={<SparkleRegular />}
                  onClick={() => setIsComposerOpen(true)}
                >
                  Workflow composer
                </Button>
              ))}
            {showComposerControls && composerRefreshError && (
              <span role="status">{composerRefreshError}</span>
            )}
          </div>
        )}
      </div>

      <div className={styles.tabRow}>
        <TabList selectedValue={tab} onTabSelect={onTabSelect}>
          <Tab value="tasks">Tasks</Tab>
          <Tab value="workflows">Workflows</Tab>
          <Tab value="methods">Methods</Tab>
        </TabList>
      </div>

      <div className={styles.controls}>
        <SearchBox
          className={styles.searchBox}
          placeholder="Search by keyword..."
          value={query}
          aria-label="Search catalog"
          onChange={(_, data) => changeParams('q', data.value, true)}
        />
        <Dropdown
          aria-label="Task type"
          className={styles.filter}
          placeholder="All types"
          value={typeOptionText(typeFilter)}
          selectedOptions={typeFilter ? [typeFilter] : []}
          onOptionSelect={onTypeChange}
          disabled={!supportsCatalogFilters}
        >
          <Option value="">All types</Option>
          {TASK_TYPES.map((t) => (
            <Option key={t} value={t}>
              {TASK_TYPE_LABELS[t]}
            </Option>
          ))}
        </Dropdown>
        <TargetPicker
          value={targetFilter ? [targetFilter] : []}
          kind={typeFilter || undefined}
          label="Search targets"
          disabled={!supportsCatalogFilters}
          onChange={(ids) => changeParams('targetId', ids[0] ?? '')}
        />
        {targetFilter && (
          <Button appearance="subtle" onClick={() => changeParams('targetId', '')}>
            Clear target
          </Button>
        )}
      </div>

      {isLoading && (
        <div className={styles.loading}>
          <Spinner label="Searching..." />
        </div>
      )}

      {!isLoading && loadError && (
        <MessageBar intent="error">
          <MessageBarBody>
            Could not load the catalog. Check your connection and reload this page.
          </MessageBarBody>
        </MessageBar>
      )}

      {!isLoading && !loadError && items && items.length === 0 && (
        <div className={styles.emptyState}>
          <DocumentSearchRegular fontSize={40} />
          {tab === 'tasks' && !catalogHasAnyTasks && !hasSearchCriteria ? (
            <>
              <Title1 as="h2" style={{ fontSize: tokens.fontSizeBase500 }}>
                The catalog is empty
              </Title1>
              <Body1>
                {publicDemo ? (
                  selectedCollection() === 'playground' ? (
                    canWrite ? (
                      'The Playground is empty. Choose “New task” to contribute the first shared example.'
                    ) : (
                      'The Playground is empty and currently read-only.'
                    )
                  ) : (
                    'No published tasks are available in this collection. Choose another collection to browse.'
                  )
                ) : (
                  <>
                    No tasks have been added yet. Choose &ldquo;New task&rdquo; to contribute the
                    first one.
                  </>
                )}
              </Body1>
            </>
          ) : (
            <>
              <Title1 as="h2" style={{ fontSize: tokens.fontSizeBase500 }}>
                No matches found
              </Title1>
              <Body1>
                {supportsCatalogFilters
                  ? 'Try a different keyword, or clear the type/tool-surface filters.'
                  : 'Try a different keyword.'}
              </Body1>
            </>
          )}
        </div>
      )}

      {!isLoading && tab === 'tasks' && tasks && tasks.length > 0 && (
        <div className={styles.gridContainer}>
          <DataGrid items={tasks} columns={taskColumns} getRowId={(item) => item.id}>
            <DataGridHeader>
              <DataGridRow>
                {({ renderHeaderCell }) => (
                  <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                )}
              </DataGridRow>
            </DataGridHeader>
            <DataGridBody<TaskSummary>>
              {({ item, rowId }) => (
                <DataGridRow<TaskSummary>
                  key={rowId}
                  className={styles.clickableRow}
                  onClick={() => navigate(`/tasks/${item.id}`)}
                >
                  {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
                </DataGridRow>
              )}
            </DataGridBody>
          </DataGrid>
        </div>
      )}

      {!isLoading && tab === 'workflows' && workflows && workflows.length > 0 && (
        <div className={styles.gridContainer}>
          <DataGrid items={workflows} columns={workflowColumns} getRowId={(item) => item.id}>
            <DataGridHeader>
              <DataGridRow>
                {({ renderHeaderCell }) => (
                  <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                )}
              </DataGridRow>
            </DataGridHeader>
            <DataGridBody<WorkflowSummary>>
              {({ item, rowId }) => (
                <DataGridRow<WorkflowSummary>
                  key={rowId}
                  className={styles.clickableRow}
                  onClick={() => navigate(`/workflows/${item.id}`)}
                >
                  {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
                </DataGridRow>
              )}
            </DataGridBody>
          </DataGrid>
        </div>
      )}

      {!isLoading && tab === 'methods' && methods && methods.length > 0 && (
        <div className={styles.gridContainer}>
          <DataGrid items={methods} columns={methodColumns} getRowId={(item) => item.id}>
            <DataGridHeader>
              <DataGridRow>
                {({ renderHeaderCell }) => (
                  <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                )}
              </DataGridRow>
            </DataGridHeader>
            <DataGridBody<MethodSummary>>
              {({ item, rowId }) => (
                <DataGridRow<MethodSummary>
                  key={rowId}
                  className={styles.clickableRow}
                  onClick={() => navigate(`/methods/${item.id}`)}
                >
                  {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
                </DataGridRow>
              )}
            </DataGridBody>
          </DataGrid>
        </div>
      )}

      {!isLoading && !loadError && (
        <nav className={styles.pagination} aria-label="Catalog pagination">
          <Caption1 aria-live="polite">
            {pageRange} of {totalCount} items
          </Caption1>
          <div className={styles.pageActions}>
            <Button disabled={page <= 1} onClick={() => changeParams('page', String(page - 1))}>
              Previous
            </Button>
            <Caption1>
              Page {page} of {pageCount}
            </Caption1>
            <Button
              disabled={page >= pageCount}
              onClick={() => changeParams('page', String(page + 1))}
            >
              Next
            </Button>
          </div>
        </nav>
      )}
      {showComposerControls && (!publicDemo || auth.composerEnabled) && (
        <AiComposerPanel
          key={publicDemo ? selectedCollection() : 'private'}
          open={isComposerOpen}
          onOpenChange={setIsComposerOpen}
          readOnly={publicDemo}
          canWrite={!publicDemo && canWrite}
          maxGoalLength={publicDemo ? auth.composerMaxGoalCharacters : undefined}
        />
      )}
    </div>
  )
}
