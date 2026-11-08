import { useMemo, useState } from 'react'
import { useParams, Navigate } from 'react-router-dom'
import {
  Badge,
  Body1,
  Body1Strong,
  Button,
  Caption1,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Subtitle1,
  Title1,
  Tooltip,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components'
import { CheckmarkRegular, CopyRegular } from '@fluentui/react-icons'
import { MarkdownContent } from '../../components/MarkdownContent'
import { findIntegrationGuide, type IntegrationStatus, type StarterPrompt } from './integrationGuides'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXL, minWidth: 0 },
  header: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, minWidth: 0 },
  headingRow: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalM,
  },
  heading: { margin: 0, overflowWrap: 'anywhere' },
  tagline: { color: tokens.colorNeutralForeground2, maxWidth: '75ch' },
  section: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, minWidth: 0 },
  sectionHeading: { margin: 0 },
  promptList: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  promptCard: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalL),
  },
  promptHeader: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
  },
  promptText: {
    whiteSpace: 'pre-wrap',
    overflowWrap: 'anywhere',
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    margin: 0,
  },
  note: { color: tokens.colorNeutralForeground3, maxWidth: '75ch' },
})

const statusAppearance: Record<IntegrationStatus, 'success' | 'warning' | 'informative'> = {
  available: 'success',
  preview: 'warning',
  blocked: 'informative',
}

const statusIntent: Record<IntegrationStatus, 'success' | 'warning' | 'info'> = {
  available: 'success',
  preview: 'warning',
  blocked: 'info',
}

function PromptCard({ prompt }: { prompt: StarterPrompt }) {
  const styles = useStyles()
  const [copied, setCopied] = useState(false)

  async function copy() {
    try {
      await navigator.clipboard.writeText(prompt.prompt)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard unavailable or permission denied — the text is selectable, so this is a no-op.
    }
  }

  return (
    <li className={styles.promptCard}>
      <div className={styles.promptHeader}>
        <Body1Strong>{prompt.title}</Body1Strong>
        <Tooltip content={copied ? 'Copied!' : 'Copy prompt'} relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={copied ? <CheckmarkRegular /> : <CopyRegular />}
            onClick={() => void copy()}
            aria-label={copied ? `Copied prompt: ${prompt.title}` : `Copy prompt: ${prompt.title}`}
          />
        </Tooltip>
      </div>
      <pre className={styles.promptText}>{prompt.prompt}</pre>
    </li>
  )
}

/**
 * Renders one "Tool integration" guide (GitHub Copilot, Cowork, Scout) from its definition in
 * `integrationGuides.ts`. The MCP endpoint shown is derived from the origin the app is served
 * from, so UAT and production each display their own address without a separate build.
 */
export function IntegrationGuidePage() {
  const styles = useStyles()
  const { id } = useParams<{ id: string }>()
  const guide = findIntegrationGuide(id)
  const origin = typeof window === 'undefined' ? '' : window.location.origin
  const sections = useMemo(() => guide?.sections(origin) ?? [], [guide, origin])

  if (!guide) return <Navigate to="/integrations/github-copilot" replace />

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <div className={styles.headingRow}>
          <Title1 as="h1" className={styles.heading}>
            {guide.label}
          </Title1>
          <Badge appearance="tint" color={statusAppearance[guide.status]}>
            {guide.statusLabel}
          </Badge>
        </div>
        <Body1 className={styles.tagline}>{guide.tagline}</Body1>
      </div>

      <MessageBar intent={statusIntent[guide.status]}>
        <MessageBarBody>
          <MessageBarTitle>{guide.statusLabel}</MessageBarTitle> {guide.statusDetail}
        </MessageBarBody>
      </MessageBar>

      {sections.map((section) => (
        <section key={section.id} className={styles.section} aria-labelledby={`h-${section.id}`}>
          <Subtitle1 as="h2" id={`h-${section.id}`} className={styles.sectionHeading}>
            {section.heading}
          </Subtitle1>
          <MarkdownContent content={section.body} />
        </section>
      ))}

      <section className={styles.section} aria-labelledby="h-prompts">
        <Subtitle1 as="h2" id="h-prompts" className={styles.sectionHeading}>
          Starter prompts
        </Subtitle1>
        <Body1 className={styles.tagline}>
          Paste one of these into {guide.label} and replace the text in brackets. Each is written so
          the assistant checks the gallery first, rather than answering from memory.
        </Body1>
        {guide.promptsNote && <Caption1 className={styles.note}>{guide.promptsNote}</Caption1>}
        <ul className={styles.promptList}>
          {guide.prompts.map((prompt) => (
            <PromptCard key={prompt.title} prompt={prompt} />
          ))}
        </ul>
      </section>
    </div>
  )
}
