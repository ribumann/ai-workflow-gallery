export interface Branding {
  title: string
  organization?: string
  logo?: string
  favicon?: string
  accent?: string
  links?: Partial<Record<'support' | 'privacy' | 'terms' | 'source', string>>
  shareChannels: ('copy' | 'email' | 'teams')[]
  publisher?: { name: string; website: string; privacy: string; terms: string }
}

export function validateBranding(input: unknown): Branding
export function loadBranding(): Branding
export function brandingHtml(branding: Branding, html: string): string
export function brandingText(branding: Branding, content: string): string
