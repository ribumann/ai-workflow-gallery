import { useState } from 'react'
import { Combobox, Option } from '@fluentui/react-components'
import { targetLabel, targetsFor } from '../services/catalog'

export function TargetPicker({
  value, onChange, kind, allowedIds, multiple = false, disabled = false, label = 'Works in',
}: {
  value: string[]
  onChange: (ids: string[]) => void
  kind?: string
  allowedIds?: string[]
  multiple?: boolean
  disabled?: boolean
  label?: string
}) {
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const targets = targetsFor(kind).filter((target) =>
    (!allowedIds || allowedIds.includes(target.id)) &&
    [target.label, target.id, target.provider, target.product, target.host, ...target.aliases]
      .some((text) => text.toLowerCase().includes(query.toLowerCase())),
  )
  return (
    <Combobox
      aria-label={label}
      multiselect={multiple}
      disabled={disabled}
      open={open}
      onOpenChange={(_, data) => setOpen(data.open)}
      placeholder={multiple ? 'Search and choose targets' : 'Search targets'}
      value={query || value.map(targetLabel).join(', ')}
      selectedOptions={value}
      onChange={(event) => { setQuery(event.target.value); setOpen(true) }}
      onOptionSelect={(_, data) => {
        onChange(data.selectedOptions)
        setQuery('')
      }}
      onBlur={() => setQuery('')}
    >
      {targets.map((target) => (
        <Option key={target.id} value={target.id} text={target.label}>{target.label}</Option>
      ))}
      {!targets.length && <Option disabled>No documented targets match</Option>}
    </Combobox>
  )
}
