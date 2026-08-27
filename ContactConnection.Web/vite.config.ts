import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig(({ mode }) => ({
  plugins: [react(), tailwindcss()],
  // react-grid-layout (via react-draggable) references process.env.NODE_ENV directly —
  // a leftover CommonJS/webpack convention. Vite doesn't polyfill Node's `process` global
  // in the browser, so without this define it throws "process is not defined" at runtime.
  define: {
    'process.env.NODE_ENV': JSON.stringify(mode),
  },
  server: {
    port: 5173,
    host: true,
    allowedHosts: ['.cc.local', '.contactconnection.cc', '.contactconnection.io'],
    proxy: {
      '/api': {
        target: 'http://localhost:5135',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'ws://localhost:5135',
        ws: true,
        changeOrigin: true,
      },
    },
  },
}))
