import type {
  TaskDetail,
  TaskFormInput,
  TaskSearchParams,
  TaskSummary,
  CommentList,
  ComposeRequest,
  ComposeResponse,
  EngagementComment,
  EngagementKind,
  FeedbackRequest,
  FeedbackResponse,
  MethodDetail,
  MethodInput,
  MethodSearchParams,
  MethodSummary,
  PagedResult,
  ReportLink,
  ReportLinkInput,
  ReportSearchParams,
  RatingSummary,
  WorkflowDetail,
  WorkflowSaveRequest,
  WorkflowSearchParams,
  WorkflowSummary,
} from './types'
import { acquireGalleryToken, SignInRequiredError } from '../auth/entraClient'
import { requestReauthentication } from '../auth/authenticationEvents'
import type { SessionResponse } from '../auth/authContext'
import { publicDemo, scopedPath, selectedCollection } from './deployment'

/**
 * Thrown for any non-2xx API response so callers can branch on status (e.g. 404 vs 409) without
 * re-parsing the response body themselves.
 */
export class ApiError extends Error {
  public readonly status: number
  public readonly body?: unknown

  constructor(status: number, message: string, body?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

async function requestResponse(path: string, init?: RequestInit): Promise<Response> {
  path = scopedPath(path)
  let token: string | null
  try {
    token = publicDemo ? null : await acquireGalleryToken()
  } catch (error) {
    if (error instanceof SignInRequiredError) requestReauthentication()
    throw error
  }
  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...init?.headers,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  })

  if (!response.ok) {
    if (response.status === 401) requestReauthentication()
    let body: unknown
    try {
      body = await response.json()
    } catch {
      body = undefined
    }
    throw new ApiError(response.status, `Request to ${path} failed with ${response.status}`, body)
  }

  return response
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await requestResponse(path, init)
  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

function buildQuery(params: Record<string, string | number | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') {
      search.set(key, String(value))
    }
  }
  const qs = search.toString()
  return qs ? `?${qs}` : ''
}

/**
 * Builds the `multipart/form-data` body shared by `POST`/`PUT /api/tasks`: a JSON `metadata` part
 * plus an optional `file` part.
 */
function buildTaskFormData(input: TaskFormInput): FormData {
  const formData = new FormData()
  formData.set(
    'metadata',
    JSON.stringify({
      name: input.name,
      description: input.description,
      taskType: input.taskType,
      targetIds: input.targetIds,
      format: input.format,
      setup: input.setup,
      prerequisites: input.prerequisites,
      tags: input.tags,
      referenceLink: input.referenceLink || null,
      inlineContent: input.inlineContent || null,
      dataSource: input.dataSource || null,
      dataOutput: input.dataOutput || null,
      expectedLastEditedAt: input.expectedLastEditedAt,
      methodId: input.methodId,
    }),
  )
  if (input.file) {
    formData.set('file', input.file)
  }
  return formData
}

/** Typed client for the collection-scoped catalog endpoints. */
export const apiClient = {
  async getSession(): Promise<SessionResponse> {
    const session = await request<SessionResponse>('/api/session')
    if (typeof session?.isAuthenticated !== 'boolean' || typeof session?.isAuthorized !== 'boolean' ||
        session.displayName !== null && typeof session.displayName !== 'string') {
      throw new Error('The authentication service returned an invalid session response.')
    }
    if (publicDemo && (session.publicDemo !== true ||
        session.collectionCapabilities?.id !== selectedCollection()))
      throw new Error('The API did not confirm the selected public collection capability.')
    return session
  },
  listMethods(params: MethodSearchParams = {}): Promise<PagedResult<MethodSummary>> {
    return request<PagedResult<MethodSummary>>(`/api/methods${buildQuery({ ...params })}`)
  },

  getMethod(id: string): Promise<MethodDetail> {
    return request<MethodDetail>(`/api/methods/${encodeURIComponent(id)}`)
  },

  createMethod(payload: MethodInput): Promise<MethodDetail> {
    return request<MethodDetail>('/api/methods', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  updateMethod(
    id: string,
    payload: MethodInput & { expectedVersion: string },
  ): Promise<MethodDetail> {
    return request<MethodDetail>(`/api/methods/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  deleteMethod(id: string): Promise<void> {
    return request<void>(`/api/methods/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    })
  },

  listReports(params: ReportSearchParams = {}): Promise<PagedResult<ReportLink>> {
    return request<PagedResult<ReportLink>>(`/api/reports${buildQuery({ ...params })}`)
  },

  getReport(id: string): Promise<ReportLink> {
    return request<ReportLink>(`/api/reports/${encodeURIComponent(id)}`)
  },

  createReport(payload: ReportLinkInput): Promise<ReportLink> {
    return request<ReportLink>('/api/reports', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  updateReport(
    id: string,
    payload: ReportLinkInput & { expectedVersion: string },
  ): Promise<ReportLink> {
    return request<ReportLink>(`/api/reports/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  deleteReport(id: string): Promise<void> {
    return request<void>(`/api/reports/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    })
  },

  listTasks(params: TaskSearchParams = {}): Promise<PagedResult<TaskSummary>> {
    const qs = buildQuery({
      q: params.q,
      type: params.type,
      targetId: params.targetId,
      page: params.page,
      pageSize: params.pageSize,
    })
    return request<PagedResult<TaskSummary>>(`/api/tasks${qs}`)
  },

  getTask(id: string): Promise<TaskDetail> {
    return request<TaskDetail>(`/api/tasks/${id}`)
  },

  async getTaskFile(id: string, signal?: AbortSignal): Promise<Blob> {
    const response = await requestResponse(`/api/tasks/${encodeURIComponent(id)}/file`, {
      headers: { Accept: 'application/octet-stream' },
      cache: 'no-store',
      signal,
    })
    return response.blob()
  },

  /** Creates a new task (T030, FR-004). */
  createTask(input: TaskFormInput): Promise<TaskDetail> {
    return request<TaskDetail>('/api/tasks', {
      method: 'POST',
      body: buildTaskFormData(input),
    })
  },

  /** Edits an existing task (T031, FR-005); may throw `ApiError` with `status === 409` on a
   * stale `expectedLastEditedAt` (T037) — `err.body` is the current `TaskDetail`. */
  updateTask(id: string, input: TaskFormInput): Promise<TaskDetail> {
    return request<TaskDetail>(`/api/tasks/${id}`, {
      method: 'PUT',
      body: buildTaskFormData(input),
    })
  },

  /** Deletes an task (T032, FR-005/FR-009). Without `confirm`, throws `ApiError` with
   * `status === 409` and `err.body` as a `DeleteConflict` when the task is still referenced by
   * one or more workflows; retry with `confirm: true` to delete anyway. */
  deleteTask(id: string, confirm = false): Promise<void> {
    const qs = confirm ? '?confirm=true' : ''
    return request<void>(`/api/tasks/${id}${qs}`, { method: 'DELETE' })
  },

  listWorkflows(params: WorkflowSearchParams = {}): Promise<PagedResult<WorkflowSummary>> {
    const qs = buildQuery({
      q: params.q,
      taskId: params.taskId,
      taskType: params.taskType,
      targetId: params.targetId,
      page: params.page,
      pageSize: params.pageSize,
    })
    return request<PagedResult<WorkflowSummary>>(`/api/workflows${qs}`)
  },

  getWorkflow(id: string): Promise<WorkflowDetail> {
    return request<WorkflowDetail>(`/api/workflows/${id}`)
  },

  createWorkflow(payload: WorkflowSaveRequest): Promise<WorkflowDetail> {
    return request<WorkflowDetail>('/api/workflows', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  updateWorkflow(id: string, payload: WorkflowSaveRequest): Promise<WorkflowDetail> {
    return request<WorkflowDetail>(`/api/workflows/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  deleteWorkflow(id: string): Promise<void> {
    return request<void>(`/api/workflows/${id}`, { method: 'DELETE' })
  },

  /** Sends a free-text feedback submission to the maintainer mailbox. */
  submitFeedback(payload: FeedbackRequest): Promise<FeedbackResponse> {
    return request<FeedbackResponse>('/api/feedback', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },

  /** Reads the current 👍/👎 aggregate and this device's vote (issue #60). One Functions-class
   * pair of endpoints backs both tasks and workflows, so `kind` selects the route segment. */
  getRating(kind: EngagementKind, id: string, deviceId: string): Promise<RatingSummary> {
    return request<RatingSummary>(`/api/${kind}/${id}/rating${buildQuery({ deviceId })}`)
  },

  /** Casts, switches, or (if `value` matches the device's existing vote) toggles off a rating. */
  rate(kind: EngagementKind, id: string, deviceId: string, value: 1 | -1): Promise<RatingSummary> {
    return request<RatingSummary>(`/api/${kind}/${id}/rating`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ deviceId, value }),
    })
  },

  listComments(kind: EngagementKind, id: string): Promise<CommentList> {
    return request<CommentList>(`/api/${kind}/${id}/comments`)
  },

  addComment(kind: EngagementKind, id: string, body: string): Promise<EngagementComment> {
    return request<EngagementComment>(`/api/${kind}/${id}/comments`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ body }),
    })
  },

  /** Generates a workflow proposal (task-mapping + gaps) for a user-typed goal. May throw
   * `ApiError` with `status === 429` (`err.body` is a
   * `ComposeRateLimited` with `retryAfterSeconds`) or `status === 502` (AI Foundry outage — caller
   * must show the message but keep the rest of the app usable). */
  composeWorkflow(payload: ComposeRequest): Promise<ComposeResponse> {
    return request<ComposeResponse>('/api/ai/compose', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },
}
