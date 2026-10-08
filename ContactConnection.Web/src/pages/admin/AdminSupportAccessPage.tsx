import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { api } from '../../api/client'

/**
 * Support Access (S184): every time ContactConnection support opened this account's portal — who, why, when and for
 * how long. Support works under its own named account ("… (ContactConnection Support)"), so its changes show who made them.
 */

interface Row { id: string; name: string; reason: string; startedAt: string; endedAt: string | null; active: boolean }

const fmt = (s: string) => new Date(s).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
const minutes = (a: string, b: string) => Math.max(1, Math.round((new Date(b).getTime() - new Date(a).getTime()) / 60000))

export default function AdminSupportAccessPage() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { api.get<Row[]>('/api/v1/admin/support-access').then(setRows).catch((e: Error) => setError(e.message)) }, [])

  return (
    <AdminShell>
      <div className="p-6 max-w-4xl">
        <h1 className="text-white text-xl font-semibold">Support Access</h1>
        <p className="text-gray-500 text-sm mt-0.5 mb-6">
          Every time ContactConnection support signed in to your portal to help — who, why, and for how long. Sessions last
          at most an hour, and changes made during one show the support person's name.
        </p>
        {error && <p className="text-sm text-red-400 mb-4">{error}</p>}
        {!rows && !error && <p className="text-gray-400 text-sm">Loading…</p>}
        {rows?.length === 0 && <p className="text-gray-500 text-sm">ContactConnection support hasn't accessed your portal.</p>}
        {rows && rows.length > 0 && (
          <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">When</th>
                  <th className="px-4 py-3 font-medium">Who</th>
                  <th className="px-4 py-3 font-medium">Reason</th>
                  <th className="px-4 py-3 font-medium">How long</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.id} className="border-b border-gray-800 last:border-0 align-top">
                    <td className="px-4 py-3 text-gray-300 whitespace-nowrap">{fmt(r.startedAt)}</td>
                    <td className="px-4 py-3 text-white whitespace-nowrap">{r.name} <span className="text-gray-500 text-xs">ContactConnection Support</span></td>
                    <td className="px-4 py-3 text-gray-300">{r.reason}</td>
                    <td className="px-4 py-3 whitespace-nowrap">
                      {r.active ? <span className="text-emerald-400">In progress</span>
                        : <span className="text-gray-400">{r.endedAt ? `${minutes(r.startedAt, r.endedAt)} min` : ''}</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </AdminShell>
  )
}
