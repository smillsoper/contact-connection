import { createContext, useContext } from 'react'

/**
 * Where a widget gets its data (S181). Internal dashboards leave this unset and widgets call the internal API with their own
 * filters; the client portal sets it per widget to a fetch of `/client-portal/dashboards/{d}/widgets/{w}/data`, which the
 * server answers from the saved config inside the dashboard's locked scope.
 */
export const WidgetDataSourceContext = createContext<(() => Promise<unknown>) | null>(null)

/** The client-portal fetch for this widget, or the given internal one. */
export function useWidgetFetch<T>(internal: () => Promise<T>): () => Promise<T> {
  const source = useContext(WidgetDataSourceContext)
  return source ? (source as () => Promise<T>) : internal
}
