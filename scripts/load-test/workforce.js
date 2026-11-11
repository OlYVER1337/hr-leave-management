import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const hrBaseUrl = __ENV.HR_BASE_URL || 'http://host.docker.internal:18081';
const leaveBaseUrl = __ENV.LEAVE_BASE_URL || 'http://host.docker.internal:18082';
const vuCount = Number(__ENV.VUS || 50);
const duration = __ENV.DURATION || '5m';
const year = Number(__ENV.YEAR || new Date().getUTCFullYear());
const httpErrors = new Counter('workforce_http_errors');
const workforceRequests = new Counter('workforce_requests');
const workforceDuration = new Trend('workforce_request_duration_ms');
const leaveApiRequests = new Counter('leave_api_requests');

// Direct requests to HR API.
const hrDirectRequests = new Counter('hr_direct_requests');
const hrDirectDuration = new Trend('hr_direct_duration_ms');
const hrDirectTotalDuration = new Counter('hr_direct_total_duration_ms');

// Outbound HR HTTP calls made internally by Leave Service. The Leave API
// returns their count and individual durations in X-HR-Call-* response headers.
const leaveToHrCalls = new Counter('leave_to_hr_calls');
const leaveToHrDuration = new Trend('leave_to_hr_call_duration_ms');
const leaveToHrTotalDuration = new Counter('leave_to_hr_total_duration_ms');

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

function recordHrCalls(response) {
  const header = response.headers['X-HR-Call-Durations-Ms']
    || response.headers['x-hr-call-durations-ms']
    || '';
  const durations = String(header)
    .split(',')
    .map((value) => Number(value))
    .filter((value) => Number.isFinite(value) && value >= 0);

  const countHeader = response.headers['X-HR-Call-Count']
    || response.headers['x-hr-call-count'];
  const declaredCallCount = Number(countHeader);
  const callCount = countHeader !== undefined && countHeader !== ''
    && Number.isFinite(declaredCallCount) && declaredCallCount >= 0
    ? declaredCallCount
    : durations.length;

  leaveToHrCalls.add(callCount);
  for (const durationMs of durations) {
    leaveToHrDuration.add(durationMs);
    leaveToHrTotalDuration.add(durationMs);
  }
}

export default function (tokens) {
  const route = routes[(__VU + __ITER) % routes.length];
  const response = http.get(route.url, {
    headers: { Authorization: authorizationHeader(tokens[route.token]) },
    tags: { endpoint: route.name },
  });

  // Custom metrics below include only measured iterations, not setup/login/warm-up.
  workforceRequests.add(1);
  workforceDuration.add(response.timings.duration);

  if (route.name === 'employees') {
    hrDirectRequests.add(1);
    hrDirectDuration.add(response.timings.duration);
    hrDirectTotalDuration.add(response.timings.duration);
  } else {
    leaveApiRequests.add(1);
  }

  // Setup/warm-up requests are intentionally excluded: only calls observed
  // during the measured VU workload are added to custom HR metrics.
  recordHrCalls(response);

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
  const durationMetrics = data.metrics.workforce_request_duration_ms?.values || {};
  const hrDirectMetrics = data.metrics.hr_direct_duration_ms?.values || {};
  const leaveToHrMetrics = data.metrics.leave_to_hr_call_duration_ms?.values || {};
  const requests = data.metrics.workforce_requests?.values?.count || 0;
  const leaveRequests = data.metrics.leave_api_requests?.values?.count || 0;
  const errorCount = data.metrics.workforce_http_errors?.values?.count || 0;
  const durationValue = Number.parseFloat(duration);
  const durationUnit = duration.slice(-1);
  const measuredSeconds = durationUnit === 'm'
    ? durationValue * 60
    : durationUnit === 's'
      ? durationValue
      : durationUnit === 'h'
        ? durationValue * 3600
        : 0;
  // For k6 Counter summaries, values.count is the accumulated counter value.
  const leaveToHrCallCount = data.metrics.leave_to_hr_calls?.values?.count || 0;

  const summary = {
    workload: 'employee-and-leave-reads',
    vus: vuCount,
    duration,
    year,
    requests,
    rps: measuredSeconds > 0 ? requests / measuredSeconds : 0,
    p50_ms: durationMetrics.med || 0,
    p95_ms: durationMetrics['p(95)'] || 0,
    error_rate: requests > 0 ? errorCount / requests : 0,
    http_errors: errorCount,
    hr_direct_requests: data.metrics.hr_direct_requests?.values?.count || 0,
    hr_direct_total_time_ms: data.metrics.hr_direct_total_duration_ms?.values?.count || 0,
    hr_direct_avg_ms: hrDirectMetrics.avg || 0,
    hr_direct_p95_ms: hrDirectMetrics['p(95)'] || 0,
    leave_to_hr_calls: leaveToHrCallCount,
    leave_to_hr_calls_per_second: measuredSeconds > 0 ? leaveToHrCallCount / measuredSeconds : 0,
    leave_api_requests: data.metrics.leave_api_requests?.values?.count || 0,
    leave_to_hr_calls_per_leave_request: leaveRequests > 0 ? leaveToHrCallCount / leaveRequests : 0,
    leave_to_hr_total_time_ms: data.metrics.leave_to_hr_total_duration_ms?.values?.count || 0,
    leave_to_hr_avg_ms: leaveToHrMetrics.avg || 0,
    leave_to_hr_p95_ms: leaveToHrMetrics['p(95)'] || 0,
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
