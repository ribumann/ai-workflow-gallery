import { describe, expect, it } from 'vitest'

import {
  canEmbedReportUrl,
  getReportEmbedCaution,
  isSafeHttpUrl,
  isSafeReportUrl,
} from '../../src/utils/url'

describe('isSafeHttpUrl', () => {
  it.each([
    'http://example.com',
    'https://example.com/path?query=value#section',
    'HTTPS://EXAMPLE.COM',
    'https://user:password@example.com',
    ' https://example.com/path ',
    'https://example.com/a b',
    'https://example.com/a\nb',
    'https:\\\\example.com\\path',
    'https:example.com',
    'https:///example.com',
    'http://localhost:7071',
    'https://[::1]:8443/path',
  ])('accepts HTTP(S) URL parser input %j', (value) => {
    expect(isSafeHttpUrl(value)).toBe(true)
  })

  it.each([
    null,
    undefined,
    '',
    ' ',
    'not a URL',
    '/relative/path',
    '//example.com/path',
    'javascript:alert(1)',
    'data:text/html,hello',
    'mailto:person@example.com',
    'ftp://example.com/file',
    'https://',
    'https://exa mple.com',
    'https://example.com:invalid',
  ])('rejects missing, invalid or non-HTTP(S) input %j', (value) => {
    expect(isSafeHttpUrl(value)).toBe(false)
  })
})

describe('isSafeReportUrl', () => {
  it.each([
    'https://example.com',
    'HTTPS://EXAMPLE.COM/path?query=value#section',
    'https://example.com:8443/path',
    'https://localhost/path',
    'https://[::1]/path',
    'https://example.com/a%20b?value=%0A',
    'https://example.com/a%5Cb',
    'https://example.com.',
    'https://@example.com',
  ])('accepts the current HTTPS report URL form %j', (value) => {
    expect(isSafeReportUrl(value)).toBe(true)
  })

  it.each([
    '',
    'not a URL',
    '/relative/path',
    '//example.com/path',
    'http://example.com',
    'javascript:alert(1)',
    'data:text/html,hello',
    'ftp://example.com/file',
    'https:example.com',
    'https:/example.com',
    'https:///example.com',
    'https://',
    'https://example.com:invalid',
    'https://user@example.com',
    'https://user:password@example.com',
    'https://:password@example.com',
    ' https://example.com',
    'https://example.com ',
    'https://example.com/a b',
    'https://example.com/\tpath',
    'https://example.com/\npath',
    'https://example.com/\rpath',
    'https://example.com/\u0000path',
    'https://example.com/\u007fpath',
    'https://example.com/\u0085path',
    'https://example.com/\u00a0path',
    'https:\\\\example.com\\path',
    'https://example.com\\path',
  ])('rejects unsafe or normalized report URL input %j', (value) => {
    expect(isSafeReportUrl(value)).toBe(false)
  })
})

describe('canEmbedReportUrl', () => {
  const appOrigin = 'https://gallery.example.com'

  it.each([
    'http://reports.example.com',
    'https://user:password@reports.example.com',
    'https://reports.example.com/a b',
    '/reports/1',
    'not a URL',
  ])('rejects invalid report input before comparing origins: %j', (value) => {
    expect(canEmbedReportUrl(value, appOrigin)).toBe(false)
  })

  it.each([
    'https://gallery.example.com',
    'https://gallery.example.com/reports/1?preview=true#chart',
    'HTTPS://GALLERY.EXAMPLE.COM/reports/1',
    'https://gallery.example.com:443/reports/1',
  ])('rejects a matching parsed origin: %j', (value) => {
    expect(canEmbedReportUrl(value, appOrigin)).toBe(false)
  })

  it.each([
    'https://reports.example.com',
    'https://sub.gallery.example.com/report',
    'https://gallery.example.com:8443/report',
    'https://gallery.example.com.evil.example/report',
  ])('allows a different valid HTTPS origin without checking framing: %j', (value) => {
    expect(canEmbedReportUrl(value, appOrigin)).toBe(true)
  })

  it.each([
    'https://gallery.example.com/',
    'https://gallery.example.com:443',
    'HTTPS://GALLERY.EXAMPLE.COM',
    'https://gallery.example.com/path',
  ])('compares appOrigin verbatim rather than normalizing it: %j', (origin) => {
    expect(canEmbedReportUrl('https://gallery.example.com/report', origin)).toBe(true)
  })
})

describe('getReportEmbedCaution', () => {
  const sharePointCaution =
    'SharePoint pages often block viewing inside other apps. Choose Open externally, or try a destination-supported preview.'
  const powerBiCaution =
    'This is a Power BI report-page link, not a secure embed URL. Choose Open externally, or use File > Embed report > Website or portal in Power BI.'
  const shortLinkCaution =
    "This short link may redirect to a page that blocks embedding. Choose Open externally, or use the destination's supported embed URL."

  it.each([
    'https://sharepoint.com',
    'https://tenant.sharepoint.com/sites/team',
    'https://nested.tenant.sharepoint.com/anything',
    'https://TENANT.SHAREPOINT.COM./sites/team',
    'https://tenant.sharepoint.com:8443/sites/team',
    'http://tenant.sharepoint.com/sites/team',
    'ftp://tenant.sharepoint.com/file',
  ])('warns for SharePoint hostnames without validating protocol: %j', (value) => {
    expect(getReportEmbedCaution(new URL(value))).toBe(sharePointCaution)
  })

  it.each([
    'https://app.powerbi.com/groups/workspace/reports/report',
    'https://msit.powerbi.com/groups/me/reports/report',
    'https://APP.POWERBI.COM./GROUPS/Workspace',
    'https://app.powerbi.com:8443/groups/',
  ])('warns for Power BI report-page paths: %j', (value) => {
    expect(getReportEmbedCaution(new URL(value))).toBe(powerBiCaution)
  })

  it.each(['https://aka.ms/report', 'https://1drv.ms/file', 'https://AKA.MS./report'])(
    'warns for exact short-link hostnames: %j',
    (value) => {
      expect(getReportEmbedCaution(new URL(value))).toBe(shortLinkCaution)
    },
  )

  it.each([
    'https://reports.example.com/report',
    'https://evilsharepoint.com/sites/team',
    'https://sharepoint.com.evil.example/sites/team',
    'https://app.powerbi.com.evil.example/groups/me',
    'https://sub.app.powerbi.com/groups/me',
    'https://app.powerbi.com/reportEmbed?reportId=123',
    'https://app.powerbi.com/groups',
    'https://app.powerbi.com/groups-other/report',
    'https://app.powerbi.com/other/groups/me',
    'https://app.powerbi.com/?path=/groups/me',
    'https://sub.aka.ms/report',
    'https://aka.ms.evil.example/report',
    'https://sub.1drv.ms/file',
  ])('returns no hint outside the exact hostname/path rules: %j', (value) => {
    expect(getReportEmbedCaution(new URL(value))).toBeUndefined()
  })
})
