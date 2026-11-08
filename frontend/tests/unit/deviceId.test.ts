import { describe, expect, it, beforeEach } from 'vitest'
import { getOrCreateDeviceId } from '../../src/utils/deviceId'

// Covers the localStorage-persisted device id used to scope one-vote-per-browser rating (issue
// #60): it must be generated once, then reused (stable) across calls within the same browser.
describe('getOrCreateDeviceId', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  it('generates a non-empty id when none is stored yet', () => {
    const id = getOrCreateDeviceId()
    expect(id).toBeTruthy()
  })

  it('returns the same id on subsequent calls', () => {
    const first = getOrCreateDeviceId()
    const second = getOrCreateDeviceId()
    expect(second).toBe(first)
  })

  it('persists the id in localStorage', () => {
    const id = getOrCreateDeviceId()
    expect(window.localStorage.getItem('workflow-gallery-device-id')).toBe(id)
  })
})
