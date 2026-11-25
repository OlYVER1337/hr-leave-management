import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const hrBaseUrl = __ENV.HR_BASE_URL || 'http://host.docker.internal:18081';
const leaveBaseUrl = __ENV.LEAVE_BASE_URL || 'http://host.docker.internal:18082';

const vuCount = Number(__ENV.VUS || 50);
const duration = __ENV.DURATION || '5m';
const managerCount = Number(__ENV.LOADTEST_MANAGER_COUNT || 25);
const maxApprovalsPerVu = Number(__ENV.MAX_APPROVALS_PER_VU || 400);
const thinkTimeSeconds = Number(__ENV.THINK_TIME_SECONDS || 1);
const pageSize = 100;

if (!Number.isSafeInteger(vuCount) || vuCount < 1) {
  throw new Error('VUS must be a positive integer.');
}
if (!Number.isSafeInteger(managerCount) || managerCount < 1 || managerCount > 200) {
  throw new Error('LOADTEST_MANAGER_COUNT must be an integer between 1 and 200.');
}
if (!Number.isSafeInteger(maxApprovalsPerVu) || maxApprovalsPerVu < 1) {
  throw new Error('MAX_APPROVALS_PER_VU must be a positive integer.');
}
if (!Number.isFinite(thinkTimeSeconds) || thinkTimeSeconds < 1) {
  throw new Error('THINK_TIME_SECONDS must be at least 1 to preserve the seeded request capacity.');
}

const approvalRequests = new Counter('approval_requests');
const approvalSuccesses = new Counter('approval_successes');
const approvalErrors = new Counter('approval_http_errors');
const approvalDuration = new Trend('approval_request_duration_ms');
const leaveToHrCalls = new Counter('approval_leave_to_hr_calls');
const leaveToHrDuration = new Trend('approval_leave_to_hr_call_duration_ms');
const leaveToHrTotalDuration = new Counter('approval_leave_to_hr_total_duration_ms');
function durationToSeconds(value) {
  const pattern = /(\d+(?:\.\d+)?)(ms|s|m|h)/g;
  const factors = { ms: 0.001, s: 1, m: 60, h: 3600 };

  let total = 0;
  let matched = '';
  let match;

  while ((match = pattern.exec(value)) !== null) {
    matched += match[0];
    total += Number(match[1]) * factors[match[2]];
  }

  if (matched !== value || total <= 0) {
    throw new Error(`Invalid DURATION: ${value}`);
  }

  return total;
}
export const options = {
  scenarios: {
    manager_decisions_sustained: {
      executor: 'constant-vus',
      vus: vuCount,
      duration,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],
    approval_http_errors: ['count==0'],
  },
};

function managerEmail(number) {
  if (managerCount === 1) {
    return __ENV.MANAGER_EMAIL;
  }

  return `loadtest.manager.${String(number).padStart(4, '0')}@example.invalid`;
}

function getHeader(response, name) {
  const target = name.toLowerCase();
  for (const key of Object.keys(response.headers)) {
    if (key.toLowerCase() === target) {
      return String(response.headers[key]);
    }
  }
  return null;
}

function loginManager(email) {
  const response = http.post(
    `${hrBaseUrl}/api/v1/auth/login`,
    JSON.stringify({
      email,
      password: __ENV.MANAGER_PASSWORD,
    }),
    {
      headers: { 'Content-Type': 'application/json' },
      tags: { phase: 'setup', endpoint: 'manager_login' },
    },
  );

  if (!check(response, { 'Manager login succeeds': (result) => result.status === 200 })) {
    throw new Error(`Manager login failed with HTTP ${response.status}`);
  }

  const payload = response.json();
  if (!payload || typeof payload.accessToken !== 'string') {
    throw new Error('Manager login response did not contain an access token.');
  }

  return payload.accessToken;
}

function getPendingLoadTestIds(token, managerNumber, requiredCount) {
  const ids = [];
  let page = 1;
  let totalCount = null;

  while (ids.length < requiredCount) {
    const url =
      `${leaveBaseUrl}/api/v1/leave-requests/pending-approval` +
      `?page=${page}&pageSize=${pageSize}&search=${encodeURIComponent('LOADTEST:')}`;

    const response = http.get(url, {
      headers: { Authorization: `Bearer ${token}` },
      tags: { phase: 'setup', endpoint: 'pending_approval_setup' },
    });

    if (response.status !== 200) {
      throw new Error(
        `Manager ${managerNumber} pending inbox page ${page} failed with HTTP ${response.status}.`,
      );
    }

    if (page === 1) {
      const totalHeader = getHeader(response, 'X-Total-Count');
      const parsedTotal = totalHeader === null ? Number.NaN : Number(totalHeader);
      if (!Number.isSafeInteger(parsedTotal) || parsedTotal < 0) {
        throw new Error(
          `Manager ${managerNumber} pending inbox did not return a valid X-Total-Count header.`,
        );
      }

      totalCount = parsedTotal;
      if (totalCount < requiredCount) {
        throw new Error(
          `Manager ${managerNumber}: only ${totalCount} pending LOADTEST requests; ${requiredCount} unique requests required for the assigned VUs.`,
        );
      }
    }

    const rows = response.json();
    if (!Array.isArray(rows)) {
      throw new Error(`Manager ${managerNumber} pending inbox page ${page} did not return an array.`);
    }

    for (const request of rows) {
      if (
        typeof request?.id === 'string' &&
        String(request.reason || '').startsWith('LOADTEST:')
      ) {
        ids.push(request.id);
      }
    }

    const totalPages = Math.ceil(totalCount / pageSize);
    if (ids.length >= requiredCount || rows.length === 0 || page >= totalPages) {
      break;
    }

    page += 1;
  }

  const selectedIds = ids.slice(0, requiredCount);
  if (selectedIds.length < requiredCount) {
    throw new Error(
      `Manager ${managerNumber}: fetched ${selectedIds.length} unique LOADTEST requests; ${requiredCount} required.`,
    );
  }

  if (new Set(selectedIds).size !== selectedIds.length) {
    throw new Error(`Manager ${managerNumber} pending inbox returned duplicate request IDs.`);
  }

  return selectedIds;
}

export function setup() {
  // Phân VU round-robin theo Manager, giống cách phân công request ID ở default().
  const managerVuCounts = Array(managerCount).fill(0);
  for (let vu = 1; vu <= vuCount; vu += 1) {
    managerVuCounts[(vu - 1) % managerCount] += 1;
  }

  const managers = [];
  for (let number = 1; number <= managerCount; number += 1) {
    const assignedVus = managerVuCounts[number - 1];
    if (assignedVus === 0) {
      managers.push({ token: null, ids: [] });
      continue;
    }

    const token = loginManager(managerEmail(number));
    const requiredCount = assignedVus * maxApprovalsPerVu;
    const ids = getPendingLoadTestIds(token, number, requiredCount);
    managers.push({ token, ids });
  }

  return { managers };
}

function recordHrCalls(response) {
  const countHeader = getHeader(response, 'X-HR-Call-Count');
  if (countHeader === null) {
    return;
  }

  const durationHeader = getHeader(response, 'X-HR-Call-Durations-Ms') || '';
  const durations = durationHeader
    .split(',')
    .map((value) => value.trim())
    .filter((value) => value !== '')
    .map((value) => Number(value))
    .filter((value) => Number.isFinite(value) && value >= 0);

  const normalizedCount = countHeader.trim();
  const declaredCallCount = normalizedCount === '' ? Number.NaN : Number(normalizedCount);
  const callCount = Number.isInteger(declaredCallCount) && declaredCallCount >= 0
    ? declaredCallCount
    : durations.length;

  leaveToHrCalls.add(callCount);
  for (const durationMs of durations) {
    leaveToHrDuration.add(durationMs);
    leaveToHrTotalDuration.add(durationMs);
  }
}

export default function (data) {
  const managerIndex = (__VU - 1) % managerCount;
  const perManagerVuIndex = Math.floor((__VU - 1) / managerCount);
  const manager = data.managers[managerIndex];
  const requestIndex = (perManagerVuIndex * maxApprovalsPerVu) + __ITER;

  if (!manager?.token || !manager.ids[requestIndex]) {
    throw new Error(
      `VU ${__VU} exhausted its preallocated unique approval IDs at iteration ${__ITER}. Increase the seed capacity before running a longer or faster test.`,
    );
  }

  const requestId = manager.ids[requestIndex];
  const response = http.patch(
    `${leaveBaseUrl}/api/v1/leave-requests/${requestId}/decision`,
    JSON.stringify({ decision: 'approve', note: 'Load-test sustained approval' }),
    {
      headers: {
        Authorization: `Bearer ${manager.token}`,
        'Content-Type': 'application/json',
      },
      tags: {
  name: 'PATCH /api/v1/leave-requests/:id/decision',
  endpoint: 'leave_decision',
  workload: 'approval_sustained',
},
    },
  );

  approvalRequests.add(1);
  approvalDuration.add(response.timings.duration);
  recordHrCalls(response);

  if (response.status !== 200) {
    approvalErrors.add(1);
    console.error(
      `Approval request ${requestId} failed with HTTP ${response.status}: ${String(response.body || '').slice(0, 200)}`,
    );
  } else {
    approvalSuccesses.add(1);
  }

  check(response, { 'approval succeeds': (result) => result.status === 200 });
  sleep(thinkTimeSeconds);
}

export function handleSummary(data) {
  // Không ghi setup_data (bao gồm access token) vào raw report.
  delete data.setup_data;

  const durationMetrics = data.metrics.approval_request_duration_ms?.values || {};
  const requestMetrics = data.metrics.approval_requests?.values || {};
  const hrMetrics = data.metrics.approval_leave_to_hr_call_duration_ms?.values || {};
  const attempts = requestMetrics.count || 0;
  const errors = data.metrics.approval_http_errors?.values?.count || 0;

  const summary = {
    workload: 'manager-approval-and-hr-lookup-sustained',
    executor: 'constant-vus',
    git_sha: __ENV.GIT_SHA || 'not-provided',
    hr_image_id: __ENV.HR_IMAGE_ID || 'not-provided',
    leave_image_id: __ENV.LEAVE_IMAGE_ID || 'not-provided',
    vus: vuCount,
    duration,
    test_run_duration_ms: data.state?.testRunDurationMs || 0,
    think_time_seconds: thinkTimeSeconds,
    manager_count: managerCount,
    seeded_employees: Number(__ENV.SEED_EMPLOYEE_COUNT || 4000),
    requests_per_employee: Number(__ENV.REQUESTS_PER_EMPLOYEE || 20),
    seeded_requests: Number(__ENV.SEED_EMPLOYEE_COUNT || 4000) * Number(__ENV.REQUESTS_PER_EMPLOYEE || 20),
    seeded_pending_percent: Number(__ENV.SEED_PENDING_PERCENT || 25),
    seeded_pending_requests:
      Number(__ENV.SEED_EMPLOYEE_COUNT || 4000) *
      Number(__ENV.REQUESTS_PER_EMPLOYEE || 20) *
      Number(__ENV.SEED_PENDING_PERCENT || 25) / 100,
    max_approvals_per_vu: maxApprovalsPerVu,
    approval_id_capacity: vuCount * maxApprovalsPerVu,
    approval_attempts: attempts,
    approval_rps: attempts / durationToSeconds(duration),
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
