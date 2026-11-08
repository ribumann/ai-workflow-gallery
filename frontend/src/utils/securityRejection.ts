/**
 * Recognizes server-side rejections that specifically indicate a malicious-looking input was
 * caught by validation (unsafe reference-link URI scheme, path-traversal file name) rather than a
 * routine validation error (missing field, oversized file, disallowed extension). Used to trigger
 * the "well tried, caught you!" easter egg — see components/CaughtYouToast.tsx — without falsely
 * celebrating ordinary mistakes.
 */
export function isSecurityRejectionMessage(message: string | null | undefined): boolean {
  if (!message) {
    return false
  }

  const normalized = message.toLowerCase()
  return (
    normalized.includes('must be a valid http') || // unsafe referenceLink URI scheme (Tasks.cs)
    normalized.includes('must not contain a path') // path-traversal file name (FileValidationService.cs)
  )
}
