import { useMemo, useRef, useState } from 'react'
import {
  Button,
  Divider,
  Tab,
  TabList,
  Textarea,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  type SelectTabData,
  type SelectTabEvent,
  type TextareaProps,
} from '@fluentui/react-components'
import {
  CheckmarkRegular,
  CodeRegular,
  CodeBlockRegular,
  CopyRegular,
  LinkRegular,
  TextBoldRegular,
  TextItalicRegular,
  TextBulletListLtrRegular,
} from '@fluentui/react-icons'
import { marked } from 'marked'
import DOMPurify from 'dompurify'

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalM,
  },
  toolbarActions: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXXS,
  },
  // Fluent's Textarea applies `className` to its wrapper, not the control. Sizing the wrapper made
  // CSS promote `overflow-x` to `auto` alongside `overflow-y` (one axis may not stay `visible` when
  // the other is clipped), so both scrollbars appeared on the very first keystroke. The wrapper now
  // only spans the column and the control itself is styled through the `textarea` slot.
  textareaRoot: {
    width: '100%',
  },
  textareaControl: {
    fontFamily: tokens.fontFamilyMonospace,
    lineHeight: tokens.lineHeightBase400,
    // Long-form authoring: keep the field generous, let it grow with the viewport, and never clip
    // sideways. Only the writer's own drag or genuinely long content brings a scrollbar into play.
    maxHeight: '70vh',
    overflowX: 'hidden',
    overflowWrap: 'anywhere',
  },
  preview: {
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.padding(tokens.spacingVerticalM),
    backgroundColor: tokens.colorNeutralBackground3,
    maxHeight: '70vh',
    overflowY: 'auto',
    overflowX: 'hidden',
    overflowWrap: 'anywhere',
    fontSize: tokens.fontSizeBase300,
    '& pre': {
      whiteSpace: 'pre-wrap',
      wordBreak: 'break-word',
      overflowX: 'auto',
    },
    '& :first-child': {
      marginTop: 0,
    },
    '& :last-child': {
      marginBottom: 0,
    },
  },
  empty: {
    color: tokens.colorNeutralForeground3,
  },
})

interface MarkdownContentEditorProps extends Pick<
  TextareaProps,
  'id' | 'aria-labelledby' | 'aria-describedby' | 'aria-invalid' | 'required'
> {
  value: string
  onChange: (value: string) => void
  rows?: number
  disabled?: boolean
  maxLength?: number
}

export function MarkdownContentEditor({
  value,
  onChange,
  rows = 10,
  disabled = false,
  maxLength,
  ...controlProps
}: MarkdownContentEditorProps) {
  const styles = useStyles()
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const [mode, setMode] = useState<'write' | 'preview'>('write')
  const [copied, setCopied] = useState(false)

  // Long-form fields deserve room to think in. Callers may ask for more, never for a cramped box.
  const effectiveRows = Math.max(rows, 10)
  // Keep Write and Preview the same height so switching tabs never jumps the page.
  const minHeight = `calc(${effectiveRows} * ${tokens.lineHeightBase400} + ${tokens.spacingVerticalXL})`

  const html = useMemo(() => {
    if (!value.trim()) return ''
    return DOMPurify.sanitize(marked.parse(value, { async: false }) as string)
  }, [value])

  const onTabSelect = (_: SelectTabEvent, data: SelectTabData) => {
    setMode(data.value as 'write' | 'preview')
  }

  function replaceSelection(
    prefix: string,
    suffix: string,
    placeholder: string,
    selection?: string,
  ) {
    const textarea = textareaRef.current
    const start = textarea?.selectionStart ?? value.length
    const end = textarea?.selectionEnd ?? start
    const selected = selection ?? value.slice(start, end)
    const inserted = selected || placeholder
    const nextValue = `${value.slice(0, start)}${prefix}${inserted}${suffix}${value.slice(end)}`
    if (maxLength !== undefined && nextValue.length > maxLength) return

    onChange(nextValue)
    requestAnimationFrame(() => {
      textarea?.focus()
      const cursor = selected
        ? start + prefix.length + inserted.length + suffix.length
        : start + prefix.length
      textarea?.setSelectionRange(cursor, cursor)
    })
  }

  function makeList() {
    const textarea = textareaRef.current
    const start = textarea?.selectionStart ?? value.length
    const end = textarea?.selectionEnd ?? start
    const selected = value.slice(start, end) || 'List item'
    replaceSelection(
      '',
      '',
      '',
      selected
        .split('\n')
        .map((line) => `- ${line}`)
        .join('\n'),
    )
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard API unavailable or permission denied — no-op, matches ShareMenu's fallback.
    }
  }

  return (
    <div className={styles.root}>
      <div className={styles.toolbar}>
        <TabList size="small" selectedValue={mode} onTabSelect={onTabSelect}>
          <Tab value="write">Write</Tab>
          <Tab value="preview">Preview</Tab>
        </TabList>
        <div className={styles.toolbarActions} role="toolbar" aria-label="Markdown formatting">
          {mode === 'write' && (
            <>
              <Button
                appearance="subtle"
                size="small"
                icon={<TextBoldRegular />}
                aria-label="Bold"
                title="Bold"
                onClick={() => replaceSelection('**', '**', 'bold text')}
                disabled={disabled}
              />
              <Button
                appearance="subtle"
                size="small"
                icon={<TextItalicRegular />}
                aria-label="Italic"
                title="Italic"
                onClick={() => replaceSelection('_', '_', 'italic text')}
                disabled={disabled}
              />
              <Button
                appearance="subtle"
                size="small"
                icon={<LinkRegular />}
                aria-label="Insert link"
                title="Insert link"
                onClick={() => replaceSelection('[', '](https://)', 'link text')}
                disabled={disabled}
              />
              <Button
                appearance="subtle"
                size="small"
                icon={<TextBulletListLtrRegular />}
                aria-label="Bulleted list"
                title="Bulleted list"
                onClick={makeList}
                disabled={disabled}
              />
              <Button
                appearance="subtle"
                size="small"
                icon={<CodeRegular />}
                aria-label="Inline code"
                title="Inline code"
                onClick={() => replaceSelection('`', '`', 'code')}
                disabled={disabled}
              />
              <Button
                appearance="subtle"
                size="small"
                icon={<CodeBlockRegular />}
                aria-label="Code block"
                title="Code block"
                onClick={() => replaceSelection('```\n', '\n```', 'code')}
                disabled={disabled}
              />
              <Divider vertical />
            </>
          )}
          <Button
            appearance="subtle"
            size="small"
            icon={copied ? <CheckmarkRegular /> : <CopyRegular />}
            onClick={() => void copy()}
            disabled={!value}
          >
            {copied ? 'Copied' : 'Copy'}
          </Button>
        </div>
      </div>
      {mode === 'write' ? (
        <Textarea
          {...controlProps}
          ref={textareaRef}
          className={styles.textareaRoot}
          textarea={{ className: styles.textareaControl, style: { minHeight } }}
          resize="vertical"
          value={value}
          onChange={(_, data) => onChange(data.value)}
          rows={effectiveRows}
          disabled={disabled}
          maxLength={maxLength}
        />
      ) : html ? (
        <div
          {...controlProps}
          className={styles.preview}
          style={{ minHeight }}
          role="region"
          tabIndex={0}
          aria-label="Markdown preview"
          dangerouslySetInnerHTML={{ __html: html }}
        />
      ) : (
        <div
          {...controlProps}
          className={mergeClasses(styles.preview, styles.empty)}
          style={{ minHeight }}
          role="region"
          tabIndex={0}
          aria-label="Markdown preview"
        >
          Nothing to preview yet.
        </div>
      )}
    </div>
  )
}
