import { useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'

// Public site (S171): the landing page and legal pages on www.contactconnection.io. Carriers and
// reviewers (e.g. STIR/SHAKEN vetting) look here to see who operates the platform.

export const COMPANY = {
  legalName: 'Call Center Solutions, LLC',
  product: 'ContactConnection',
  street: '1674 SE Douglas Ave',
  cityStateZip: 'Roseburg, OR 97470',
  email: 'support@contactconnection.io',
  abuseEmail: 'abuse@contactconnection.io',
}

/** "Sign in": tenants each have their own address (yourcompany.contactconnection.io). */
function SignIn() {
  const [open, setOpen] = useState(false)
  const [workspace, setWorkspace] = useState('')
  const go = () => {
    const sub = workspace.trim().toLowerCase().replace(/[^a-z0-9-]/g, '')
    if (!sub) return
    const host = window.location.hostname
    const base = host.endsWith('contactconnection.cc') ? 'contactconnection.cc' : 'contactconnection.io'
    window.location.href = host === 'localhost' ? `/login?subdomain=${sub}` : `https://${sub}.${base}/login`
  }
  return (
    <div className="relative">
      <button onClick={() => setOpen((v) => !v)}
        className="px-4 py-2 text-sm font-medium text-white bg-sky-600 hover:bg-sky-500 rounded-lg transition-colors">
        Sign in
      </button>
      {open && (
        <div className="absolute right-0 top-full mt-2 w-72 bg-gray-900 border border-gray-700 rounded-xl shadow-2xl p-4 z-50">
          <label className="block text-xs text-gray-400 mb-1">Your company's workspace</label>
          <div className="flex items-center bg-gray-800 border border-gray-600 rounded-lg overflow-hidden">
            <input autoFocus value={workspace} onChange={(e) => setWorkspace(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') go() }}
              className="flex-1 min-w-0 bg-transparent px-3 py-2 text-sm text-white outline-none" placeholder="yourcompany" />
            <span className="pr-3 text-xs text-gray-500">.contactconnection.io</span>
          </div>
          <button onClick={go} className="mt-3 w-full px-4 py-2 text-sm font-medium text-white bg-sky-600 hover:bg-sky-500 rounded-lg">Continue</button>
        </div>
      )}
    </div>
  )
}

export default function PublicLayout({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-gray-950 text-gray-200 flex flex-col">
      <header className="sticky top-0 z-40 bg-gray-950/90 backdrop-blur border-b border-gray-800">
        <div className="max-w-6xl mx-auto px-6 h-16 flex items-center justify-between gap-4">
          <Link to="/" className="flex items-center gap-2 shrink-0">
            <img src="/cc-favicon.svg" alt="" className="h-7 w-7" />
            <span className="text-white font-semibold tracking-tight">Contact<span className="text-sky-400">Connection</span></span>
          </Link>
          <nav className="hidden sm:flex items-center gap-6 text-sm text-gray-400">
            <a href="/#platform" className="hover:text-white">Platform</a>
            <a href="/#about" className="hover:text-white">About</a>
            <a href="/#contact" className="hover:text-white">Contact</a>
          </nav>
          <SignIn />
        </div>
      </header>

      <main className="flex-1">{children}</main>

      <footer className="border-t border-gray-800 mt-16">
        <div className="max-w-6xl mx-auto px-6 py-10 grid gap-8 sm:grid-cols-3 text-sm">
          <div>
            <p className="text-white font-medium">{COMPANY.product}</p>
            <p className="text-gray-500 mt-1">A product of {COMPANY.legalName}</p>
            <p className="text-gray-500">{COMPANY.street}, {COMPANY.cityStateZip}</p>
          </div>
          <div className="space-y-1">
            <p className="text-gray-400 font-medium">Legal</p>
            <Link to="/privacy" className="block text-gray-500 hover:text-white">Privacy Policy</Link>
            <Link to="/terms" className="block text-gray-500 hover:text-white">Terms of Service</Link>
            <Link to="/acceptable-use" className="block text-gray-500 hover:text-white">Acceptable Use Policy</Link>
          </div>
          <div className="space-y-1">
            <p className="text-gray-400 font-medium">Contact</p>
            <a href={`mailto:${COMPANY.email}`} className="block text-gray-500 hover:text-white">{COMPANY.email}</a>
            <p className="text-gray-500">Report abuse: {COMPANY.abuseEmail}</p>
          </div>
        </div>
        <p className="text-center text-xs text-gray-600 pb-8">© {new Date().getFullYear()} {COMPANY.legalName}. All rights reserved.</p>
      </footer>
    </div>
  )
}
