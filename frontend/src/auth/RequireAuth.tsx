import type { ReactNode } from 'react'
import { Button, Spinner, Title2, Body1, makeStyles, tokens } from '@fluentui/react-components'
import { useAuth } from './useAuth'
import { publicDemo } from '../services/deployment'

const useStyles = makeStyles({
  screen: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalL,
    minHeight: '70vh',
    textAlign: 'center',
    padding: tokens.spacingHorizontalXXL,
  },
  message: {
    maxWidth: '32rem',
    color: tokens.colorNeutralForeground2,
  },
})

/** The SPA shell is public; no catalog content renders before server authorization. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const auth = useAuth()
  const styles = useStyles()

  if (auth.error) {
    return <div className={styles.screen} role="alert">
      <Title2>Sign-in unavailable</Title2>
      <Body1 className={styles.message}>{auth.error}</Body1>
    </div>
  }

  if (auth.isLoading) {
    return (
      <div className={styles.screen}>
        <Spinner label="Checking your sign-in..." />
      </div>
    )
  }

  if (publicDemo && auth.publicDemo === true) return <>{children}</>

  if (!auth.isAuthenticated) {
    return (
      <div className={styles.screen}>
        <Title2>Sign in to the gallery</Title2>
        <Body1 className={styles.message}>Use an account from an authorized organization.</Body1>
        <Button appearance="primary" onClick={() => void auth.signIn()}>Sign in</Button>
      </div>
    )
  }

  if (!auth.isAuthorized) {
    return (
      <div className={styles.screen}>
        <Title2>This account is not authorized</Title2>
        <Body1 className={styles.message}>
          This account&rsquo;s organization or delegated permission is not authorized for this
          shared gallery. Use an admitted organization or contact the administrator.
        </Body1>
        <Button appearance="primary" onClick={() => void auth.signOut()}>
          Sign out and try another account
        </Button>
      </div>
    )
  }

  return <>{children}</>
}
