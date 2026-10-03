import type { PatrolStandard } from '@/features/patrol-standards/model'

export const standards: PatrolStandard[] = [
  {
    id: 'std-001', name: 'Module Assembly Line A Patrol', factoryCode: 'JAX-01', factoryName: 'Jacksonville Plant', workshopCode: 'MOD', lineCode: 'LN-A', lineName: 'Module Line A', materialCode: 'MOD-72C', createdBy: 'A. Morgan', createdTime: '2026-09-12 08:40', updatedBy: 'P. Shah', updatedTime: '2026-09-28 14:22',
    inspectionItems: [
      { id: 'item-001', processCode: 'EL-01', processName: 'Cell Stringing', itemCategory: 'Workmanship', inspectionItem: 'Cell alignment', inspectionContent: 'Verify cell spacing and string alignment', upperOperator: '', upperValue: '', lowerOperator: '', lowerValue: '', inspectionType: 'Visual', samplingPlan: '5 modules / shift', sampleCount: '5', photoRequirement: 'On abnormal', defectLevel: 'Major' },
      { id: 'item-002', processCode: 'FR-03', processName: 'Framing', itemCategory: 'Dimension', inspectionItem: 'Frame diagonal', inspectionContent: 'Measure both frame diagonals', upperOperator: '≤', upperValue: '2', lowerOperator: '≥', lowerValue: '0', inspectionType: 'Numeric', samplingPlan: '3 modules / shift', sampleCount: '3', photoRequirement: 'Not required', defectLevel: 'Major' },
    ],
  },
  {
    id: 'std-002', name: 'Lamination Process Patrol', factoryCode: 'JAX-01', factoryName: 'Jacksonville Plant', workshopCode: 'MOD', lineCode: 'LN-B', lineName: 'Module Line B', materialCode: 'MOD-72C', createdBy: 'P. Shah', createdTime: '2026-09-15 10:12', updatedBy: 'P. Shah', updatedTime: '2026-09-26 09:03',
    inspectionItems: [
      { id: 'item-003', processCode: 'LM-02', processName: 'Lamination', itemCategory: 'Process parameter', inspectionItem: 'Temperature', inspectionContent: 'Verify actual cycle temperature', upperOperator: '≤', upperValue: '155', lowerOperator: '≥', lowerValue: '145', inspectionType: 'Numeric', samplingPlan: '5 modules / shift', sampleCount: '5', photoRequirement: 'On abnormal', defectLevel: 'Critical' },
    ],
  },
  {
    id: 'std-003', name: 'Final Visual Inspection Patrol', factoryCode: 'JAX-01', factoryName: 'Jacksonville Plant', workshopCode: 'QA', lineCode: 'LN-C', lineName: 'Module Line C', materialCode: 'MOD-54C', createdBy: 'A. Morgan', createdTime: '2026-09-18 13:05', updatedBy: 'A. Morgan', updatedTime: '2026-09-29 16:35',
    inspectionItems: [
      { id: 'item-004', processCode: 'FI-01', processName: 'Final Inspection', itemCategory: 'Appearance', inspectionItem: 'Surface condition', inspectionContent: 'Check glass and frame for visible defects', upperOperator: '', upperValue: '', lowerOperator: '', lowerValue: '', inspectionType: 'Visual', samplingPlan: '100%', sampleCount: '1', photoRequirement: 'On abnormal', defectLevel: 'Minor' },
    ],
  },
]

export const plans = [
  { id: 'plan-001', enabled: true, start: '2026-10-01', end: '2026-12-31', schedule: 'Every 4 hours', no: 'PLN-2026-041', name: 'Line A Routine Patrol', standard: standards[0].name, factory: 'JAX-01', line: 'LN-A', assigned: 'Jordan Lee' },
  { id: 'plan-002', enabled: true, start: '2026-10-01', end: '2026-11-30', schedule: 'Every shift', no: 'PLN-2026-042', name: 'Lamination Shift Patrol', standard: standards[1].name, factory: 'JAX-01', line: 'LN-B', assigned: 'Casey Chen' },
  { id: 'plan-003', enabled: false, start: '2026-10-15', end: '2027-01-15', schedule: 'Daily · 08:00', no: 'PLN-2026-043', name: 'Final Inspection Daily', standard: standards[2].name, factory: 'JAX-01', line: 'LN-C', assigned: 'Taylor Kim' },
]

export const tasks = [
  { id: 'task-001', status: 'pendingApproval', result: 'pass', generated: '2026-10-01 06:00', no: 'PT-261001-001', plan: plans[0].name, standard: standards[0].name, factory: 'JAX-01', line: 'LN-A', inspector: 'Jordan Lee', submitted: '2026-10-01 08:26' },
  { id: 'task-002', status: 'pendingApproval', result: 'attention', generated: '2026-10-01 07:00', no: 'PT-261001-002', plan: plans[1].name, standard: standards[1].name, factory: 'JAX-01', line: 'LN-B', inspector: 'Casey Chen', submitted: '2026-10-01 09:14' },
]

export const records = [
  { id: 'record-001', taskNo: 'PT-260930-018', completed: '2026-09-30 18:42', result: 'pass', inspector: 'Jordan Lee', standard: standards[0].name, plan: plans[0].name, factory: 'JAX-01', line: 'LN-A', planNo: plans[0].no, approver: 'Priya Shah', approval: '2026-09-30 18:42', shift: 'B', created: '2026-09-30 14:00' },
  { id: 'record-002', taskNo: 'PT-260930-019', completed: '2026-09-30 20:17', result: 'pass', inspector: 'Casey Chen', standard: standards[1].name, plan: plans[1].name, factory: 'JAX-01', line: 'LN-B', planNo: plans[1].no, approver: 'Priya Shah', approval: '2026-09-30 20:17', shift: 'B', created: '2026-09-30 15:00' },
  { id: 'record-003', taskNo: 'PT-261001-003', completed: '2026-10-01 05:55', result: 'attention', inspector: 'Taylor Kim', standard: standards[2].name, plan: plans[2].name, factory: 'JAX-01', line: 'LN-C', planNo: plans[2].no, approver: 'Alex Morgan', approval: '2026-10-01 05:55', shift: 'C', created: '2026-10-01 02:00' },
]

export const users = [
  { id: 'user-001', username: 'AXM-1001', name: 'Alex Morgan', role: 'admin', active: true, created: '2026-08-20 09:00' },
  { id: 'user-002', username: 'PQS-2042', name: 'Priya Shah', role: 'pqe', active: true, created: '2026-08-21 09:00' },
  { id: 'user-003', username: 'JDL-3107', name: 'Jordan Lee', role: 'ipqa', active: true, created: '2026-08-22 09:00' },
  { id: 'user-004', username: 'TKS-3111', name: 'Taylor Kim', role: 'ipqa', active: false, created: '2026-08-24 09:00' },
]

export const inspectionDetail = {
  id: 'detail-001', item: 'Lamination temperature', process: 'Lamination · LM-02', type: 'Numeric', upper: '155 °C', lower: '145 °C', sampling: '5 modules / shift', count: '5', date: '2026-09-30', time: '17:50', result: 'Pass', reinspection: '—', abnormalCode: '—', abnormalType: '—', abnormalReason: '—', abnormalDescription: '—', audit: 'Submitted 18:31 · Approved 18:42',
}
