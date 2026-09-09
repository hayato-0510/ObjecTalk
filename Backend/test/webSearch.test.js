import assert from 'node:assert/strict';
import test from 'node:test';

import { createWebSearchTool } from '../src/tools/webSearch.js';

function stubFetch(response) {
  const calls = [];
  const impl = async (url, options) => {
    calls.push({ url, options });
    if (response instanceof Error) throw response;
    return response;
  };
  impl.calls = calls;
  return impl;
}

test('sends a web_search request and returns the output text', async () => {
  const fetchImpl = stubFetch({
    ok: true,
    json: async () => ({ output_text: '最新のAIニュースは○○です。' }),
  });
  const webSearch = createWebSearchTool({ apiKey: 'sk-test', fetchImpl });

  const result = await webSearch({ query: '今日のAIニュース' });

  assert.equal(result, '最新のAIニュースは○○です。');
  const { url, options } = fetchImpl.calls[0];
  assert.match(String(url), /\/v1\/responses$/);
  assert.equal(options.headers.Authorization, 'Bearer sk-test');
  const body = JSON.parse(options.body);
  assert.deepEqual(body.tools, [{ type: 'web_search' }]);
  assert.match(body.input, /今日のAIニュース/);
});

test('falls back to the structured output array when output_text is absent', async () => {
  const fetchImpl = stubFetch({
    ok: true,
    json: async () => ({
      output: [
        { type: 'web_search_call' },
        { type: 'message', content: [{ type: 'output_text', text: '答えは42。' }] },
      ],
    }),
  });
  const webSearch = createWebSearchTool({ apiKey: 'sk-test', fetchImpl });

  assert.equal(await webSearch({ query: 'x' }), '答えは42。');
});

test('reports a soft failure on an empty response', async () => {
  const webSearch = createWebSearchTool({
    apiKey: 'sk-test',
    fetchImpl: stubFetch({ ok: true, json: async () => ({}) }),
  });

  assert.match(await webSearch({ query: 'x' }), /調べられなかった/);
});

test('reports a soft failure on a non-ok response', async () => {
  const webSearch = createWebSearchTool({
    apiKey: 'sk-test',
    fetchImpl: stubFetch({ ok: false, status: 500, text: async () => 'boom' }),
  });

  assert.match(await webSearch({ query: 'x' }), /問題が起きた|調べられなかった/);
});

test('reports a soft failure when fetch throws', async () => {
  const webSearch = createWebSearchTool({
    apiKey: 'sk-test',
    fetchImpl: stubFetch(new Error('offline')),
  });

  assert.match(await webSearch({ query: 'x' }), /問題が起きた|調べられなかった/);
});
