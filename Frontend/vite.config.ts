import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import { readFileSync, existsSync, copyFileSync, mkdirSync } from 'fs'
import path from 'path'

const pkg = JSON.parse(readFileSync('./package.json', 'utf-8'))

// Frontend/.env is gitignored (see Frontend/.gitignore) so a fresh checkout or
// a secondary deployment copy of this repo never has it. Keep a durable,
// outside-git backup and self-heal from it here so `.env` being absent never
// silently turns into an empty VITE_CARTO_KEY build (2026-10 CARTO watermark
// incident — the live map lost the API key because the frontend was rebuilt
// without Frontend/.env present).
const envPath = path.resolve(process.cwd(), '.env')
const envBackupPath = 'C:\\GSDDashboard-secrets\\Frontend.env'
if (!existsSync(envPath) && existsSync(envBackupPath)) {
  mkdirSync(path.dirname(envPath), { recursive: true })
  copyFileSync(envBackupPath, envPath)
  console.warn(
    `[vite.config] Frontend/.env was missing — restored from backup at ${envBackupPath}. ` +
    'Keep that backup in sync manually if you ever rotate the CARTO key.'
  )
}

// https://vite.dev/config/
export default defineConfig(({ command, mode }) => {
  const env = loadEnv(mode, process.cwd(), 'VITE_')

  // Hard-fail production builds without the CARTO key instead of silently
  // shipping a keyless bundle (CARTO shows a "API KEY REQUIRED" watermark on
  // the map when the tile URL's `key=` param is empty). Dev server (`vite`)
  // only warns, so local development without the key still works.
  if (command === 'build' && !env.VITE_CARTO_KEY) {
    throw new Error(
      '\n\nBUILD ABORTED: VITE_CARTO_KEY is not set.\n' +
      `Expected it in ${envPath} (see Frontend/.env.example for the variable name).\n` +
      `A durable copy is kept outside git at ${envBackupPath} — restore it from there,\n` +
      'or set the real key, then rebuild. Building without it would ship a bundle that\n' +
      'shows the CARTO "API KEY REQUIRED" watermark on the live map.\n'
    )
  }
  if (command === 'serve' && !env.VITE_CARTO_KEY) {
    console.warn('[vite.config] VITE_CARTO_KEY is not set — dev server map tiles will show a watermark.')
  }

  return {
    plugins: [react()],
    define: {
      __APP_VERSION__: JSON.stringify(pkg.version),
    },
    build: {
      // Output directly to the .NET backend's static-file root so the backend
      // always serves the latest bundle without a separate copy step.
      outDir: '../Backend/wwwroot',
      emptyOutDir: true,
    },
    server: {
      // Relative /api calls (see src/api/client.ts) need somewhere to go in dev,
      // since Vite serves the frontend on :5173 while the backend is on :5000.
      proxy: {
        '/api': 'http://localhost:5000',
      },
    },
  }
})
