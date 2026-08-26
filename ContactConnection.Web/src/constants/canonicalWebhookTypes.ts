// Mirrors the backend's curated canonical-webhook-type catalog (CanonicalWebhookType.cs,
// CanonicalWebhookMappingEvaluator.cs) — kept manually in sync, same convention already used for
// API_SUB_TYPES/API_CATEGORIES in constants/apiTypes.ts. Every domain entity mutates only through
// its own named methods (never raw reflection), so match fields and operations are a small,
// curated list per type rather than arbitrary property names.

export const CANONICAL_WEBHOOK_TYPES = [
  { value: 'order', label: 'Order' },
  { value: 'order_line', label: 'Order Line' },
  { value: 'call_record', label: 'Call Record' },
]

export const CANONICAL_WEBHOOK_TYPE_LABELS: Record<string, string> = Object.fromEntries(
  CANONICAL_WEBHOOK_TYPES.map((t) => [t.value, t.label])
)

export interface MatchFieldOption {
  value: string
  label: string
}

/** Root-level match fields — how to find the top-level record this webhook's payload is about. */
export const ROOT_MATCH_FIELDS_BY_TYPE: Record<string, MatchFieldOption[]> = {
  order: [
    { value: 'Id', label: 'Order Id' },
    { value: 'CallRecordId', label: 'Call Record Id' },
  ],
  order_line: [
    { value: 'Id', label: 'Order Line Id' },
  ],
  call_record: [
    { value: 'Id', label: 'Call Record Id' },
    { value: 'ContactIdExternal', label: 'External Contact Id' },
  ],
}

/** Match fields for finding a child OrderLine inside an Order's items array — a separate,
 *  smaller catalog since "Sku" only makes sense once the parent Order is already known (a SKU
 *  alone can't find a specific line). */
export const ORDER_LINE_ITEM_MATCH_FIELDS: MatchFieldOption[] = [
  { value: 'Id', label: 'Order Line Id' },
  { value: 'Sku', label: 'SKU' },
]

export interface OperationParam {
  key: string
  label: string
  /** 'path' = resolved from the payload via a tree-node pick, per incoming webhook.
   *  'customFieldDefinition' = a fixed value chosen once when configuring the mapping (from the
   *  tenant's existing custom field definitions), not resolved per-payload. */
  kind: 'path' | 'customFieldDefinition'
}

export interface OperationOption {
  value: string
  label: string
  params: OperationParam[]
}

export const OPERATIONS_BY_TYPE: Record<string, OperationOption[]> = {
  order: [
    { value: 'Cancel', label: 'Cancel Order', params: [] },
  ],
  order_line: [
    { value: 'Ship', label: 'Ship', params: [{ key: 'trackingNumber', label: 'Tracking Number', kind: 'path' }] },
    { value: 'MarkDelivered', label: 'Mark Delivered', params: [] },
    { value: 'Cancel', label: 'Cancel Line', params: [] },
  ],
  call_record: [
    {
      value: 'SetCustomField', label: 'Set Custom Field', params: [
        { key: 'definitionId', label: 'Custom Field', kind: 'customFieldDefinition' },
        { key: 'valueSourcePath', label: 'Value', kind: 'path' },
      ],
    },
  ],
}

/** When an Order webhook has "multiple line items" (itemsArray) enabled, the operation applies
 *  per matched OrderLine, not to the Order itself — so it picks from order_line's operations. */
export const ORDER_ITEMS_ARRAY_OPERATIONS = OPERATIONS_BY_TYPE.order_line

export const NO_MATCH_POLICIES = [
  { value: 'skip_and_log', label: 'Log an error, do nothing' },
  { value: 'ignore', label: 'Ignore silently' },
]

export const MULTIPLE_MATCH_POLICIES = [
  { value: 'skip_and_log', label: 'Log an error, do nothing' },
  { value: 'update_all', label: 'Update every matching record' },
]
