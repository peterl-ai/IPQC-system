export const patrolStandardRequiredFields = ['lineName', 'standardName'] as const

export type PatrolStandardRequiredField = (typeof patrolStandardRequiredFields)[number]

export interface RequiredFieldRule {
  required: true
  whitespace: true
  message: string
}

export function buildPatrolStandardRequiredRules(
  messages: Record<PatrolStandardRequiredField, string>,
): Record<PatrolStandardRequiredField, RequiredFieldRule[]> {
  return {
    lineName: [{ required: true, whitespace: true, message: messages.lineName }],
    standardName: [{ required: true, whitespace: true, message: messages.standardName }],
  }
}

export function isXlsxFileName(fileName: string): boolean {
  return /\.xlsx$/i.test(fileName.trim())
}
