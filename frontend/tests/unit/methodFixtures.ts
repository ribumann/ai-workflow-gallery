import type { MethodDetail, TaskDetail, WorkflowDetail } from '../../src/services/types'

export const method: MethodDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  title: 'Evidence-led synthesis',
  summary: 'Separate source evidence from assumptions before writing a synthesis.',
  approach:
    '## Start with evidence\n\n- Cite sources\n- Explain uncertainty\n\n```text\nsource -> claim\n```',
  createdBy: { id: 'author', displayName: 'Original author' },
  createdAt: '2026-09-10T12:00:00Z',
  lastEditedBy: { id: 'editor', displayName: 'Latest editor' },
  lastEditedAt: '2026-09-11T09:00:00Z',
  version: '22222222-2222-2222-2222-222222222222',
  canDelete: true,
}

export const methodSummary = { ...method, createdByDisplayName: method.createdBy.displayName }

export const task: TaskDetail = {
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Synthesize notes',
  description: 'A reusable prompt.',
  taskType: 'prompt',
  targetIds: ['microsoft.copilot.m365', 'github.copilot.vscode'], format: null, setup: null, prerequisites: null,
  tags: ['synthesis'],
  referenceLink: 'https://example.org/prompt',
  inlineContent: '# Keep this content',
  dataSource: 'Notes',
  dataOutput: 'Summary',
  attachedFile: { fileName: 'prompt.md', sizeBytes: 128, downloadUrl: '/api/tasks/file' },
  createdBy: method.createdBy,
  createdAt: method.createdAt,
  lastEditedBy: method.lastEditedBy,
  lastEditedAt: method.lastEditedAt,
  referencedByWorkflowIds: [],
}

export const workflow: WorkflowDetail = {
  id: '44444444-4444-4444-4444-444444444444',
  name: 'Review and synthesize',
  problemStatement: 'Too many disconnected notes.',
  desiredOutcome: 'An evidence-backed summary.',
  createdBy: method.createdBy,
  createdAt: method.createdAt,
  lastEditedBy: method.lastEditedBy,
  lastEditedAt: method.lastEditedAt,
  steps: [
    {
      sequenceOrder: 1,
      taskId: task.id,
      taskAvailable: true,
      taskName: task.name,
      taskType: 'prompt',
      targetId: 'microsoft.copilot.m365',
      taskTargetIds: task.targetIds,
      notes: 'Keep citations',
      isManualStep: false,
      manualStepInstructions: null,
      dataSource: 'Notes',
      dataOutput: 'Draft',
    },
    {
      sequenceOrder: 2,
      taskId: null,
      taskAvailable: true,
      taskName: 'Review',
      taskType: null,
      targetId: null,
      taskTargetIds: [],
      notes: 'Check facts',
      isManualStep: true,
      manualStepInstructions: 'Ask a colleague to review.',
      dataSource: 'Draft',
      dataOutput: 'Approved summary',
    },
  ],
}
