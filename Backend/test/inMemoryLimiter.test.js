import assert from 'node:assert/strict';
import test from 'node:test';
import { createInMemoryLimiter } from '../src/rateLimit/inMemoryLimiter.js';

test('limits each uid and IP key and resets at the window boundary', () => {
  let now = 1000;
  const allow = createInMemoryLimiter({ limit: 2, windowMs: 100, now: () => now });
  assert.equal(allow('u1', 'ip1'), true);
  assert.equal(allow('u1', 'ip1'), true);
  assert.equal(allow('u1', 'ip1'), false);
  assert.equal(allow('u1', 'ip2'), true);
  assert.equal(allow('u2', 'ip1'), true);
  now += 100;
  assert.equal(allow('u1', 'ip1'), true);
});
