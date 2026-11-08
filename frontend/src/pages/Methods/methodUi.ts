import { makeStyles, shorthands, tokens } from '@fluentui/react-components'
import { ApiError } from '../../services/apiClient'
import type { MethodDeleteConflict, MethodInput, WorkflowRef } from '../../services/types'

export const METHOD_LIMITS = { title: 200, summary: 2000, approach: 100000 } as const

/** Methods are browsed from the unified discovery page rather than a separate directory,
 * so every "back to the list" path points at that tab. */
export const METHODS_BROWSE_PATH = '/?tab=methods'

export function validateMethod(input: MethodInput): Partial<Record<keyof MethodInput, string>> {
  const errors: Partial<Record<keyof MethodInput, string>> = {}
  const labels = { title: 'Title', summary: 'Short summary', approach: 'Written approach' }
  for (const field of ['title', 'summary', 'approach'] as const) {
    if (!input[field].trim()) errors[field] = `${labels[field]} is required.`
    else if (input[field].trim().length > METHOD_LIMITS[field])
      errors[field] =
        `${labels[field]} must be ${METHOD_LIMITS[field].toLocaleString()} characters or fewer.`
  }
  return errors
}

export function methodError(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    if (error.status === 404) return 'This method is no longer available. It may have been deleted.'
    if (error.status === 403) return 'You do not have permission to make this change.'
    if (error.status === 409)
      return 'This method changed or is still in use. Your content has not been removed.'
    if (
      error.body &&
      typeof error.body === 'object' &&
      'message' in error.body &&
      typeof error.body.message === 'string'
    )
      return error.body.message
  }
  return fallback
}

function isReferenceList(value: unknown): value is WorkflowRef[] {
  return (
    Array.isArray(value) &&
    value.every(
      (ref: unknown) =>
        ref !== null &&
        typeof ref === 'object' &&
        'id' in ref &&
        typeof ref.id === 'string' &&
        'name' in ref &&
        typeof ref.name === 'string',
    )
  )
}

export function methodReferences(error: unknown): MethodDeleteConflict | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null
  const body = error.body
  if (
    !body ||
    typeof body !== 'object' ||
    !('message' in body) ||
    typeof body.message !== 'string' ||
    !('referencedByTasks' in body) ||
    !isReferenceList(body.referencedByTasks) ||
    !('referencedByWorkflows' in body) ||
    !isReferenceList(body.referencedByWorkflows)
  )
    return null
  return {
    message: body.message,
    referencedByTasks: body.referencedByTasks,
    referencedByWorkflows: body.referencedByWorkflows,
  }
}

export const useMethodStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, minWidth: 0 },
  header: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalL,
  },
  intro: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, minWidth: 0 },
  heading: { overflowWrap: 'anywhere' },
  link: {
    color: tokens.colorBrandForegroundLink,
    overflowWrap: 'anywhere',
    ':hover': { color: tokens.colorBrandForegroundLinkHover },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: '2px',
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
  measure: { maxWidth: '75ch', overflowWrap: 'anywhere' },
  muted: { color: tokens.colorNeutralForeground2 },
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    maxWidth: '48rem',
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalM,
  },
  list: { display: 'flex', flexDirection: 'column', listStyleType: 'none', margin: 0, padding: 0 },
  row: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalM,
    ...shorthands.padding(tokens.spacingVerticalL, 0),
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
  },
  rowText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    flex: '1 1 15rem',
    minWidth: 0,
    overflowWrap: 'anywhere',
  },
  search: { width: '100%', maxWidth: '36rem' },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalL,
    overflowWrap: 'anywhere',
  },
  picker: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    ...shorthands.padding(tokens.spacingVerticalM),
  },
})
