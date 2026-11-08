import { describe, expect, it } from 'vitest'

import {
  COPY_FROM_PARAM,
  copyAsNewVersionPath,
  nextVersionName,
} from '../../src/utils/versionCopy'

describe('nextVersionName', () => {
  it('appends (v2) to a plain name', () => {
    expect(nextVersionName('Weekly report')).toBe('Weekly report (v2)')
  })

  it('increments an existing version suffix instead of stacking', () => {
    expect(nextVersionName('Weekly report (v2)')).toBe('Weekly report (v3)')
    expect(nextVersionName('Weekly report (v9)')).toBe('Weekly report (v10)')
  })

  it('matches the version suffix case-insensitively and tolerates stray spacing', () => {
    expect(nextVersionName('Weekly report (V2)')).toBe('Weekly report (v3)')
    expect(nextVersionName('  Weekly report  (v2)  ')).toBe('Weekly report (v3)')
  })

  it('ignores version-like text that is not a trailing suffix', () => {
    expect(nextVersionName('Report (v2) draft')).toBe('Report (v2) draft (v2)')
  })

  it('truncates the base name so the result still fits the name limit', () => {
    const longName = 'a'.repeat(200)
    const result = nextVersionName(longName)

    expect(result).toHaveLength(200)
    expect(result.endsWith(' (v2)')).toBe(true)
  })

  it('keeps a name that is only a version suffix usable', () => {
    expect(nextVersionName('(v2)')).toBe('(v2) (v3)')
  })
})

describe('copyAsNewVersionPath', () => {
  it('builds the editor link with the copy source id', () => {
    expect(copyAsNewVersionPath('/tasks/new', 'abc-123')).toBe(
      `/tasks/new?${COPY_FROM_PARAM}=abc-123`,
    )
  })

  it('url-encodes the id', () => {
    expect(copyAsNewVersionPath('/methods/new', 'a b/c')).toBe(
      `/methods/new?${COPY_FROM_PARAM}=a%20b%2Fc`,
    )
  })
})
