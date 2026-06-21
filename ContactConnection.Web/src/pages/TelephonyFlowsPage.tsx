import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { flowsApi, type FlowSummary } from '../api/flows'

export default function TelephonyFlowsPage() {
  const navigate = useNavigate()
  const [flows, setFlows] = useState<FlowSummary[]>([])
  const [loading, setLoading] = useState(true)

  const load = () => {
    setLoading(true)
    flowsApi.listAllByType('telephony').then((data) => {
      setFlows(data)
      setLoading(false)
    }).catch(() => setLoading(false))
  }

  useEffect(() => { load() }, [])

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this telephony flow?')) return
    await flowsApi.delete(id)
    load()
  }

  const handlePublish = async (id: string) => {
    await flowsApi.publish(id)
    load()
  }

  return (
    <div className="p-8 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-100">Telephony Flows</h1>
          <p className="text-sm text-gray-400 mt-1">Call routing flows executed by ESL for every inbound DID call</p>
        </div>
        <button
          onClick={() => navigate('/telephony-designer')}
          className="bg-blue-600 hover:bg-blue-500 text-white text-sm font-medium rounded px-4 py-2"
        >
          + New Flow
        </button>
      </div>

      {loading ? (
        <p className="text-gray-400">Loading…</p>
      ) : flows.length === 0 ? (
        <div className="text-center py-16 text-gray-500">
          <p className="text-lg mb-2">No telephony flows yet</p>
          <p className="text-sm">Create a flow and assign it to a campaign to enable inbound routing.</p>
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {flows.map((f) => (
            <div
              key={f.id}
              className="bg-gray-800 border border-gray-700 rounded-lg px-5 py-4 flex items-center gap-4"
            >
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <p className="font-semibold text-gray-100 truncate">{f.name}</p>
                  {f.is_active ? (
                    <span className="text-[10px] font-bold bg-green-900 text-green-300 px-2 py-0.5 rounded uppercase">
                      Published
                    </span>
                  ) : (
                    <span className="text-[10px] font-bold bg-gray-700 text-gray-400 px-2 py-0.5 rounded uppercase">
                      Draft
                    </span>
                  )}
                </div>
                <p className="text-xs text-gray-500 mt-0.5">
                  v{f.version} · Updated {new Date(f.updated_at).toLocaleDateString()}
                </p>
              </div>

              <div className="flex items-center gap-2 shrink-0">
                <button
                  onClick={() => navigate(`/telephony-designer/${f.id}`)}
                  className="text-xs bg-gray-700 hover:bg-gray-600 text-gray-200 rounded px-3 py-1.5"
                >
                  Edit
                </button>
                {!f.is_active && (
                  <button
                    onClick={() => handlePublish(f.id)}
                    className="text-xs bg-blue-700 hover:bg-blue-600 text-white rounded px-3 py-1.5"
                  >
                    Publish
                  </button>
                )}
                <button
                  onClick={() => handleDelete(f.id)}
                  className="text-xs bg-red-900 hover:bg-red-800 text-red-300 rounded px-3 py-1.5"
                >
                  Delete
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
