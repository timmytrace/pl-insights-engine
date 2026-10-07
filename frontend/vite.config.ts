import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const api = process.env.STUDIO_API ?? 'http://localhost:5080'

// The dev server proxies the API and replay socket, so the app only ever uses relative URLs.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': api,
      '/ws': { target: api, ws: true },
    },
  },
})
