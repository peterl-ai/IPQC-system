import { describe, expect, it } from 'vitest'
import { emptyPatrolStandardDraft, emptyPatrolStandardItem, isInspectionItemEmpty, toPatrolStandardDraft, type PatrolStandard } from './model'

describe('Patrol Standard editor model', () => {
  it('starts with optional header values empty and no inspection items', () => {
    expect(emptyPatrolStandardDraft()).toMatchObject({ name: '', lineName: '', factoryCode: '', inspectionItems: [] })
  })

  it('copies API-loaded values into an isolated editor draft', () => {
    const standard: PatrolStandard = { id: 'server-id', name: 'Source', factoryCode: '', factoryName: '',
      workshopCode: '', lineCode: '', lineName: 'Line 1', materialCode: '', createdBy: 'server',
      createdTime: '', updatedBy: 'server', updatedTime: '',
      inspectionItems: [{ ...emptyPatrolStandardItem('item-id'), inspectionItem: 'Visual' }] }
    const draft = toPatrolStandardDraft(standard)
    draft.inspectionItems[0]!.inspectionItem = 'Edited'
    expect(standard.inspectionItems[0]!.inspectionItem).toBe('Visual')
  })

  it('rejects completely empty detail rows but permits partial input', () => {
    expect(isInspectionItemEmpty(emptyPatrolStandardItem('item-1'))).toBe(true)
    expect(isInspectionItemEmpty({ ...emptyPatrolStandardItem('item-2'), processCode: 'P1' })).toBe(false)
  })
})
