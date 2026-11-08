import { Body1, Dropdown, Option, makeStyles, tokens } from '@fluentui/react-components'
import { COLLECTIONS, canWriteCollection, publicDemo, selectedCollection } from '../services/deployment'
import { useAuth } from '../auth/useAuth'

const useStyles = makeStyles({
  bar: { display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: tokens.spacingHorizontalL, marginBottom: tokens.spacingVerticalL },
})

export function CollectionBar() {
  const styles = useStyles()
  const auth = useAuth()
  const isPublicMode = publicDemo || auth.publicDemo === true
  const canWrite = canWriteCollection(auth.publicDemo, auth.collectionCapabilities)
  const selected = COLLECTIONS.find((collection) => collection.id === selectedCollection())!
  return <section className={styles.bar} aria-label="Selected collection">
    <Dropdown aria-label="Collection" value={selected.label} selectedOptions={[selected.id]}
      onOptionSelect={(_, data) => {
        if (!data.optionValue || !COLLECTIONS.some((collection) => collection.id === data.optionValue)) return
        window.sessionStorage.setItem('gallery.collection', data.optionValue)
        window.location.assign(`/?collectionId=${encodeURIComponent(data.optionValue)}`)
      }}>
      {COLLECTIONS.map((collection) => <Option key={collection.id} value={collection.id}>{collection.label}</Option>)}
    </Dropdown>
    <Body1>
      {selected.id !== 'public-techniques' && selected.description}
      {isPublicMode && selected.id === 'playground' &&
        ' Playground content is shared, public and disposable.'}
      {isPublicMode && selected.id === 'playground' && canWrite &&
        ' Anyone can contribute.'}
      {isPublicMode && selected.id === 'playground' && !canWrite &&
        ' Read-only.'}
      {isPublicMode && selected.id !== 'playground' && ' Published content is read-only.'}
    </Body1>
  </section>
}
