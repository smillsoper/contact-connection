import { createContext, useContext } from 'react'
import type { RecordDetailResponse, RecordsPage, RecordsParams } from '../../api/dashboardWidgets'

/**
 * Where the records widget gets its rows, details and recordings (S181). Unset on internal dashboards (the widget uses the
 * internal API with its own filters); the client portal provides scoped versions for each widget.
 */
export interface RecordsSource {
  query: (p: RecordsParams) => Promise<RecordsPage>
  detail: (callId: string) => Promise<RecordDetailResponse>
  recording: (callId: string) => Promise<Response>
}

export const RecordsSourceContext = createContext<RecordsSource | null>(null)
export const useRecordsSource = () => useContext(RecordsSourceContext)
