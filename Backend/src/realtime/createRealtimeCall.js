export function createOpenAIRealtimeCall({ apiKey, model, voice, fetchImpl = fetch }) {
  if (!apiKey) throw new Error('OpenAI API key is required');
  return async function createRealtimeCall({ sdp, instructions, signal }) {
    const form = new FormData();
    form.append('sdp', sdp);
    form.append('session', JSON.stringify({
      type: 'realtime',
      model,
      output_modalities: ['audio'],
      instructions,
      audio: {
        input: { turn_detection: { type: 'server_vad' } },
        output: { voice },
      },
    }));
    const response = await fetchImpl('https://api.openai.com/v1/realtime/calls', {
      method: 'POST',
      headers: { Authorization: `Bearer ${apiKey}` },
      body: form,
      signal,
    });
    if (!response.ok) throw new Error('OpenAI Realtime call failed');
    return response.text();
  };
}
