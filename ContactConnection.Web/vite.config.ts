import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    host: true,
    allowedHosts: ['.cc.local', '.contactconnection.cc'],
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
})
