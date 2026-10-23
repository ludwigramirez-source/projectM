// Aplica el tema antes del primer pintado (evita parpadeo). Ver src/app/theme.tsx.
// Va en un archivo (no inline) para que la Content-Security-Policy pueda usar script-src 'self'.
try {
  var t = localStorage.getItem('pm-theme')
  if (t !== 'light' && t !== 'dark') t = matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
  document.documentElement.dataset.theme = t
} catch {
  document.documentElement.dataset.theme = 'light'
}
