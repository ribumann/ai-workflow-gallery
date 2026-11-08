import { Link as RouterLink } from 'react-router-dom'
import { Body1, Title3 } from '@fluentui/react-components'
import type { MethodRef } from '../services/types'
import { useMethodStyles } from '../pages/Methods/methodUi'

export function PrimaryMethod({
  method,
  methodId,
}: {
  method?: MethodRef | null
  methodId?: string | null
}) {
  const styles = useMethodStyles()
  const id = method?.id ?? methodId
  if (!id) return null
  return (
    <section className={styles.section} aria-label="Primary method">
      <Title3 as="h2">Primary method</Title3>
      <RouterLink className={styles.link} to={`/methods/${encodeURIComponent(id)}`}>
        {method?.title ?? 'View selected method'}
      </RouterLink>
      <Body1 className={styles.measure}>
        {method?.summary ?? 'Method details are unavailable. Open the method to retry.'}
      </Body1>
    </section>
  )
}
