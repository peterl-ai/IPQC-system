export interface PatrolStandardItem {
  id: string
  processCode: string
  processName: string
  itemCategory: string
  inspectionItem: string
  inspectionContent: string
  upperOperator: string
  upperValue: string
  lowerOperator: string
  lowerValue: string
  inspectionType: string
  samplingPlan: string
  sampleCount: string
  photoRequirement: string
  defectLevel: string
}

export interface PatrolStandard {
  id: string
  name: string
  factoryCode: string
  factoryName: string
  workshopCode: string
  lineCode: string
  lineName: string
  materialCode: string
  createdBy: string
  createdTime: string
  updatedBy: string
  updatedTime: string
  inspectionItems: PatrolStandardItem[]
}

export type PatrolStandardDraft = Omit<
  PatrolStandard,
  'id' | 'createdBy' | 'createdTime' | 'updatedBy' | 'updatedTime'
>

export interface MockAuditContext {
  id: string
  user: string
  time: string
}

export function emptyPatrolStandardDraft(): PatrolStandardDraft {
  return {
    name: '',
    factoryCode: '',
    factoryName: '',
    workshopCode: '',
    lineCode: '',
    lineName: '',
    materialCode: '',
    inspectionItems: [],
  }
}

export function emptyPatrolStandardItem(id: string): PatrolStandardItem {
  return {
    id,
    processCode: '',
    processName: '',
    itemCategory: '',
    inspectionItem: '',
    inspectionContent: '',
    upperOperator: '',
    upperValue: '',
    lowerOperator: '',
    lowerValue: '',
    inspectionType: '',
    samplingPlan: '',
    sampleCount: '',
    photoRequirement: '',
    defectLevel: '',
  }
}

export function toPatrolStandardDraft(standard: PatrolStandard): PatrolStandardDraft {
  return {
    name: standard.name,
    factoryCode: standard.factoryCode,
    factoryName: standard.factoryName,
    workshopCode: standard.workshopCode,
    lineCode: standard.lineCode,
    lineName: standard.lineName,
    materialCode: standard.materialCode,
    inspectionItems: standard.inspectionItems.map((item) => ({ ...item })),
  }
}

export function createMockPatrolStandard(
  standards: readonly PatrolStandard[],
  draft: PatrolStandardDraft,
  audit: MockAuditContext,
): PatrolStandard[] {
  return [
    ...standards,
    {
      ...draft,
      inspectionItems: draft.inspectionItems.map((item) => ({ ...item })),
      id: audit.id,
      createdBy: audit.user,
      createdTime: audit.time,
      updatedBy: audit.user,
      updatedTime: audit.time,
    },
  ]
}

export function updateMockPatrolStandard(
  standards: readonly PatrolStandard[],
  id: string,
  draft: PatrolStandardDraft,
  audit: Omit<MockAuditContext, 'id'>,
): PatrolStandard[] {
  return standards.map((standard) => standard.id === id
    ? {
        ...standard,
        ...draft,
        inspectionItems: draft.inspectionItems.map((item) => ({ ...item })),
        updatedBy: audit.user,
        updatedTime: audit.time,
      }
    : standard)
}

export function copyMockPatrolStandard(
  source: PatrolStandard,
  createItemId: () => string,
): PatrolStandardDraft {
  const draft = toPatrolStandardDraft(source)
  return {
    ...draft,
    inspectionItems: draft.inspectionItems.map((item) => ({
      ...item,
      id: createItemId(),
    })),
  }
}

export function deleteMockPatrolStandard(
  standards: readonly PatrolStandard[],
  id: string,
): PatrolStandard[] {
  return standards.filter((standard) => standard.id !== id)
}

export function isInspectionItemEmpty(item: PatrolStandardItem): boolean {
  return Object.entries(item).every(([key, value]) => key === 'id' || value.trim() === '')
}
