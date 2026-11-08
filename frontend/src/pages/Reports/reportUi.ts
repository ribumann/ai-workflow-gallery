import { makeStyles, shorthands, tokens } from '@fluentui/react-components'
import { ApiError } from '../../services/apiClient'

export function reportError(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    if (error.status === 401 || error.status === 403)
      return 'Your session may have expired. Sign in again, then retry.'
    if (error.status === 404)
      return 'This link no longer exists. It may have been deleted by another user.'
    if (error.status === 409)
      return 'Someone else changed this link. Your draft is still here. Reload the latest version before editing again.'
    if (
      error.body &&
      typeof error.body === 'object' &&
      'error' in error.body &&
      typeof error.body.error === 'string'
    ) {
      return error.body.error
    }
  }
  return fallback
}

export const useReportStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, minWidth: 0 },
  header: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalL,
    flexWrap: 'wrap',
    '@media (max-width: 599px)': { flexDirection: 'column' },
  },
  heading: { margin: 0, overflowWrap: 'anywhere' },
  intro: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    minWidth: 0,
    flex: 1,
  },
  description: {
    whiteSpace: 'pre-wrap',
    overflowWrap: 'anywhere',
    maxWidth: '75ch',
    color: tokens.colorNeutralForeground2,
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalM,
    '@media (max-width: 599px)': { flexDirection: 'column', alignItems: 'stretch' },
  },
  // Matches the discovery page tab row so both directories read as one product.
  tabRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    overflowX: 'auto',
  },
  search: {
    flex: 1,
    minWidth: 0,
    width: '100%',
    maxWidth: '36rem',
    '@media (max-width: 599px)': { flex: 'none', maxWidth: '100%' },
  },
  list: { listStyleType: 'none', margin: 0, padding: 0 },
  row: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalXL,
    ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalL),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
    '@media (max-width: 1199px)': { flexDirection: 'column', gap: tokens.spacingVerticalM },
  },
  title: {
    color: tokens.colorBrandForegroundLink,
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    overflowWrap: 'anywhere',
  },
  metadata: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
    overflowWrap: 'anywhere',
  },
  empty: {
    ...shorthands.padding(tokens.spacingVerticalXXL, 0),
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    alignItems: 'flex-start',
  },
  pagination: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    maxWidth: '48rem',
    width: '100%',
  },
  frame: {
    width: '100%',
    minHeight: '65vh',
    flex: 1,
    border: 'none',
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
  },
})
