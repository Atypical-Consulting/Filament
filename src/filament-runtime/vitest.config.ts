import { defineConfig } from 'vitest/config';

export default defineConfig({
  define: {
    // Tests run the DEV build: C3 is asserted through __filament.stats, so the
    // counters must exist here. The production bundle defines this to `false`
    // (scripts/build.mjs) and size.mjs proves the counters are then gone.
    __FILAMENT_STATS__: 'true',
  },
  test: {
    globals: true,
    environment: 'happy-dom',
    include: ['test/**/*.test.ts'],
    // --expose-gc backs the heap-growth assertion in c3-counter.test.ts, which is
    // the one piece of C3 evidence that does not come from our own counters.
    pool: 'forks',
    poolOptions: { forks: { execArgv: ['--expose-gc'] } },
  },
});
