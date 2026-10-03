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

export type PatrolStandardSummary = Omit<PatrolStandard, 'inspectionItems'>

export type PatrolStandardDraft = Omit<
  PatrolStandard,
  'id' | 'createdBy' | 'createdTime' | 'updatedBy' | 'updatedTime'
>

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

export function isInspectionItemEmpty(item: PatrolStandardItem): boolean {
  return Object.entries(item).every(([key, value]) => key === 'id' || value.trim() === '')
}
