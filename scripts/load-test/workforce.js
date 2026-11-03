import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';

const hrBaseUrl = __ENV.HR_BASE_URL || 'http://host.docker.internal:18081';
const leaveBaseUrl = __ENV.LEAVE_BASE_URL || 'http://host.docker.internal:18082';
const vuCount = Number(__ENV.VUS || 50);
const duration = __ENV.DURATION || '5m';
const year = Number(__ENV.YEAR || new Date().getUTCFullYear());
const httpErrors = new Counter('workforce_http_errors');
const reportedFailures = new Set();
const authorizationHeader = (token) => ['Bearer', token].join(' ');

export const options = {
  scenarios: {
    workforce_reads: {
      executor: 'constant-vus',
      vus: vuCount,
      duration,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],
  },
};

const routes = [
  { name: 'employees', url: `${hrBaseUrl}/api/v1/employees`, token: 'hr' },
  { name: 'all_leave_requests', url: `${leaveBaseUrl}/api/v1/leave-requests`, token: 'hr' },
  {
    name: 'leave_calendar',
    url: `${leaveBaseUrl}/api/v1/leave-requests/calendar?from=${year}-01-01&to=${year}-12-31`,
    token: 'hr',
  },
  {
    name: 'manager_approvals',
    url: `${leaveBaseUrl}/api/v1/leave-requests/pending-approval`,
    token: 'manager',
  },
];

function login(email, password, label) {
  const response = http.post(
    `${hrBaseUrl}/api/v1/auth/login`,
    JSON.stringify({ email, password }),
    { headers: { 'Content-Type': 'application/json' }, tags: { phase: 'setup', role: label } },
  );
  if (!check(response, { [`${label} login succeeds`]: (result) => result.status === 200 })) {
    throw new Error(`${label} login failed with HTTP ${response.status}`);
  }

  const payload = response.json();
  if (!payload || typeof payload.accessToken !== 'string') {
    throw new Error(`${label} login response did not contain an access token`);
  }
  return payload.accessToken;
}

export function setup() {
  const tokens = {
    hr: login(__ENV.HR_EMAIL, __ENV.HR_PASSWORD, 'HR'),
    manager: login(__ENV.MANAGER_EMAIL, __ENV.MANAGER_PASSWORD, 'Manager'),
  };

  for (const route of routes) {
    const response = http.get(route.url, {
      headers: { Authorization: authorizationHeader(tokens[route.token]) },
      tags: { endpoint: route.name, phase: 'warmup' },
    });
    if (response.status !== 200) {
      throw new Error(`Workforce warm-up failed for ${route.name}: HTTP ${response.status}`);
    }
  }

  return tokens;
}

export default function (tokens) {
  const route = routes[(__VU + __ITER) % routes.length];
  const response = http.get(route.url, {
    headers: { Authorization: authorizationHeader(tokens[route.token]) },
    tags: { endpoint: route.name },
  });
  if (response.status < 200 || response.status >= 300) {
    httpErrors.add(1);
    if (!reportedFailures.has(`${route.name}:${response.status}`)) {
      reportedFailures.add(`${route.name}:${response.status}`);
      console.error(`Workforce request ${route.name} failed with HTTP ${response.status}: ${response.body?.slice(0, 200)}`);
    }
  }
  check(response, {
    'workforce request succeeds': (result) => result.status === 200,
    'workforce response is JSON': (result) =>
      result.headers['Content-Type']?.includes('application/json'),
  });
  sleep(0.1);
}

export function handleSummary(data) {
  const durationMetrics = data.metrics.http_req_duration?.values || {};
  const requestMetrics = data.metrics.http_reqs?.values || {};
  const failureMetrics = data.metrics.http_req_failed?.values || {};
  const summary = {
    workload: 'employee-and-leave-reads',
    vus: vuCount,
    duration,
    year,
    requests: requestMetrics.count || 0,
    rps: requestMetrics.rate || 0,
    p50_ms: durationMetrics.med || 0,
    p95_ms: durationMetrics['p(95)'] || 0,
    error_rate: failureMetrics.rate || 0,
    http_errors: data.metrics.workforce_http_errors?.values?.count || 0,
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
