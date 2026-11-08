import { describe, expect, it } from 'vitest'
import { isSecurityRejectionMessage } from '../../src/utils/securityRejection'

// Guards the "well tried, caught you!" easter egg trigger (see CaughtYouToast.test.tsx and
// TaskEditorPage.tsx): it must fire only for the two known attack-pattern rejections from the
// API (unsafe reference-link scheme, path-traversal file name), never for ordinary validation
// mistakes, so legitimate users aren't mocked for benign errors.
describe('isSecurityRejectionMessage', () => {
  it('matches the unsafe reference-link scheme rejection', () => {
    expect(isSecurityRejectionMessage('Reference link must be a valid http:// or https:// URL.')).toBe(
      true,
    )
  })

  it('matches the path-traversal file name rejection', () => {
    expect(isSecurityRejectionMessage('The uploaded file name must not contain a path.')).toBe(true)
  })

  it('is case-insensitive', () => {
    expect(isSecurityRejectionMessage('REFERENCE LINK MUST BE A VALID HTTP:// OR HTTPS:// URL.')).toBe(
      true,
    )
  })

  it('does not match ordinary validation errors', () => {
    expect(isSecurityRejectionMessage('Name is required.')).toBe(false)
    expect(isSecurityRejectionMessage('The uploaded file exceeds the 5 MB size limit.')).toBe(false)
    expect(
      isSecurityRejectionMessage('This task could not be saved. Check the fields above.'),
    ).toBe(false)
  })

  it('handles null and undefined safely', () => {
    expect(isSecurityRejectionMessage(null)).toBe(false)
    expect(isSecurityRejectionMessage(undefined)).toBe(false)
    expect(isSecurityRejectionMessage('')).toBe(false)
  })
})
