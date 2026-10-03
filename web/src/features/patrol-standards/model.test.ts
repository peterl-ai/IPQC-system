import { describe, expect, it } from 'vitest'
import {
  copyMockPatrolStandard,
  createMockPatrolStandard,
  deleteMockPatrolStandard,
  emptyPatrolStandardDraft,
  emptyPatrolStandardItem,
  isInspectionItemEmpty,
  updateMockPatrolStandard,
  type PatrolStandard,
} from './model'

const source: PatrolStandard = {
  id: 'std-1',
  name: 'Source standard',
  factoryCode: '',
  factoryName: '',
  workshopCode: '',
  lineCode: '',
  lineName: 'Line A',
  materialCode: '',
  createdBy: 'Creator',
  createdTime: '2026-10-01 08:00',
  updatedBy: 'Creator',
  updatedTime: '2026-10-01 08:00',
  inspectionItems: [{ ...emptyPatrolStandardItem('item-1'), inspectionItem: 'Temperature' }],
}

describe('Patrol Standard mock state operations', () => {
  it('creates a new standard without requiring optional header fields', () => {
    const created = createMockPatrolStandard([], {
      ...emptyPatrolStandardDraft(),
      name: 'New standard',
      lineName: 'Line B',
    }, { id: 'std-2', user: 'Mock User', time: '2026-10-02 09:00' })

    expect(created).toHaveLength(1)
    expect(created[0]).toMatchObject({ id: 'std-2', name: 'New standard', lineName: 'Line B', factoryCode: '' })
  })

  it('edits the selected standard and keeps its original identity', () => {
    const updated = updateMockPatrolStandard([source], source.id, {
      ...emptyPatrolStandardDraft(),
      name: 'Updated standard',
      lineName: 'Line C',
    }, { user: 'Editor', time: '2026-10-02 10:00' })

    expect(updated[0]).toMatchObject({ id: 'std-1', name: 'Updated standard', createdBy: 'Creator', updatedBy: 'Editor' })
  })

  it('copies header and detail values without overwriting the source', () => {
    const draft = copyMockPatrolStandard(source)
    draft.name = 'Copied standard'
    draft.inspectionItems[0]!.inspectionItem = 'Pressure'
    const copied = createMockPatrolStandard([source], draft, { id: 'std-2', user: 'Mock User', time: '2026-10-02 11:00' })

    expect(copied).toHaveLength(2)
    expect(copied[0]).toEqual(source)
    expect(copied[1]).toMatchObject({ id: 'std-2', name: 'Copied standard' })
    expect(copied[1]!.inspectionItems[0]!.inspectionItem).toBe('Pressure')
    expect(copied[0]!.inspectionItems[0]!.inspectionItem).toBe('Temperature')
  })

  it('deletes only the selected standard', () => {
    const another = { ...source, id: 'std-2' }
    expect(deleteMockPatrolStandard([source, another], source.id)).toEqual([another])
  })

  it('rejects completely empty detail rows but permits partial realistic input', () => {
    expect(isInspectionItemEmpty(emptyPatrolStandardItem('item-1'))).toBe(true)
    expect(isInspectionItemEmpty({ ...emptyPatrolStandardItem('item-2'), processCode: 'LM-02' })).toBe(false)
  })
})
