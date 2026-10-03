import { describe, expect, it } from 'vitest'
import { buildPatrolStandardRequiredRules, isXlsxFileName, patrolStandardRequiredFields } from './validation'

describe('Patrol Standard header validation', () => {
  it('requires only Line Name and Patrol Standard Name', () => {
    expect(patrolStandardRequiredFields).toEqual(['lineName', 'standardName'])
  })

  it('builds required, whitespace-aware Ant Design rules', () => {
    const rules = buildPatrolStandardRequiredRules({
      lineName: 'Line Name is required.',
      standardName: 'Patrol Standard Name is required.',
    })

    expect(Object.keys(rules)).toEqual(['lineName', 'standardName'])
    expect(rules.lineName[0]).toEqual({ required: true, whitespace: true, message: 'Line Name is required.' })
    expect(rules.standardName[0]).toEqual({ required: true, whitespace: true, message: 'Patrol Standard Name is required.' })
  })

  it('accepts only .xlsx import filenames, case-insensitively', () => {
    expect(isXlsxFileName('patrol-standard.xlsx')).toBe(true)
    expect(isXlsxFileName('PATROL-STANDARD.XLSX')).toBe(true)
    expect(isXlsxFileName('patrol-standard.xls')).toBe(false)
    expect(isXlsxFileName('patrol-standard.xlsx.csv')).toBe(false)
  })
})
