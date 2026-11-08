import { afterEach, expect, it } from 'vitest'
import { canWriteCollection, selectedCollection, scopedPath } from '../../src/services/deployment'

afterEach(() => {
  window.sessionStorage.clear()
  window.history.replaceState({}, '', '/')
})

it('sends an explicit collection scope and preserves query and encoded identifiers', () => {
  expect(scopedPath('/api/tasks/id%2Fencoded?confirm=true')).toBe('/api/tasks/id%2Fencoded?confirm=true&collectionId=public-techniques')
  expect(scopedPath('/api/session')).toBe('/api/session?collectionId=public-techniques')
  expect(scopedPath('/api/feedback')).toBe('/api/feedback')
})

it('retains the explicitly selected collection across normal internal navigation', () => {
  window.history.replaceState({}, '', '/?collectionId=curated-tools')
  expect(selectedCollection()).toBe('curated-tools')
  window.history.replaceState({}, '', '/tasks/example')
  expect(scopedPath('/api/tasks/example')).toBe('/api/tasks/example?collectionId=curated-tools')
})

it('rejects unknown collection scope without falling back to a union or another collection', () => {
  window.history.replaceState({}, '', '/?collectionId=unknown-collection')
  expect(() => selectedCollection()).toThrow('Unknown collection')
})

it('honors server collection write grants in both authenticated and public deployments', () => {
  for (const publicSession of [true, false]) {
    expect(canWriteCollection(publicSession, { id: 'public-techniques', canWrite: false })).toBe(false)
    expect(canWriteCollection(publicSession, { id: 'public-techniques', canWrite: true })).toBe(true)
    expect(canWriteCollection(publicSession, { id: 'curated-tools', canWrite: true })).toBe(false)
  }
  expect(canWriteCollection(true, undefined)).toBe(false)
  expect(canWriteCollection(false, undefined)).toBe(true)
})
