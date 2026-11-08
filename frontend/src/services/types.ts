// Shared shapes mirroring the API DTOs. Keep these in sync with api/src/Dtos/*.cs.

import type { TaskType } from './catalog'
export { TASK_TYPES, TASK_TYPE_LABELS, type TaskType } from './catalog'

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export interface TaskSummary {
  id: string
  name: string
  description: string
  taskType: TaskType
  targetIds: string[]
  format: string | null
  setup: string | null
  prerequisites: string | null
  tags: string[]
  hasReferenceLink: boolean
  hasInlineContent: boolean
  hasAttachedFile: boolean
  createdByDisplayName: string
  lastEditedAt: string
}

export interface TaskFile {
  fileName: string
  sizeBytes: number
  downloadUrl: string
}

export interface UserRef {
  id: string
  displayName: string
}

export interface TaskDetail {
  id: string
  name: string
  description: string
  taskType: TaskType
  targetIds: string[]
  format: string | null
  setup: string | null
  prerequisites: string | null
  tags: string[]
  referenceLink: string | null
  inlineContent: string | null
  dataSource: string | null
  dataOutput: string | null
  attachedFile: TaskFile | null
  createdBy: UserRef
  createdAt: string
  lastEditedBy: UserRef
  lastEditedAt: string
  referencedByWorkflowIds: string[]
  methodId?: string | null
  method?: MethodRef | null
}

export interface WorkflowSummary {
  id: string
  name: string
  problemStatement: string
  desiredOutcome: string
  stepCount: number
  targetIds: string[]
  createdByDisplayName: string
  lastEditedAt: string
}

export interface WorkflowStepDetail {
  sequenceOrder: number
  taskId: string | null
  taskAvailable: boolean
  taskName: string
  taskType: TaskType | null
  targetId: string | null
  taskTargetIds: string[]
  notes: string | null
  isManualStep: boolean
  manualStepInstructions: string | null
  dataSource: string | null
  dataOutput: string | null
}

export interface WorkflowDetail {
  id: string
  name: string
  problemStatement: string
  desiredOutcome: string
  steps: WorkflowStepDetail[]
  createdBy: UserRef
  createdAt: string
  lastEditedBy: UserRef
  lastEditedAt: string
  methodId?: string | null
  method?: MethodRef | null
}

export interface TaskSearchParams {
  q?: string
  type?: TaskType
  targetId?: string
  page?: number
  pageSize?: number
}

export interface WorkflowSearchParams {
  q?: string
  taskId?: string
  taskType?: TaskType
  targetId?: string
  page?: number
  pageSize?: number
}

/** One step in a create/update `WorkflowSaveRequest`. */
export interface WorkflowStepInput {
  taskId: string | null
  targetId: string | null
  notes: string | null
  isManualStep: boolean
  manualStepTitle: string | null
  manualStepInstructions: string | null
  dataSource: string | null
  dataOutput: string | null
}

/** Request body for `POST /api/workflows` and `PUT /api/workflows/{id}` (US3, T038/T039). */
export interface WorkflowSaveRequest {
  name: string
  problemStatement: string
  desiredOutcome: string
  steps: WorkflowStepInput[]
  /** Optimistic-concurrency guard for updates only (FR-007); omit on create. */
  expectedLastEditedAt?: string
  /** Omit when untouched; null explicitly clears the primary method. */
  methodId?: string | null
}

/**
 * Fields the `TaskEditor` page (T033) collects for both create and edit. `expectedLastEditedAt`
 * is only meaningful on edit (T037's optimistic-concurrency guard) and is omitted on create.
 */
export interface TaskFormInput {
  name: string
  description: string
  taskType: TaskType
  targetIds: string[]
  format: string | null
  setup: string | null
  prerequisites: string | null
  tags: string[]
  referenceLink: string
  inlineContent: string
  dataSource: string
  dataOutput: string
  file: File | null
  expectedLastEditedAt?: string
  /** Omit when untouched; null explicitly clears the primary method. */
  methodId?: string | null
}

/** Body of the `409 Conflict` from `DELETE /api/tasks/{id}` (FR-009). */
export interface WorkflowRef {
  id: string
  name: string
}

export interface DeleteConflict {
  referencedByWorkflows: WorkflowRef[]
}

/** Generic error body shape returned by the task write endpoints on `400`/`401`. */
export interface ErrorResponse {
  message: string
}

/** Request body for `POST /api/feedback`. */
export interface FeedbackRequest {
  message: string
  pageContext?: string
}

export interface FeedbackResponse {
  operationId: string
}

/** Which parent an engagement (rating/comment) subresource hangs off. Mirrors the route segment,
 * e.g. `/api/{kind}/{id}/rating`. */
export type EngagementKind = 'tasks' | 'workflows' | 'methods'

/** Response shape for `GET`/`PUT /api/{tasks|workflows}/{id}/rating` (issue #60). */
export interface RatingSummary {
  helpfulCount: number
  notHelpfulCount: number
  myVote: 1 | -1 | null
}

/** Row shape for `GET /api/{tasks|workflows}/{id}/comments` (issue #60). */
export interface EngagementComment {
  id: string
  body: string
  createdByDisplayName: string
  createdAt: string
}

export interface CommentList {
  items: EngagementComment[]
}

/** Request body for `POST /api/ai/compose`. */
export interface ComposeRequest {
  goal: string
}

/** One proposed step in a `ComposeResponse`, resolved against a real, currently-existing task
 * (FR-004) — never a synthesized/invented id. */
export interface ProposedStep {
  sequenceOrder: number
  taskId: string
  taskName: string
  taskType: TaskType
  targetId: string
  notes: string | null
}

/** A gap in the catalog the AI composer could not fill with a real task (FR-004). */
export interface ComposeGap {
  description: string
  sequenceHint: number
  suggestedTaskName: string | null
  suggestedTaskDescription: string | null
}

/** Response shape for `POST /api/ai/compose` (issue #59). `steps`/`gaps` may both be empty
 * (spec.md Acceptance Scenario 1.3) but the call still succeeds with `200 OK`. */
export interface ComposeResponse {
  suggestedName: string
  problemStatement: string
  suggestedOutcome: string
  steps: ProposedStep[]
  gaps: ComposeGap[]
}

/** Body of the `429 Too Many Requests` from `POST /api/ai/compose` (FR-010). */
export interface ComposeRateLimited {
  retryAfterSeconds: number
}
export type ReportLinkKind = 'Report' | 'AiApp'

export interface ReportLink {
  id: string
  name: string
  description: string
  url: string
  kind: ReportLinkKind
  createdByDisplayName: string
  createdAt: string
  lastEditedByDisplayName: string
  lastEditedAt: string
  version: string
}

export interface ReportLinkInput {
  name: string
  description: string
  url: string
  kind: ReportLinkKind
}

export interface ReportSearchParams {
  q?: string
  kind?: ReportLinkKind
  page?: number
  pageSize?: number
}

export interface MethodRef {
  id: string
  title: string
  summary: string
}

export interface MethodSummary extends MethodRef {
  createdByDisplayName: string
  lastEditedAt: string
}

export interface MethodDetail extends MethodRef {
  approach: string
  createdBy: UserRef
  createdAt: string
  lastEditedBy: UserRef
  lastEditedAt: string
  version: string
  canDelete: boolean
}

export interface MethodInput {
  title: string
  summary: string
  approach: string
}

export interface MethodSearchParams {
  q?: string
  page?: number
  pageSize?: number
}

export interface MethodDeleteConflict {
  message: string
  referencedByTasks: WorkflowRef[]
  referencedByWorkflows: WorkflowRef[]
}
