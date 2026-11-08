import { useState } from 'react'
import { Button, Tooltip, makeStyles, mergeClasses, shorthands, tokens } from '@fluentui/react-components'
import { CheckmarkRegular, CopyRegular } from '@fluentui/react-icons'

const useStyles = makeStyles({
  root: {
    position: 'relative',
  },
  pre: {
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
    fontFamily: tokens.fontFamilyMonospace,
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.padding(tokens.spacingVerticalM),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    maxHeight: '24rem',
    overflowY: 'auto',
    margin: 0,
  },
  copyButton: {
    position: 'absolute',
    top: tokens.spacingVerticalS,
    right: tokens.spacingHorizontalS,
  },
})

interface CodeBlockProps {
  content: string
  className?: string
}

/**
 * Scrollable, monospace, read-only viewer for an task's inline content (prompt/code text),
 * with a one-click copy button. Long content is clamped to a fixed max height instead of pushing
 * the rest of the page down indefinitely (used on TaskDetailPage).
 */
export function CodeBlock({ content, className }: CodeBlockProps) {
  const styles = useStyles()
  const [copied, setCopied] = useState(false)

  async function copy() {
    try {
      await navigator.clipboard.writeText(content)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard API unavailable or permission denied — no-op, matches ShareMenu's fallback.
    }
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      <Tooltip content={copied ? 'Copied!' : 'Copy to clipboard'} relationship="label">
        <Button
          className={styles.copyButton}
          appearance="subtle"
          size="small"
          icon={copied ? <CheckmarkRegular /> : <CopyRegular />}
          onClick={() => void copy()}
          aria-label={copied ? 'Copied to clipboard' : 'Copy to clipboard'}
        />
      </Tooltip>
      <pre className={styles.pre}>{content}</pre>
    </div>
  )
}
