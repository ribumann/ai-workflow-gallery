import registry from '../../../catalog/registry.json'

export const catalog = registry
export type CatalogTarget = (typeof registry.targets)[number]
export type TaskType = string
export const TASK_TYPES = registry.kinds.map((kind) => kind.id)
export const TASK_TYPE_LABELS: Record<string, string> = Object.fromEntries(
  registry.kinds.map((kind) => [kind.id, kind.label]),
)

export function targetLabel(id: string | null): string {
  return registry.targets.find((target) => target.id === id)?.label ?? id ?? 'No product target'
}

export function targetsFor(kind?: string): CatalogTarget[] {
  return registry.targets.filter((target) =>
    target.profiles.some((profile) => !kind || profile.kind === kind),
  )
}

export function formatsFor(kind: string, targetIds: string[]) {
  if (!targetIds.length) return []
  return registry.formats.filter((format) => targetIds.every((id) =>
    registry.targets.find((target) => target.id === id)?.profiles.some((profile) =>
      profile.kind === kind && profile.formats.includes(format.id),
    ),
  ))
}

export function validateClassification(kind: string, targetIds: string[], format: string | null): string | null {
  if (!TASK_TYPES.includes(kind)) return 'Choose a known type.'
  if (!targetIds.length || targetIds.length > 16 || new Set(targetIds).size !== targetIds.length)
    return 'Choose between 1 and 16 distinct supported targets.'
  if (targetIds.some((id) => !targetsFor(kind).some((target) => target.id === id)))
    return 'Each target must document support for this type. Unknown is not supported.'
  if (format && !formatsFor(kind, targetIds).some((candidate) => candidate.id === format))
    return 'Choose a format documented for every selected target, or leave it unspecified.'
  return null
}
