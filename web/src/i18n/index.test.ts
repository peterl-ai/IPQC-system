import { describe, expect, it } from 'vitest'
import { getSavedLocale, LANGUAGE_KEY, saveLocale } from './index'

describe('language preference persistence', () => {
  it('defaults to English for an unsupported or missing value', () => {
    const storage = { getItem: () => null }
    expect(getSavedLocale(storage)).toBe('en')
  })

  it('restores Simplified Chinese', () => {
    const storage = { getItem: (key: string) => key === LANGUAGE_KEY ? 'zh-CN' : null }
    expect(getSavedLocale(storage)).toBe('zh-CN')
  })

  it('persists the selected language', () => {
    const values = new Map<string, string>()
    saveLocale('zh-CN', { setItem: (key, value) => values.set(key, value) })
    expect(values.get(LANGUAGE_KEY)).toBe('zh-CN')
  })
})
