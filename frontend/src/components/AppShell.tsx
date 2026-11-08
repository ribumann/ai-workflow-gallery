import { useState, useSyncExternalStore, type ReactNode } from 'react'
import { Link as RouterLink, useLocation } from 'react-router-dom'
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Avatar,
  Body1Strong,
  Button,
  DrawerBody,
  DrawerHeader,
  DrawerHeaderTitle,
  Link,
  makeStyles,
  mergeClasses,
  OverlayDrawer,
  shorthands,
  tokens,
  useRestoreFocusSource,
  useRestoreFocusTarget,
} from '@fluentui/react-components'
import {
  AppsRegular,
  BotRegular,
  CodeRegular,
  DataBarVerticalRegular,
  DismissRegular,
  DocumentRegular,
  NavigationRegular,
  PeopleTeamRegular,
  SearchRegular,
  SignOutRegular,
} from '@fluentui/react-icons'
import { useAuth } from '../auth/useAuth'
import { FeedbackDialog } from './FeedbackDialog'
import { branding } from '../branding'
import { publicDemo } from '../services/deployment'

const mobileQuery = '(max-width: 899px)'
function subscribeToViewport(callback: () => void) {
  const query = window.matchMedia(mobileQuery)
  query.addEventListener('change', callback)
  return () => query.removeEventListener('change', callback)
}
const getMobileSnapshot = () => window.matchMedia(mobileQuery).matches

const navigationGroups = [
  {
    id: 'reports',
    label: 'Reports & AI Dashboards',
    items: [
      { id: 'all-reports', label: 'All links', to: '/reports', icon: <SearchRegular /> },
      {
        id: 'reports',
        label: 'Reports',
        to: '/reports?kind=Report',
        icon: <DataBarVerticalRegular />,
      },
      { id: 'apps', label: 'AI Dashboards', to: '/reports?kind=AiApp', icon: <AppsRegular /> },
    ],
  },
  {
    id: 'integrations',
    label: 'Tool integration',
    items: [
      {
        id: 'github-copilot',
        label: 'GitHub Copilot',
        to: '/integrations/github-copilot',
        icon: <CodeRegular />,
      },
      {
        id: 'cowork',
        label: 'Cowork',
        to: '/integrations/cowork',
        icon: <PeopleTeamRegular />,
      },
      { id: 'scout', label: 'Scout', to: '/integrations/scout', icon: <BotRegular /> },
    ],
  },
]

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    minHeight: '100vh',
    backgroundColor: tokens.colorNeutralBackground2,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalXL),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
    '@media (max-width: 599px)': {
      ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    },
  },
  brandRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, minWidth: 0 },
  brand: {
    minWidth: 0,
    overflowWrap: 'anywhere',
    color: tokens.colorNeutralForeground1,
    textDecorationLine: 'none',
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    '@media (max-width: 599px)': { fontSize: tokens.fontSizeBase400 },
  },
  logo: { width: '32px', height: '32px', objectFit: 'contain', flexShrink: 0 },
  brandText: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  organization: { color: tokens.colorNeutralForeground2, overflowWrap: 'anywhere' },
  account: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
  accountName: { '@media (max-width: 1199px)': { display: 'none' } },
  body: { display: 'flex', flex: 1, minWidth: 0 },
  sidebar: {
    flexShrink: 0,
    width: '17rem',
    ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalS),
    ...shorthands.borderRight('1px', 'solid', tokens.colorNeutralStroke2),
  },
  navPanel: { margin: 0, paddingInlineStart: tokens.spacingHorizontalXXL },
  navLink: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    minHeight: '44px',
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalL),
    marginBottom: tokens.spacingVerticalXXS,
    borderRadius: tokens.borderRadiusMedium,
    textDecorationLine: 'none',
    color: tokens.colorNeutralForeground2,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
      color: tokens.colorNeutralForeground1,
    },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: '2px',
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
  selected: {
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
    ':hover': {
      backgroundColor: tokens.colorBrandBackground2Hover,
      color: tokens.colorBrandForeground1,
    },
  },
  content: { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' },
  main: {
    flex: 1,
    width: '100%',
    maxWidth: '90rem',
    marginLeft: 'auto',
    marginRight: 'auto',
    ...shorthands.padding(tokens.spacingVerticalXXL, tokens.spacingHorizontalXXL),
    '@media (max-width: 599px)': {
      ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalM),
    },
  },
  footer: {
    textAlign: 'center',
    ...shorthands.padding(tokens.spacingVerticalM),
    color: tokens.colorNeutralForeground3,
  },
  skip: {
    position: 'absolute',
    top: '-100px',
    left: tokens.spacingHorizontalM,
    zIndex: 100,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground1,
    ':focus': { top: tokens.spacingVerticalS },
  },
})

function Navigation({ onNavigate }: { onNavigate: () => void }) {
  const styles = useStyles()
  const { pathname, search } = useLocation()
  const params = new URLSearchParams(search)
  const active = pathname.startsWith('/integrations')
    ? (pathname.split('/')[2] ?? 'github-copilot')
    : pathname.startsWith('/reports')
      ? params.get('kind') === 'Report'
        ? 'reports'
        : params.get('kind') === 'AiApp'
          ? 'apps'
          : 'all-reports'
      : 'catalog'

  return (
    <nav aria-label="Main navigation">
      <RouterLink
        to="/"
        onClick={onNavigate}
        aria-current={active === 'catalog' ? 'page' : undefined}
        className={mergeClasses(styles.navLink, active === 'catalog' && styles.selected)}
      >
        <DocumentRegular />
        <span>Catalog</span>
      </RouterLink>
      <Accordion multiple collapsible defaultOpenItems={[]}>
        {navigationGroups.map((group) => (
          <AccordionItem key={group.id} value={group.id}>
            <AccordionHeader>{group.label}</AccordionHeader>
            <AccordionPanel className={styles.navPanel}>
              {group.items.map((item) => (
                <RouterLink
                  key={item.id}
                  to={item.to}
                  onClick={onNavigate}
                  aria-current={active === item.id ? 'page' : undefined}
                  className={mergeClasses(styles.navLink, active === item.id && styles.selected)}
                >
                  {item.icon}
                  <span>{item.label}</span>
                </RouterLink>
              ))}
            </AccordionPanel>
          </AccordionItem>
        ))}
      </Accordion>
    </nav>
  )
}

export function AppShell({ children }: { children: ReactNode }) {
  const styles = useStyles()
  const auth = useAuth()
  const publicMode = publicDemo || auth.publicDemo === true
  const isMobile = useSyncExternalStore(subscribeToViewport, getMobileSnapshot, () => false)
  const [desktopNavOpen, setDesktopNavOpen] = useState(true)
  const [mobileNavOpen, setMobileNavOpen] = useState(false)
  const navOpen = isMobile ? mobileNavOpen : desktopNavOpen
  const restoreFocusTarget = useRestoreFocusTarget()
  const restoreFocusSource = useRestoreFocusSource()

  return (
    <div className={styles.root}>
      <a href="#main-content" className={styles.skip}>
        Skip to content
      </a>
      <header className={styles.header}>
        <div className={styles.brandRow}>
          <Button
            appearance="subtle"
            icon={<NavigationRegular />}
            aria-label="Toggle navigation"
            aria-expanded={navOpen}
            aria-controls="app-navigation"
            {...restoreFocusTarget}
            onClick={() =>
              isMobile ? setMobileNavOpen(!mobileNavOpen) : setDesktopNavOpen(!desktopNavOpen)
            }
          />
          {branding.logo && <img src={branding.logo} alt="" className={styles.logo} />}
          <div className={styles.brandText}>
            <RouterLink to="/" className={styles.brand}>
              {branding.title}
            </RouterLink>
            {branding.organization && (
              <span className={styles.organization}>{branding.organization}</span>
            )}
          </div>
        </div>
        <div className={styles.account}>
          {publicMode ? (
            <Body1Strong>Public demo</Body1Strong>
          ) : (
            <>
              <FeedbackDialog />
              <Body1Strong className={styles.accountName}>
                {auth.displayName ?? 'Signed in'}
              </Body1Strong>
              <Avatar name={auth.displayName ?? undefined} size={28} />
              {auth.isAuthenticated && (
                <Button
                  appearance="subtle"
                  icon={<SignOutRegular />}
                  onClick={() => void auth.signOut()}
                >
                  Sign out
                </Button>
              )}
            </>
          )}
        </div>
      </header>
      <div className={styles.body}>
        {!isMobile && desktopNavOpen && (
          <aside id="app-navigation" className={styles.sidebar}>
            <Navigation onNavigate={() => {}} />
          </aside>
        )}
        {isMobile && (
          <OverlayDrawer
            {...restoreFocusSource}
            id="app-navigation"
            position="start"
            open={mobileNavOpen}
            onOpenChange={(_, data) => setMobileNavOpen(data.open)}
          >
            <DrawerHeader>
              <DrawerHeaderTitle
                action={
                  <Button
                    appearance="subtle"
                    icon={<DismissRegular />}
                    aria-label="Close navigation"
                    onClick={() => setMobileNavOpen(false)}
                  />
                }
              >
                Navigation
              </DrawerHeaderTitle>
            </DrawerHeader>
            <DrawerBody>
              <Navigation onNavigate={() => setMobileNavOpen(false)} />
            </DrawerBody>
          </OverlayDrawer>
        )}
        <div className={styles.content}>
          <main id="main-content" tabIndex={-1} className={styles.main}>
            {children}
          </main>
          {branding.links && Object.keys(branding.links).length > 0 && (
            <footer className={styles.footer}>
              {Object.entries(branding.links).map(([label, href]) => (
                <Link
                  key={label}
                  href={href}
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{ marginInline: tokens.spacingHorizontalS }}
                >
                  {
                    { support: 'Support', privacy: 'Privacy', terms: 'Terms', source: 'Source' }[
                      label
                    ]
                  }
                </Link>
              ))}
            </footer>
          )}
        </div>
      </div>
    </div>
  )
}
