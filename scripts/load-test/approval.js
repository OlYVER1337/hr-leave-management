import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const hrBaseUrl = __ENV.HR_BASE_URL || 'http://host.docker.internal:18081';
const leaveBaseUrl = __ENV.LEAVE_BASE_URL || 'http://host.docker.internal:18082';
const vuCount = Number(__ENV.VUS || 50);
const duration = __ENV.DURATION || '5m';
const iterationsPerVu = Number(__ENV.ITERATIONS_PER_VU || 10);

const approvalRequests = new Counter('approval_requests');
const approvalSuccesses = new Counter('approval_successes');
const approvalErrors = new Counter('approval_http_errors');
const approvalDuration = new Trend('approval_request_duration_ms');
const leaveToHrCalls = new Counter('approval_leave_to_hr_calls');
const leaveToHrDuration = new Trend('approval_leave_to_hr_call_duration_ms');
const leaveToHrTotalDuration = new Counter('approval_leave_to_hr_total_duration_ms');

export const options = {
  scenarios: {
    manager_decisions: {
      executor: 'per-vu-iterations',
      vus: vuCount,
      iterations: iterationsPerVu,
      maxDuration: duration,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],
  },
};

function loginManager() {
  const response = http.post(
    `${hrBaseUrl}/api/v1/auth/login`,
    JSON.stringify({
      email: __ENV.MANAGER_EMAIL,
      password: __ENV.MANAGER_PASSWORD,
    }),
    { headers: { 'Content-Type': 'application/json' }, tags: { phase: 'setup' } },
  );
  if (!check(response, { 'Manager login succeeds': (result) => result.status === 200 })) {
    throw new Error(`Manager login failed with HTTP ${response.status}`);
  }
  const payload = response.json();
  if (!payload || typeof payload.accessToken !== 'string') {
    throw new Error('Manager login response did not contain an access token');
  }
  return payload.accessToken;
}

export function setup() {
  const token = loginManager();
  const inbox = http.get(`${leaveBaseUrl}/api/v1/leave-requests/pending-approval`, {
    headers: { Authorization: `Bearer ${token}` },
    tags: { phase: 'setup' },
  });
  if (inbox.status !== 200) {
    throw new Error(`Manager pending inbox failed with HTTP ${inbox.status}`);
  }

  const requests = inbox.json();
  const ids = (Array.isArray(requests) ? requests : [])
    .filter((request) => String(request.reason || '').startsWith('LOADTEST:'))
    .map((request) => request.id);

  const required = vuCount * iterationsPerVu;
  if (ids.length < required) {
    throw new Error(
      `Only ${ids.length} seeded pending LOADTEST requests are available; ${required} are required. Re-run seed-scale-data.sql or lower ITERATIONS_PER_VU.`,
    );
  }

  return { token, ids };
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

export default function ({ token, ids }) {
  const index = ((__VU - 1) * iterationsPerVu) + __ITER;
  const requestId = ids[index];
  const response = http.post(
    `${leaveBaseUrl}/api/v1/leave-requests/${requestId}/approve`,
    JSON.stringify({ note: 'Load-test approval' }),
    {
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      tags: { endpoint: 'approve_leave_request' },
    },
  );

  // Record only decision iterations; login and inbox setup are excluded.
  approvalRequests.add(1);
  approvalDuration.add(response.timings.duration);
  recordHrCalls(response);

  if (response.status !== 200) {
    approvalErrors.add(1);
    console.error(
      `Approval request ${requestId} failed with HTTP ${response.status}: ${response.body?.slice(0, 200)}`,
    );
  } else {
    approvalSuccesses.add(1);
  }
  check(response, { 'approval succeeds': (result) => result.status === 200 });
  sleep(0.05);
}

export function handleSummary(data) {
  const durationMetrics = data.metrics.approval_request_duration_ms?.values || {};
  const hrMetrics = data.metrics.approval_leave_to_hr_call_duration_ms?.values || {};
  const attempts = data.metrics.approval_requests?.values?.count || 0;
  const errors = data.metrics.approval_http_errors?.values?.count || 0;
  const summary = {
    workload: 'manager-approval-and-hr-lookup',
    vus: vuCount,
    iterations_per_vu: iterationsPerVu,
    planned_approvals: vuCount * iterationsPerVu,
    approval_attempts: attempts,
    completed_approvals: data.metrics.approval_successes?.values?.count || 0,
    p50_ms: durationMetrics.med || 0,
    p95_ms: durationMetrics['p(95)'] || 0,
    error_rate: attempts > 0 ? errors / attempts : 0,
    approval_errors: errors,
    leave_to_hr_calls: data.metrics.approval_leave_to_hr_calls?.values?.count || 0,
    leave_to_hr_total_time_ms: data.metrics.approval_leave_to_hr_total_duration_ms?.values?.count || 0,
    leave_to_hr_avg_ms: hrMetrics.avg || 0,
    leave_to_hr_p95_ms: hrMetrics['p(95)'] || 0,
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
