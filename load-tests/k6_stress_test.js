import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';

// ── HU-033 / T-071: Métricas personalizadas y configuración de umbrales ──────────
const evaluationOverhead = new Trend('evaluation_overhead_ms', true);

export const options = {
  stages: [
    { duration: '30s', target: 20 },  // Calentamiento inicial
    { duration: '1m',  target: 50 },  // Carga media sostenida
    { duration: '3m',  target: 100 }, // Carga objetivo: 100 VUs sostenidos (T-071)
    { duration: '30s', target: 0 },   // Ramp-down de cierre
  ],
  thresholds: {
    // T-071 / T-072: Requisito no funcional de rendimiento — Overhead <= 50ms (p95)
    evaluation_overhead_ms: ['p(95)<=50'],
    http_req_failed: ['rate<0.05'],
  },
};

// Colección distribuida de IPs sintéticas para simular tráfico realista de usuarios
const SAMPLE_IPS = [
  '190.166.12.45', '190.166.12.46', '190.166.12.47', '190.166.12.48',
  '200.88.8.1',    '200.88.8.2',    '200.88.8.3',    '200.88.8.4',
  '186.120.15.10', '186.120.15.11', '80.30.15.1',   '80.30.15.2'
];

// Colección de User-Agents realistas
const USER_AGENTS = [
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
  'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15',
  'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36',
  'OmakaseMobileClient/1.2.0 (Android 14; Mobile)'
];

// URL objetivo configurable por variable de entorno GATEWAY_URL (Default: http://localhost:5219/httpbin/get)
const TARGET_URL = __ENV.GATEWAY_URL || 'http://localhost:5219/httpbin/get';

// Token JWT suministrable opcionalmente por variable de entorno JWT_TOKEN
const JWT_TOKEN = __ENV.JWT_TOKEN || '';

export default function () {
  const randomIp = SAMPLE_IPS[Math.floor(Math.random() * SAMPLE_IPS.length)];
  const randomUserAgent = USER_AGENTS[Math.floor(Math.random() * USER_AGENTS.length)];

  const headers = {
    'User-Agent': randomUserAgent,
    'X-Forwarded-For': randomIp,
    'Accept': 'application/json',
  };

  if (JWT_TOKEN) {
    headers['Authorization'] = `Bearer ${JWT_TOKEN}`;
  }

  // Registrar respuestas 200, 401, 403 y 429 como respuestas válidas emitidas por el Gateway
  const res = http.get(TARGET_URL, {
    headers,
    responseCallback: http.expectedStatuses(200, 401, 403, 429),
  });

  // Extraer el tiempo de evaluación e intercepción
  evaluationOverhead.add(res.timings.duration);

  // Validar respuestas coherentes emitidas por el motor de seguridad
  check(res, {
    'Gateway respondió con código válido (200, 401, 403 o 429)': (r) =>
      r.status === 200 || r.status === 401 || r.status === 403 || r.status === 429,
  });

  // Pacing simulado entre peticiones (0.1s - 0.2s)
  sleep(0.1 + Math.random() * 0.1);
}
