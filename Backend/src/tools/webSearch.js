// web_search tool: OpenAI Responses API with the hosted web_search tool. Keeps everything with one
// vendor (no Brave/Tavily account) at the cost of OpenAI tokens. Returns a short Japanese answer;
// every failure is a soft string, never a throw.

const RESPONSES_URL = 'https://api.openai.com/v1/responses';

function extractText(payload) {
  if (typeof payload?.output_text === 'string' && payload.output_text.trim() !== '') {
    return payload.output_text.trim();
  }
  const parts = [];
  for (const item of Array.isArray(payload?.output) ? payload.output : []) {
    for (const content of Array.isArray(item?.content) ? item.content : []) {
      if (content?.type === 'output_text' && typeof content.text === 'string') {
        parts.push(content.text);
      }
    }
  }
  return parts.join('').trim();
}

export function createWebSearchTool({ apiKey, model = 'gpt-4o-mini', fetchImpl = fetch } = {}) {
  if (!apiKey) throw new Error('OpenAI API key is required for web_search');
  return async function webSearch({ query } = {}) {
    if (typeof query !== 'string' || query.trim() === '') {
      return '何を調べたいか教えてね。';
    }

    let response;
    try {
      response = await fetchImpl(RESPONSES_URL, {
        method: 'POST',
        headers: { Authorization: `Bearer ${apiKey}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          model,
          tools: [{ type: 'web_search' }],
          input: `次の質問に、日本語で2〜3文で簡潔に答えて。最新の情報は調べて。: ${query.trim()}`,
        }),
      });
    } catch (error) {
      console.error('web_search request failed:', error?.message);
      return '調べているときに問題が起きたよ。';
    }

    if (!response.ok) {
      const detail = await response.text?.().catch(() => '') ?? '';
      console.error('web_search non-ok:', response.status, String(detail).slice(0, 300));
      return '調べているときに問題が起きたよ。';
    }

    let text;
    try {
      text = extractText(await response.json());
    } catch (error) {
      console.error('web_search parse failed:', error?.message);
      return 'うまく調べられなかったよ。';
    }
    return text || 'うまく調べられなかったよ。';
  };
}
