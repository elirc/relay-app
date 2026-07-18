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
    // On a loaded/shared machine the forks pool can't spin up several workers
    // at once, so cap to a single worker and run files sequentially, with
    // generous timeouts.
    testTimeout: 30000,
    hookTimeout: 30000,
    maxWorkers: 1,
    fileParallelism: false,
  },
});
