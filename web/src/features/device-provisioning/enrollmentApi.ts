import { request, type Device, type Page } from './api'

export type EnrollmentState = 'Invited' | 'Challenged' | 'PendingApproval' | 'Approved' | 'Issuing' | 'IssuedUnconfirmed' | 'Cancelled' | 'Expired'
export interface Candidate {
  id: string; issuanceOperationId: string; state: 'PendingIssuance' | 'OutcomeUnknown' | 'IssuedUnconfirmed' | 'Revoked' | 'Failed'
  bindingVersion: number; certificateSha256: string | null; certificatePem: string | null; rootCertificatePem: string
  notBeforeUtc: string; notAfterUtc: string; externalRevocationPending: boolean; lastErrorCode: string | null; canExecuteLineShared: false
}
export interface Enrollment {
  id: string; deviceId: string; lineId: string; sourceBindingVersion: number; targetBindingVersion: number
  environment: string; revision: number; state: EnrollmentState; expiresAtUtc: string; csrSha256: string | null; spkiSha256: string | null
  approvedByUserId: string | null; approvedAtUtc: string | null; terminalReason: string | null; candidate: Candidate | null; canExecuteLineShared: false
}
export interface Invitation { enrollment: Enrollment; invitationSecret: string; invitationExpiresAtUtc: string }
export interface Approval {
  expectedRevision: number; expectedDeviceRevision: number; lineId: string; bindingVersion: number; csrSha256: string; spkiSha256: string; operationId: string
}
export const enrollmentApi = {
  list: (deviceId: string, page = 1) => request<Page<Enrollment>>(`/devices/${deviceId}/enrollments?page=${page}&pageSize=20`),
  invite: (device: Device) => request<Invitation>(`/devices/${device.id}/enrollments`, 'POST', { expectedDeviceRevision: device.revision }),
  approve: (id: string, input: Approval) => request<Enrollment>(`/enrollments/${id}/approve`, 'POST', input),
  cancel: (id: string, expectedRevision: number, reason: string) => request<Enrollment>(`/enrollments/${id}/cancel`, 'POST', { expectedRevision, reason }),
}
