/**
 * Interim "create new version as copy" support, shared by every artifact editor (tasks,
 * workflows, methods, report/AI-dashboard links).
 *
 * This is deliberately a *temporary* stand-in for real versioning, which needs to be specced
 * properly first: a copy is just a new, independent item opened as an unsaved draft via
 * `/<kind>/new?copyFrom=<id>`. Nothing is written until the contributor saves, the source item is
 * never modified, and no version lineage is recorded anywhere — only the name placeholder hints at
 * the relationship.
 */

/** Query-string parameter carrying the id of the item an editor should prefill a copy from. */
export const COPY_FROM_PARAM = 'copyFrom'

/** Button/label text, kept in one place so all four artifact types read identically. */
export const COPY_AS_NEW_VERSION_LABEL = 'Create new version as copy'

/** Shown in the editor while a copy draft is open, so nobody thinks they are editing the source. */
export const COPY_AS_NEW_VERSION_HINT =
  'This is an unsaved copy. Saving creates a separate new item — the original stays unchanged. Adjust the version in the name as you like.'

/** Name limit shared by task, workflow, method and report names (see each editor's Input). */
const NAME_MAX_LENGTH = 200

const VERSION_SUFFIX = /\s*\(v(\d+)\)\s*$/i

/**
 * Suggests the copy's name: `"Weekly report"` → `"Weekly report (v2)"`, and an existing version
 * suffix is incremented rather than stacked (`"Weekly report (v2)"` → `"Weekly report (v3)"`).
 * The base name is truncated when needed so the result still fits the stored name limit.
 */
export function nextVersionName(name: string, maxLength: number = NAME_MAX_LENGTH): string {
  const trimmed = name.trim()
  const match = VERSION_SUFFIX.exec(trimmed)
  const base = (match ? trimmed.slice(0, match.index).trim() : trimmed) || trimmed
  const suffix = ` (v${match ? Number(match[1]) + 1 : 2})`

  if (base.length + suffix.length <= maxLength) return base + suffix
  return base.slice(0, Math.max(0, maxLength - suffix.length)).trimEnd() + suffix
}

/** Builds the editor link for a copy, e.g. `copyAsNewVersionPath('/tasks/new', id)`. */
export function copyAsNewVersionPath(newItemPath: string, id: string): string {
  return `${newItemPath}?${COPY_FROM_PARAM}=${encodeURIComponent(id)}`
}
