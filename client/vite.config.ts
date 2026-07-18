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
    // On a loaded/shared machine the forks pool can't spin up many workers at
    // once, so run test files sequentially (one worker spawn at a time) with
    // generous timeouts.
    testTimeout: 20000,
    hookTimeout: 20000,
    fileParallelism: false,
  },
});
