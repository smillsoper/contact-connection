import { api } from './client'

export interface CustomFieldDefinition {
  id: string
  tenantId: string
  clientId: string | null    // null = tenant-wide
  campaignId: string | null  // null = client-wide or tenant-wide
  fieldName: string
  displayLabel: string
  dataTypeName: string
  isRequired: boolean
  validationRules: string | null
  displayOrder: number
  isActive: boolean
}

export interface DataType {
  typeName: string
  clrType: string
  postgresType: string
  displayFormat: string | null
  isAggregatable: boolean
  aggregationFunctions: string[]
}

export interface CreateCustomFieldDefinitionRequest {
  fieldName: string
  displayLabel: string
  dataTypeName: string
  isRequired?: boolean
  displayOrder?: number
  clientId?: string | null
  campaignId?: string | null
  validationRules?: string | null
}

export interface UpdateCustomFieldDefinitionRequest {
  displayLabel?: string
  displayOrder?: number
  isRequired?: boolean
  validationRules?: string | null
  isActive?: boolean
}

export const customFieldsApi = {
  listDataTypes: () => api.get<DataType[]>('/api/v1/data-types'),

  listDefinitions: () => api.get<CustomFieldDefinition[]>('/api/v1/custom-field-definitions'),

  getDefinition: (id: string) => api.get<CustomFieldDefinition>(`/api/v1/custom-field-definitions/${id}`),

  createDefinition: (body: CreateCustomFieldDefinitionRequest) =>
    api.post<CustomFieldDefinition>('/api/v1/custom-field-definitions', body),

  // No delete endpoint exists — definitions are deactivated (isActive: false) via PATCH, never
  // hard-deleted, since deleting one would cascade-delete every call's stored value for it.
  updateDefinition: (id: string, body: UpdateCustomFieldDefinitionRequest) =>
    api.patch<CustomFieldDefinition>(`/api/v1/custom-field-definitions/${id}`, body),
}
