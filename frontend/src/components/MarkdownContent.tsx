import { useMemo } from 'react'
import { makeStyles, shorthands, tokens } from '@fluentui/react-components'
import { marked } from 'marked'
import DOMPurify from 'dompurify'

const useStyles = makeStyles({
  content: {
    maxWidth: '75ch',
    overflowWrap: 'anywhere',
    lineHeight: tokens.lineHeightBase400,
    '& h1, & h2, & h3, & h4': {
      lineHeight: tokens.lineHeightBase500,
      marginTop: tokens.spacingVerticalXXL,
      marginBottom: tokens.spacingVerticalM,
    },
    '& pre': {
      whiteSpace: 'pre-wrap',
      overflowWrap: 'anywhere',
      overflowX: 'auto',
      backgroundColor: tokens.colorNeutralBackground3,
      ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
      ...shorthands.borderRadius(tokens.borderRadiusMedium),
      ...shorthands.padding(tokens.spacingVerticalM),
    },
    '& code': {
      fontFamily: tokens.fontFamilyMonospace,
      overflowWrap: 'anywhere',
    },
    '& a': { color: tokens.colorBrandForegroundLink },
    '& img': { maxWidth: '100%', height: 'auto' },
    '& table': { display: 'block', maxWidth: '100%', overflowX: 'auto' },
    '& :first-child': { marginTop: 0 },
  },
})

export function MarkdownContent({ content }: { content: string }) {
  const styles = useStyles()
  const html = useMemo(() => DOMPurify.sanitize(marked.parse(content, { async: false })), [content])
  return <div className={styles.content} dangerouslySetInnerHTML={{ __html: html }} />
}
