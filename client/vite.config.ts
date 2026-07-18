/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    // Generous timeouts + a single fork: on a loaded/shared machine the forks
    // pool can't spin up parallel workers fast enough, so run files in one
    // sequential worker for reliability.
    testTimeout: 20000,
    hookTimeout: 20000,
    pool: 'forks',
    poolOptions: { forks: { singleFork: true } },
  },
});
