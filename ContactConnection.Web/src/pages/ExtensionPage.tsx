import { ExtensionExplainer, ExtensionInstallButton } from '../components/ExtensionInfo'

/** /extension — the ContactConnection Agent extension: what it does, and getting it (S183). Linked from every "not installed" note. */
export default function ExtensionPage() {
  return (
    <div className="min-h-screen bg-gray-950 flex flex-col">
      <div className="flex items-stretch bg-gray-900 border-b border-gray-800 shrink-0">
        <img src="/cc-navbar-dark.svg" alt="Contact Connection" className="shrink-0 block" />
      </div>
      <div className="flex-1 flex items-start justify-center p-6">
        <div className="w-full max-w-2xl bg-gray-900 border border-gray-800 rounded-xl p-6 mt-6">
          <div className="flex items-center gap-3 mb-4">
            <img src="/cc-favicon.svg" alt="" className="w-10 h-10" />
            <div>
              <h1 className="text-white text-xl font-semibold">ContactConnection Agent</h1>
              <p className="text-gray-500 text-sm">Browser extension for Chrome and Edge</p>
            </div>
          </div>
          <ExtensionExplainer forAdmins />
          <div className="mt-6 pt-4 border-t border-gray-800"><ExtensionInstallButton /></div>
        </div>
      </div>
    </div>
  )
}
