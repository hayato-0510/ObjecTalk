import assert from 'node:assert/strict';
import test from 'node:test';

import { createDailyBudget } from '../src/rateLimit/dailyBudget.js';

test('with no cap configured it always allows', () => {
  const budget = createDailyBudget();
  for (let i = 0; i < 1000; i += 1) assert.equal(budget.tryConsume(), true);
  assert.equal(budget.remainingMinutes(), Infinity);
});

test('stops accepting sessions once the day is spent', () => {
  const budget = createDailyBudget({ maxMinutes: 10, minutesPerSession: 3 });
  assert.equal(budget.tryConsume(), true); // 3
  assert.equal(budget.tryConsume(), true); // 6
  assert.equal(budget.tryConsume(), true); // 9
  assert.equal(budget.tryConsume(), false); // would be 12 > 10
  assert.equal(budget.remainingMinutes(), 1);
});

test('the tally resets when the local date changes', () => {
  let today = new Date('2026-11-01T12:00:00');
  const budget = createDailyBudget({ maxMinutes: 3, minutesPerSession: 3, now: () => today });
  assert.equal(budget.tryConsume(), true);
  assert.equal(budget.tryConsume(), false);

  today = new Date('2026-11-02T09:00:00');
  assert.equal(budget.tryConsume(), true);
});

test('rejects invalid configuration', () => {
  assert.throws(() => createDailyBudget({ maxMinutes: 0 }), /maxMinutes/);
  assert.throws(() => createDailyBudget({ maxMinutes: -5 }), /maxMinutes/);
  assert.throws(() => createDailyBudget({ minutesPerSession: 0 }), /minutesPerSession/);
});
