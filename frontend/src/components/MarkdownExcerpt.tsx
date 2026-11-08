import { useMemo } from 'react'
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components'
import { marked } from 'marked'
import DOMPurify from 'dompurify'

/** Inline-only formatting. Links, images, tables and code blocks are deliberately stripped to their
 * text so an excerpt can never nest an interactive element inside an already-clickable row, and can
 * never blow out a grid cell with a block-level layout. */
const ALLOWED_TAGS = [
  'strong',
  'em',
  'b',
  'i',
  'code',
  'del',
  's',
  // Allowed only so the post-sanitise pass can substitute a space for it; it never reaches the DOM.
  'br',
  'p',
  'ul',
  'ol',
  'li',
  'h1',
  'h2',
  'h3',
  'h4',
  'h5',
  'h6',
  'blockquote',
]

const useStyles = makeStyles({
  excerpt: {
    display: '-webkit-box',
    WebkitBoxOrient: 'vertical',
    overflowX: 'hidden',
    overflowY: 'hidden',
    overflowWrap: 'anywhere',
    // List surfaces often set `pre-wrap` for raw text; an excerpt is rendered HTML, so the newlines
    // Markdown emits must not survive as literal blank lines inside the clamp.
    whiteSpace: 'normal',
    // Block elements survive sanitising so their text is kept, but an excerpt is a single run of
    // prose: flatten them so headings and list items read as one clamped line of copy.
    '& p, & ul, & ol, & li, & h1, & h2, & h3, & h4, & h5, & h6, & blockquote': {
      display: 'inline',
      marginTop: 0,
      marginBottom: 0,
      marginLeft: 0,
      marginRight: 0,
      paddingLeft: 0,
      fontSize: 'inherit',
      fontWeight: 'inherit',
      lineHeight: 'inherit',
    },
    '& code': {
      fontFamily: tokens.fontFamilyMonospace,
    },
  },
})

/** Renders a short, formatted preview of a Markdown field for list and grid surfaces, clamped to
 * `lines`. Overviews used to print the raw source, so authors saw `**bold**` and `- item` leaking
 * into every card; this keeps the emphasis while staying a compact, single block of text. */
export function MarkdownExcerpt({
  content,
  lines = 2,
  className,
}: {
  content: string | null | undefined
  lines?: number
  className?: string
}) {
  const styles = useStyles()

  const html = useMemo(() => {
    if (!content?.trim()) return ''
    const parsed = marked.parse(content, { async: false }) as string
    const clean = DOMPurify.sanitize(parsed, {
      ALLOWED_TAGS,
      ALLOWED_ATTR: [],
    })
    // Sanitising leaves block tags butted together ("</p><p>"), which would read as one run-on
    // word once they are displayed inline. `marked` also emits a hard break as "one<br>two" with no
    // surrounding whitespace, so that tag becomes a space rather than disappearing. Runs after
    // sanitising and only ever substitutes whitespace.
    return clean.replace(/<br\s*\/?>/g, ' ').replace(/<\/(p|li|h[1-6]|blockquote)>/g, '</$1> ')
  }, [content])

  if (!html) return null

  return (
    <div
      className={mergeClasses(styles.excerpt, className)}
      style={{ WebkitLineClamp: lines }}
      dangerouslySetInnerHTML={{ __html: html }}
    />
  )
}
