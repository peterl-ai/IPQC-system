import { describe, expect, it } from 'vitest'
import { getSavedLocale, LANGUAGE_KEY, saveLocale } from './index'
import en from './locales/en'
import zhCN from './locales/zh-CN'

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

  it('uses the confirmed Simplified Chinese IPQC terminology without changing English', () => {
    expect(zhCN.nav).toMatchObject({
      linePatrol: '人工巡检',
      standards: '巡检标准管理',
      plans: '人工巡检计划',
      tasks: '人工巡检任务',
      records: '已完成巡检台账',
    })
    expect(en.nav).toMatchObject({
      linePatrol: 'Line Patrol',
      standards: 'Patrol Standards',
      plans: 'Patrol Plans',
      tasks: 'Patrol Tasks',
      records: 'Completed Patrol Records',
    })
  })
})
