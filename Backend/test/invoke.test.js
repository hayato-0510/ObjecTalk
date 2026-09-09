import assert from 'node:assert/strict';
import test from 'node:test';

import { createToolInvoker } from '../src/tools/invoke.js';

function invoker(overrides = {}) {
  return createToolInvoker({
    getWeather: async ({ location }) => `weather:${location}`,
    webSearch: async ({ query }) => `search:${query}`,
    timeoutMs: 50,
    ...overrides,
  });
}

test('dispatches to the named tool', async () => {
  const invokeTool = invoker();
  assert.equal(await invokeTool('get_weather', { location: '鈴鹿' }), 'weather:鈴鹿');
  assert.equal(await invokeTool('web_search', { query: 'AI' }), 'search:AI');
});

test('parses a JSON string of arguments', async () => {
  const invokeTool = invoker();
  assert.equal(await invokeTool('get_weather', '{"location":"津"}'), 'weather:津');
});

test('unknown tool throws a TypeError', async () => {
  const invokeTool = invoker();
  await assert.rejects(() => invokeTool('delete_everything', {}), TypeError);
});

test('invalid JSON arguments throw a TypeError', async () => {
  const invokeTool = invoker();
  await assert.rejects(() => invokeTool('get_weather', '{not json'), TypeError);
});

test('a throwing tool becomes a soft Japanese string', async () => {
  const invokeTool = invoker({ getWeather: async () => { throw new Error('boom'); } });
  assert.match(await invokeTool('get_weather', { location: 'x' }), /調べられなかった/);
});

test('a slow tool times out with a soft string', async () => {
  const invokeTool = invoker({
    getWeather: () => new Promise((resolve) => setTimeout(() => resolve('late'), 500)),
  });
  assert.match(await invokeTool('get_weather', { location: 'x' }), /時間内に調べられなかった/);
});

test('an empty tool result becomes a soft string', async () => {
  const invokeTool = invoker({ webSearch: async () => '' });
  assert.match(await invokeTool('web_search', { query: 'x' }), /調べられなかった/);
});
