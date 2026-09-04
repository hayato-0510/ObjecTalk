import { initializeApp, applicationDefault } from 'firebase-admin/app';
import { createApp } from './app.js';
import { createFirebaseTokenVerifier } from './auth/firebaseAuth.js';
import { loadEnvironment } from './config/environment.js';
import { createOpenAIRealtimeCall } from './realtime/createRealtimeCall.js';

const config = loadEnvironment();
initializeApp({ credential: applicationDefault(), projectId: config.firebaseProjectId });
const app = createApp({
  verifyIdToken: createFirebaseTokenVerifier(),
  createRealtimeCall: createOpenAIRealtimeCall({
    apiKey: config.openAiApiKey,
    model: config.model,
    voice: config.voice,
  }),
});
app.listen(config.port, () => console.log(`ObjecTalk backend listening on port ${config.port}`));
