import http from 'k6/http';
import { check, sleep } from 'k6';

// ── HU-033 / T-071: Pruebas de Estrés con k6 (100 VUs sostenidos durante 5 min) ──────

export const options = {
  stages: [
    { duration: '30s', target: 100 }, // Ramp-up inicial a 100 VUs
    { duration: '5m',  target: 100 }, // Carga objetivo: 100 VUs sostenidos durante 5 minutos (T-071)
    { duration: '30s', target: 0 },   // Ramp-down de cierre
  ],
  thresholds: {
    // Tasa de errores HTTP fallidos (4xx indebidos o 5xx) menor al 5%
    http_req_failed: ['rate<0.05'],
    // Requisito no funcional de rendimiento — Latencia e2e p95
    http_req_duration: ['p(95)<=50'],
  },
};

// Colección distribuida de 12 IPs sintéticas para simular tráfico realista de usuarios
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

// URL objetivo configurable por variable de entorno GATEWAY_URL
const TARGET_URL = __ENV.GATEWAY_URL || 'http://localhost:5219/httpbin/get';

// Token JWT obligatorio: debe proveerse vía la variable de entorno JWT_TOKEN
const JWT_TOKEN = __ENV.JWT_TOKEN || '';

if (!JWT_TOKEN) {
  throw new Error('Error de configuración: JWT_TOKEN es obligatorio para ejecutar las pruebas de carga (T-071). Pase -e JWT_TOKEN="<token>"');
}

export default function () {
  const randomIp = SAMPLE_IPS[Math.floor(Math.random() * SAMPLE_IPS.length)];
  const randomUserAgent = USER_AGENTS[Math.floor(Math.random() * USER_AGENTS.length)];

  const headers = {
    'User-Agent': randomUserAgent,
    'X-Forwarded-For': randomIp,
    'Accept': 'application/json',
    'Authorization': `Bearer ${JWT_TOKEN}`,
  };

  // Marcar respuestas 200, 401 y 403 como estados esperados de evaluación de seguridad
  const res = http.get(TARGET_URL, {
    headers,
    responseCallback: http.expectedStatuses(200, 401, 403),
  });

  // Validar que el Gateway responda con códigos HTTP válidos de evaluación (200, 401 o 403)
  check(res, {
    'Gateway respondió con código válido (200, 401 o 403)': (r) =>
      r.status === 200 || r.status === 401 || r.status === 403,
  });

  // Pacing simulado entre peticiones (0.1s - 0.2s)
  sleep(0.1 + Math.random() * 0.1);
}
