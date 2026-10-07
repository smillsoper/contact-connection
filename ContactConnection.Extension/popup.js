// Status only — the extension has no settings; the agent portal drives it.
chrome.storage.session.get('ccState').then(({ ccState }) => {
  const s = ccState ?? { portalTabIds: [], capturing: false }
  document.getElementById('tabs').textContent = s.portalTabIds.length ? String(s.portalTabIds.length) : 'none open'
  document.getElementById('rec').textContent = s.capturing ? 'on (a call is being recorded)' : 'off'
})
document.getElementById('ver').textContent = 'Version ' + chrome.runtime.getManifest().version
