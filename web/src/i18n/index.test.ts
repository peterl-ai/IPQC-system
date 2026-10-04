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
    expect(zhCN.field).toMatchObject({
      standardName: '巡检标准名称',
      inspectionItem: '巡检项目',
      inspectionContent: '巡检内容',
      samplingPlan: '抽样方案',
      defectLevel: '缺陷等级',
    })
    expect(en.field).toMatchObject({
      standardName: 'Patrol Standard Name',
      inspectionItem: 'Inspection Item',
      inspectionContent: 'Inspection Content',
      samplingPlan: 'Sampling Plan',
      defectLevel: 'Defect Level',
    })
  })
  it('has bilingual keys for the Patrol Plan recurrence editor', () => {
    for (const key of ['everyNHours', 'daily', 'weekly', 'times', 'weekdays', 'assigneeRequired', 'enableConfirm', 'disableConfirm'] as const) {
      expect(en.plan[key]).toBeTruthy()
      expect(zhCN.plan[key]).toBeTruthy()
    }
    expect(Object.keys(en.plan.day)).toEqual(Object.keys(zhCN.plan.day))
  })
})
