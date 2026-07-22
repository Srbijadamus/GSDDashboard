import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Relative /api calls (see src/api/client.ts) need somewhere to go in dev,
    // since Vite serves the frontend on :5173 while the backend is on :5000.
    proxy: {
      '/api': 'http://localhost:5000',
    },
  },
})
