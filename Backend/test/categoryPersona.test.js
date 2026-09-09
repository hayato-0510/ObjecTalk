import assert from 'node:assert/strict';
import test from 'node:test';

import { categoryPersona } from '../src/conversation/categoryPersona.js';

const CASES = [
  ['電子機器', /電子機器です/],
  ['飲みもの', /飲みものです/],
  ['傘', /傘です/],
  ['本', /本です/],
  ['文房具', /文房具です/],
  ['アクセサリ', /アクセサリ/],
];

test('each known label returns its persona line', () => {
  for (const [label, pattern] of CASES) {
    assert.match(categoryPersona(label), pattern, label);
  }
});

test('surrounding whitespace is trimmed before lookup', () => {
  assert.equal(categoryPersona('  本  '), categoryPersona('本'));
  assert.notEqual(categoryPersona('本'), '');
});

test('その他 has no persona line', () => {
  assert.equal(categoryPersona('その他'), '');
});

test('unknown, empty and non-string categories return an empty string', () => {
  for (const value of ['すごい未知', '', '   ', null, undefined, 123, {}]) {
    assert.equal(categoryPersona(value), '', JSON.stringify(value));
  }
});
