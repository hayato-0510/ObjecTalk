export function createOpenAIRealtimeCall({ apiKey, model, voice, fetchImpl = fetch }) {
  if (!apiKey) throw new Error('OpenAI API key is required');
  return async function createRealtimeCall({ sdp, instructions, voice: selectedVoice = voice, signal }) {
    const form = new FormData();
    form.append('sdp', sdp);
    form.append('session', JSON.stringify({
      type: 'realtime',
      model,
      output_modalities: ['audio'],
      instructions,
      audio: {
        input: {
          // Raised thresholds because the client has no acoustic echo cancellation: the assistant's
          // own voice, played through the device speaker, leaks back into the microphone. A higher
          // detection threshold ignores that attenuated leakage, and a longer required silence keeps
          // the fading tail of a reply from being taken as the user starting to talk.
          turn_detection: {
            type: 'server_vad',
            threshold: 0.8,
            prefix_padding_ms: 300,
            silence_duration_ms: 1000,
          },
        },
        output: { voice: selectedVoice },
      },
    }));
    const response = await fetchImpl('https://api.openai.com/v1/realtime/calls', {
      method: 'POST',
      headers: { Authorization: `Bearer ${apiKey}` },
      body: form,
      signal,
    });
    if (!response.ok) {
      const detail = await response.text().catch(() => '');
      console.error('[TalkDiag] OpenAI', response.status, detail.slice(0, 800));
      const failure = new Error('OpenAI Realtime call failed');
      if (isQuotaExhausted(response.status, detail)) {
        failure.code = 'insufficient_quota';
      }
      throw failure;
    }
    return response.text();
  };
}

// A 429 from OpenAI can be a transient rate limit (retry helps) or an exhausted credit balance
// (retrying is pointless and just burns time). Only the latter carries insufficient_quota /
// credit_balance_exhausted, so the caller can turn it into an immediate, non-retryable failure.
function isQuotaExhausted(status, body) {
  if (status !== 429 || typeof body !== 'string' || body === '') return false;
  try {
    const error = JSON.parse(body)?.error;
    return error?.type === 'insufficient_quota' || error?.code === 'credit_balance_exhausted';
  } catch {
    return /insufficient_quota|credit_balance_exhausted/.test(body);
  }
}
