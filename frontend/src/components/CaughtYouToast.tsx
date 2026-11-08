import { useEffect } from 'react'
import { Text, makeStyles, tokens } from '@fluentui/react-components'
import { ShieldCheckmarkRegular } from '@fluentui/react-icons'

const popIn = {
  '0%': { transform: 'translateY(24px) scale(0.85)', opacity: 0 },
  '55%': { transform: 'translateY(-6px) scale(1.05)', opacity: 1 },
  '75%': { transform: 'translateY(2px) scale(0.98)' },
  '100%': { transform: 'translateY(0) scale(1)', opacity: 1 },
}

const wiggle = {
  '0%': { transform: 'rotate(0deg)' },
  '25%': { transform: 'rotate(-18deg)' },
  '75%': { transform: 'rotate(18deg)' },
  '100%': { transform: 'rotate(0deg)' },
}

const useStyles = makeStyles({
  toast: {
    position: 'fixed',
    bottom: tokens.spacingVerticalXXL,
    right: tokens.spacingHorizontalXXL,
    zIndex: 1000,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    maxWidth: '22rem',
    backgroundColor: tokens.colorPaletteRedBackground2,
    color: tokens.colorPaletteRedForeground2,
    ...{ border: `1px solid ${tokens.colorPaletteRedBorder2}` },
    borderRadius: tokens.borderRadiusXLarge,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    boxShadow: tokens.shadow16,
    animationName: popIn,
    animationDuration: '480ms',
    animationTimingFunction: 'cubic-bezier(.34,1.56,.64,1)',
    animationFillMode: 'both',
  },
  icon: {
    flexShrink: 0,
    fontSize: '22px',
    animationName: wiggle,
    animationDuration: '600ms',
    animationIterationCount: 3,
    animationDelay: '480ms',
  },
})

const AUTO_DISMISS_MS = 3200

/**
 * A small, self-contained "gotcha" toast: appears when a save attempt is rejected for looking
 * like malicious input (an unsafe reference-link scheme, or a path-traversal file name — see
 * utils/securityRejection.ts), plays a short bounce-in + icon-wiggle animation, then auto-dismisses.
 * Purely cosmetic; the real security enforcement is the server-side validation that triggered it.
 */
export function CaughtYouToast({ visible, onHide }: { visible: boolean; onHide: () => void }) {
  const styles = useStyles()

  useEffect(() => {
    if (!visible) {
      return
    }
    const timer = setTimeout(onHide, AUTO_DISMISS_MS)
    return () => clearTimeout(timer)
  }, [visible, onHide])

  if (!visible) {
    return null
  }

  return (
    <div className={styles.toast} role="status" aria-live="polite">
      <ShieldCheckmarkRegular className={styles.icon} />
      <Text weight="semibold">
        Well tried — caught you! 🕵️ That input looked a little too sneaky, so we blocked it.
      </Text>
    </div>
  )
}
