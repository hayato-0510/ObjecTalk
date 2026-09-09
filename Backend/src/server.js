import { initializeApp, applicationDefault } from 'firebase-admin/app';
import { createApp } from './app.js';
import { createFirebaseTokenVerifier } from './auth/firebaseAuth.js';
import { loadEnvironment } from './config/environment.js';
import { createDailyBudget } from './rateLimit/dailyBudget.js';
import { createOpenAIRealtimeCall } from './realtime/createRealtimeCall.js';
import { toolDefinitions } from './tools/registry.js';
import { createWeatherTool } from './tools/weather.js';
import { createWebSearchTool } from './tools/webSearch.js';
import { createToolInvoker } from './tools/invoke.js';

const config = loadEnvironment();
initializeApp({ credential: applicationDefault(), projectId: config.firebaseProjectId });

const invokeTool = createToolInvoker({
  getWeather: createWeatherTool(),
  webSearch: createWebSearchTool({ apiKey: config.openAiApiKey, model: config.webSearchModel }),
  timeoutMs: config.toolInvokeTimeoutMs,
});

const app = createApp({
  verifyIdToken: createFirebaseTokenVerifier(),
  voicePools: config.voicePools,
  invokeTool,
  dailyBudget: createDailyBudget({
    maxMinutes: config.exhibitionDailyBudgetMinutes,
    minutesPerSession: config.exhibitionMinutesPerSession,
  }),
  createRealtimeCall: createOpenAIRealtimeCall({
    apiKey: config.openAiApiKey,
    model: config.model,
    voice: config.voice,
    tools: toolDefinitions,
  }),
});
app.listen(config.port, () => console.log(`ObjecTalk backend listening on port ${config.port}`));
