// Tool schemas handed to the Realtime session (session.tools). The model picks a tool from these;
// the client forwards the call to POST /api/tools/invoke, which dispatches through invoke.js.

export const toolDefinitions = Object.freeze([
  {
    type: 'function',
    name: 'get_weather',
    description: '指定した場所の天気予報を調べる。傘や上着が必要かなど、天気に関わる質問のときに使う。',
    parameters: {
      type: 'object',
      properties: {
        location: { type: 'string', description: '天気を知りたい場所の名前（市区町村名など）。' },
        date: { type: 'string', description: '"today" / "tomorrow" / "YYYY-MM-DD"。省略時は今日。' },
      },
      required: ['location'],
    },
  },
  {
    type: 'function',
    name: 'web_search',
    description: '最新のニュースや、その場では分からない事実をインターネットで調べる。',
    parameters: {
      type: 'object',
      properties: {
        query: { type: 'string', description: '調べたい内容を短い日本語で。' },
      },
      required: ['query'],
    },
  },
]);

export const toolNames = new Set(toolDefinitions.map((tool) => tool.name));
