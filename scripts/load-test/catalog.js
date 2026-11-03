import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';

const baseUrl = __ENV.HR_BASE_URL || 'http://host.docker.internal:18081';
const vuCount = Number(__ENV.VUS || 50);
const duration = __ENV.DURATION || '5m';
const year = __ENV.YEAR || new Date().getUTCFullYear();
const authoritativeReads = __ENV.AUTHORITATIVE === 'true';
const httpErrors = new Counter('catalog_http_errors');
const reportedFailures = new Set();

export const options = {
  scenarios: {
    cached_catalog_reads: {
      executor: 'constant-vus',
      vus: vuCount,
      duration,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
  },
};

const catalogPaths = [
  `/api/v1/leave-types`,
  `/api/v1/leave-policies?year=${year}`,
  `/api/v1/holidays?from=${year}-01-01&to=${year}-12-31`,
];

function getRequestPath(path) {
  if (!authoritativeReads) {
    return path;
  }
  return `${path}${path.includes('?') ? '&' : '?'}authoritative=true`;
}

export function setup() {
  const response = http.post(
    `${baseUrl}/api/v1/auth/login`,
    JSON.stringify({
      email: __ENV.HR_EMAIL,
      password: __ENV.HR_PASSWORD,
    }),
    { headers: { 'Content-Type': 'application/json' } },
  );
  const succeeded = check(response, {
    'HR login succeeds': (result) => result.status === 200,
  });
  if (!succeeded) {
    throw new Error(`HR login failed with HTTP ${response.status}`);
  }

  const payload = response.json();
  if (!payload || typeof payload.accessToken !== 'string') {
    throw new Error('HR login response did not contain an access token');
  }

  for (const path of catalogPaths) {
    const warmup = http.get(`${baseUrl}${getRequestPath(path)}`, {
      headers: { Authorization: `Bearer ${payload.accessToken}` },
      tags: { endpoint: path.split('?')[0], phase: 'warmup' },
    });
    if (warmup.status !== 200) {
      throw new Error(`Catalog warm-up failed for ${path}: HTTP ${warmup.status}`);
    }
  }
  return { token: payload.accessToken };
}

export default function ({ token }) {
  const path = catalogPaths[(__VU + __ITER) % catalogPaths.length];
  const response = http.get(`${baseUrl}${getRequestPath(path)}`, {
    headers: { Authorization: `Bearer ${token}` },
    tags: { endpoint: path.split('?')[0] },
  });
  if (response.status < 200 || response.status >= 300) {
    httpErrors.add(1);
    if (!reportedFailures.has(`${path}:${response.status}`)) {
      reportedFailures.add(`${path}:${response.status}`);
      console.error(`Catalog request ${path} failed with HTTP ${response.status}: ${response.body?.slice(0, 200)}`);
    }
  }
  check(response, {
    'catalog request succeeds': (result) => result.status === 200,
    'catalog response is JSON': (result) =>
      result.headers['Content-Type']?.includes('application/json'),
  });
  sleep(0.1);
}

export function handleSummary(data) {
  const durationMetrics = data.metrics.http_req_duration?.values || {};
  const requestMetrics = data.metrics.http_reqs?.values || {};
  const failureMetrics = data.metrics.http_req_failed?.values || {};
  const summary = {
    vus: vuCount,
    duration,
    requests: requestMetrics.count || 0,
    rps: requestMetrics.rate || 0,
    p50_ms: durationMetrics.med || 0,
    p95_ms: durationMetrics['p(95)'] || 0,
    error_rate: failureMetrics.rate || 0,
    http_errors: data.metrics.catalog_http_errors?.values?.count || 0,
  };
  const output = JSON.stringify(summary, null, 2);
  if (__ENV.REPORT_JSON) {
    return {
      stdout: output,
      [__ENV.REPORT_JSON]: JSON.stringify(data, null, 2),
    };
  }
  return { stdout: output };
}
