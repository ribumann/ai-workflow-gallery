/**
 * Returns true only for well-formed absolute http:// or https:// URLs. Used before rendering any
 * user-supplied link as a clickable `<a href>` — the API rejects non-http(s) reference links on
 * write (see api/src/Functions/Tasks.cs → IsSafeHttpUrl), but this is a defense-in-depth check
 * on the read/render path too, in case older data or a future write path ever bypasses that guard
 * (issue #67 — adversarial review found `javascript:` URIs rendered as clickable links).
 */
export function isSafeHttpUrl(value: string | null | undefined): value is string {
  if (!value) {
    return false
  }

  try {
    const url = new URL(value)
    return url.protocol === 'http:' || url.protocol === 'https:'
  } catch {
    return false
  }
}

/** Reports are embedded in a secure app: reject URL forms browsers silently normalize. */
export function isSafeReportUrl(value: string): boolean {
  if (!/^https:\/\/[^/]/i.test(value) || value.includes('\\') || /[\s\p{Cc}]/u.test(value)) {
    return false
  }
  try {
    const url = new URL(value)
    return url.protocol === 'https:' && Boolean(url.hostname) && !url.username && !url.password
  } catch {
    return false
  }
}

export function canEmbedReportUrl(value: string, appOrigin: string): boolean {
  return isSafeReportUrl(value) && new URL(value).origin !== appOrigin
}

/** URL hints, not a determination of the destination's current framing policy. */
export function getReportEmbedCaution(url: URL): string | undefined {
  const host = url.hostname.toLowerCase().replace(/\.$/, '')
  if (host === 'sharepoint.com' || host.endsWith('.sharepoint.com')) {
    return 'SharePoint pages often block viewing inside other apps. Choose Open externally, or try a destination-supported preview.'
  }
  if (
    (host === 'app.powerbi.com' || host === 'msit.powerbi.com') &&
    url.pathname.toLowerCase().startsWith('/groups/')
  ) {
    return 'This is a Power BI report-page link, not a secure embed URL. Choose Open externally, or use File > Embed report > Website or portal in Power BI.'
  }
  if (host === 'aka.ms' || host === '1drv.ms') {
    return 'This short link may redirect to a page that blocks embedding. Choose Open externally, or use the destination\'s supported embed URL.'
  }
  return undefined
}
