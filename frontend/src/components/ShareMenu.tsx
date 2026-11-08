import { useState } from 'react'
import {
  Button,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
} from '@fluentui/react-components'
import { CheckmarkRegular, LinkRegular, MailRegular, PeopleTeamRegular, ShareRegular } from '@fluentui/react-icons'
import { branding } from '../branding'
import { selectedCollection } from '../services/deployment'

interface ShareMenuProps {
  title: string
}

/**
 * Copy-link / email / Teams share menu (issue #60). Pure client-side URL-scheme deep links only —
 * no Graph API calls and no app registration required, consistent with the app's current
 * no-app-registration constraint.
 */
export function ShareMenu({ title }: ShareMenuProps) {
  const [copied, setCopied] = useState(false)

  const scopedUrl = new URL(window.location.href)
  scopedUrl.searchParams.set('collectionId', selectedCollection())
  const url = scopedUrl.href
  const shareText = `Check this out: ${title}`
  const mailtoHref = `mailto:?subject=${encodeURIComponent(title)}&body=${encodeURIComponent(`${shareText}\n${url}`)}`
  const teamsHref = `https://teams.microsoft.com/share?href=${encodeURIComponent(url)}&msgText=${encodeURIComponent(shareText)}`

  async function copyLink() {
    try {
      await navigator.clipboard.writeText(url)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard API unavailable or permission denied — no-op; the menu item simply won't
      // visually confirm, but the menu remains usable for the other share options.
    }
  }

  return (
    <Menu>
      <MenuTrigger disableButtonEnhancement>
        <Button appearance="secondary" icon={<ShareRegular />}>
          Share
        </Button>
      </MenuTrigger>
      <MenuPopover>
        <MenuList>
          {branding.shareChannels.includes('copy') && <MenuItem
            icon={copied ? <CheckmarkRegular /> : <LinkRegular />}
            onClick={() => void copyLink()}
          >
            {copied ? 'Link copied!' : 'Copy link'}
          </MenuItem>}
          {branding.shareChannels.includes('email') && <MenuItem icon={<MailRegular />} onClick={() => window.open(mailtoHref, '_self')}>
            Share via email
          </MenuItem>}
          {branding.shareChannels.includes('teams') && <MenuItem
            icon={<PeopleTeamRegular />}
            onClick={() => window.open(teamsHref, '_blank', 'noopener')}
          >
            Share to Teams
          </MenuItem>}
        </MenuList>
      </MenuPopover>
    </Menu>
  )
}
