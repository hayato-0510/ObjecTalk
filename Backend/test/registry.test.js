import assert from 'node:assert/strict';
import test from 'node:test';

import { toolDefinitions, toolNames } from '../src/tools/registry.js';

test('every definition is a function tool with a name, description and object schema', () => {
  for (const tool of toolDefinitions) {
    assert.equal(tool.type, 'function');
    assert.ok(tool.name && typeof tool.name === 'string');
    assert.ok(tool.description && typeof tool.description === 'string');
    assert.equal(tool.parameters.type, 'object');
    assert.ok(Array.isArray(tool.parameters.required));
  }
});

test('v1 exposes exactly get_weather and web_search', () => {
  assert.deepEqual([...toolNames].sort(), ['get_weather', 'web_search']);
});

test('toolNames matches the definitions', () => {
  assert.deepEqual([...toolNames].sort(), toolDefinitions.map((t) => t.name).sort());
});
