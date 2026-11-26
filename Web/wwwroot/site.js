window.converterUi = {
  confirm: message => window.confirm(message),
  copy: async text => {
    if (navigator.clipboard && window.isSecureContext) { await navigator.clipboard.writeText(text); return; }
    const el = document.createElement('textarea'); el.value = text; el.style.position = 'fixed'; el.style.opacity = '0'; document.body.append(el); el.select();
    try { if (!document.execCommand('copy')) throw new Error('Clipboard unavailable'); } finally { el.remove(); }
  },
  download: (name, mime, text) => {
    const url = URL.createObjectURL(new Blob([text], {type:mime}));
    const a = document.createElement('a'); a.href = url; a.download = name; document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 30000);
  }
};
window.converterUi.selectRoutine = index => { document.getElementById('routine').value = String(index); };

window.converterUi.selectLanguage = language => { document.getElementById("language").value = language; };

// Theme is local to this browser; no conversion data is stored or transmitted.
(() => {
  const key = 'algorithm-converter-theme';
  const root = document.documentElement;
  let saved = null;
  try { saved = window.localStorage.getItem(key); } catch { /* storage may be blocked */ }
  root.dataset.theme = saved === 'dark' ? 'dark' : 'light';
  window.converterUi.isDarkMode = () => root.dataset.theme === 'dark';
  window.converterUi.toggleTheme = () => {
    const next = root.dataset.theme === 'dark' ? 'light' : 'dark';
    root.dataset.theme = next;
    try { window.localStorage.setItem(key, next); } catch { /* still works for this visit */ }
    return next === 'dark';
  };
})();
