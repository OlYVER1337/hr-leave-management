import http from 'k6/http';
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';

// SQL seed, verifier, constant-vus và arrival-rate cùng đọc một cấu hình.
export const approvalConfig = JSON.parse(open('/scripts/approval-benchmark.config.json'));

const seed = approvalConfig.seed;
const workload = approvalConfig.workload;
const hrBaseUrl = __ENV.HR_BASE_URL || 'http://host.docker.internal:18081';
export const leaveBaseUrl = __ENV.LEAVE_BASE_URL || 'http://host.docker.internal:18082';
const pageSize = 100;

function requirePositiveInteger(value, name) {
  if (!Number.isSafeInteger(value) || value < 1) {
    throw new Error(name + ' must be a positive integer.');
  }
}

requirePositiveInteger(seed.employeeCount, 'seed.employeeCount');
requirePositiveInteger(seed.requestsPerEmployee, 'seed.requestsPerEmployee');
requirePositiveInteger(seed.managerCount, 'seed.managerCount');
requirePositiveInteger(workload.durationSeconds, 'workload.durationSeconds');

if (seed.employeeCount < 1000 || seed.employeeCount > 5000) {
  throw new Error('seed.employeeCount must be between 1000 and 5000.');
}
if (seed.requestsPerEmployee > 20) {
  throw new Error('seed.requestsPerEmployee must not exceed 20.');
}
if (seed.managerCount > 200) {
  throw new Error('seed.managerCount must not exceed 200.');
}
if (![25, 50].includes(seed.pendingPercent)) {
  throw new Error('seed.pendingPercent must be 25 or 50.');
}
if (seed.pendingPercent === 50 && seed.requestsPerEmployee !== 20) {
  throw new Error('pendingPercent=50 requires requestsPerEmployee=20.');
}
if (workload.durationSeconds < 60) {
  throw new Error('workload.durationSeconds must be at least 60 seconds.');
}

export const approvalMetrics = {
  requests: new Counter('approval_requests'),
  successes: new Counter('approval_successes'),
  errors: new Counter('approval_http_errors'),
  duration: new Trend('approval_request_duration_ms'),
  leaveToHrCalls: new Counter('approval_leave_to_hr_calls'),
  leaveToHrDuration: new Trend('approval_leave_to_hr_call_duration_ms'),
  leaveToHrTotalDuration: new Counter('approval_leave_to_hr_total_duration_ms'),
};

function getHeader(response, name) {
  const target = name.toLowerCase();
  for (const key of Object.keys(response.headers)) {
    if (key.toLowerCase() === target) {
      return String(response.headers[key]);
    }
  }
  return null;
}

function managerEmail(managerNumber) {
  if (seed.managerCount === 1) {
    return __ENV.MANAGER_EMAIL;
  }

  return 'loadtest.manager.' + String(managerNumber).padStart(4, '0') + '@example.invalid';
}

function loginManager(email) {
  if (!__ENV.MANAGER_PASSWORD) {
    throw new Error('MANAGER_PASSWORD must be provided by the load-test Compose environment.');
  }

  const response = http.post(
    hrBaseUrl + '/api/v1/auth/login',
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
    throw new Error('Manager login failed with HTTP ' + response.status);
  }

  const payload = response.json();
  if (!payload || typeof payload.accessToken !== 'string') {
    throw new Error('Manager login response did not contain an access token.');
  }

  return payload.accessToken;
}

function getPendingLoadTestIds(token, managerNumber, requiredCount) {
  const ids = [];
  const seenIds = new Set();
  let page = 1;
  let totalCount = null;

  while (ids.length < requiredCount) {
    const url =
      leaveBaseUrl +
      '/api/v1/leave-requests/pending-approval?page=' +
      page +
      '&pageSize=' +
      pageSize +
      '&search=' +
      encodeURIComponent('LOADTEST:');

    const response = http.get(url, {
      headers: { Authorization: 'Bearer ' + token },
      tags: { phase: 'setup', endpoint: 'pending_approval_setup' },
    });

    if (response.status !== 200) {
      throw new Error(
        'Manager ' + managerNumber + ' pending inbox page ' + page +
        ' failed with HTTP ' + response.status + '.',
      );
    }

    if (page === 1) {
      const totalHeader = getHeader(response, 'X-Total-Count');
      const parsedTotal = totalHeader === null ? Number.NaN : Number(totalHeader);
      if (!Number.isSafeInteger(parsedTotal) || parsedTotal < 0) {
        throw new Error(
          'Manager ' + managerNumber + ' pending inbox did not return a valid X-Total-Count header.',
        );
      }

      totalCount = parsedTotal;
      if (totalCount < requiredCount) {
        throw new Error(
          'Manager ' + managerNumber + ' has only ' + totalCount +
          ' pending LOADTEST requests; ' + requiredCount + ' unique requests are required.',
        );
      }
    }

    const rows = response.json();
    if (!Array.isArray(rows)) {
      throw new Error(
        'Manager ' + managerNumber + ' pending inbox page ' + page + ' did not return an array.',
      );
    }

    for (const request of rows) {
      if (
        typeof request?.id === 'string' &&
        String(request.reason || '').startsWith('LOADTEST:') &&
        !seenIds.has(request.id)
      ) {
        seenIds.add(request.id);
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
      'Manager ' + managerNumber + ' yielded ' + selectedIds.length +
      ' unique LOADTEST requests; ' + requiredCount + ' are required.',
    );
  }

  return selectedIds;
}

// requiredCounts xác định chính xác số ID phải cấp cho từng Manager.
export function loadManagerPools(requiredCounts) {
  if (!Array.isArray(requiredCounts) || requiredCounts.length !== seed.managerCount) {
    throw new Error('requiredCounts must contain one entry per configured Manager.');
  }

  const managers = [];
  for (let index = 0; index < seed.managerCount; index += 1) {
    const managerNumber = index + 1;
    const requiredCount = requiredCounts[index];

    if (!Number.isSafeInteger(requiredCount) || requiredCount < 0) {
      throw new Error('Invalid request-ID capacity for Manager ' + managerNumber + '.');
    }

    if (requiredCount === 0) {
      managers.push({ token: null, ids: [] });
      continue;
    }

    const token = loginManager(managerEmail(managerNumber));
    const ids = getPendingLoadTestIds(token, managerNumber, requiredCount);
    managers.push({ token, ids });
  }

  return managers;
}

export function recordHrCalls(response) {
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

  approvalMetrics.leaveToHrCalls.add(callCount);
  for (const durationMs of durations) {
    approvalMetrics.leaveToHrDuration.add(durationMs);
    approvalMetrics.leaveToHrTotalDuration.add(durationMs);
  }
}

export function writeApprovalSummary(data, run) {
  // Không ghi setup_data (bao gồm access token) vào raw report.
  delete data.setup_data;

  const durationMetrics = data.metrics.approval_request_duration_ms?.values || {};
  const requestMetrics = data.metrics.approval_requests?.values || {};
  const hrMetrics = data.metrics.approval_leave_to_hr_call_duration_ms?.values || {};
  const attempts = requestMetrics.count || 0;
  const errors = data.metrics.approval_http_errors?.values?.count || 0;
  const droppedIterations = data.metrics.dropped_iterations?.values?.count || 0;
  const k6Iterations = data.metrics.iterations?.values?.count || 0;

  const summary = {
    workload: run.workload,
    executor: run.executor,
    git_sha: __ENV.GIT_SHA || 'not-provided',
    hr_image_id: __ENV.HR_IMAGE_ID || 'not-provided',
    leave_image_id: __ENV.LEAVE_IMAGE_ID || 'not-provided',
    vus: run.vus || run.maxVUs,
    duration_seconds: workload.durationSeconds,
    test_run_duration_ms: data.state?.testRunDurationMs || 0,
    manager_count: seed.managerCount,
    seeded_employees: seed.employeeCount,
    requests_per_employee: seed.requestsPerEmployee,
    seeded_requests: seed.employeeCount * seed.requestsPerEmployee,
    seeded_pending_percent: seed.pendingPercent,
    seeded_pending_requests:
      seed.employeeCount * seed.requestsPerEmployee * seed.pendingPercent / 100,
    expected_pending_per_manager: run.expectedPendingPerManager,
    approval_attempts: attempts,
    completed_approvals: data.metrics.approval_successes?.values?.count || 0,
    approval_attempt_rate: attempts / workload.durationSeconds,
    approval_success_rate:
      (data.metrics.approval_successes?.values?.count || 0) / workload.durationSeconds,
    p50_ms: durationMetrics.med || 0,
    p95_ms: durationMetrics['p(95)'] || 0,
    error_rate: attempts > 0 ? errors / attempts : 0,
    approval_errors: errors,
    dropped_iterations: droppedIterations,
    k6_iterations: k6Iterations,
    leave_to_hr_calls: data.metrics.approval_leave_to_hr_calls?.values?.count || 0,
    leave_to_hr_total_time_ms:
      data.metrics.approval_leave_to_hr_total_duration_ms?.values?.count || 0,
    leave_to_hr_avg_ms: hrMetrics.avg || 0,
    leave_to_hr_p95_ms: hrMetrics['p(95)'] || 0,
  };

  if (run.executor === 'constant-arrival-rate') {
    summary.target_approval_rate_per_second = run.ratePerSecond;
    summary.planned_iterations = run.plannedIterations;
    summary.completed_iterations = k6Iterations;
    summary.approval_http_attempts = attempts;
    summary.dropped_iteration_rate =
      run.plannedIterations > 0 ? droppedIterations / run.plannedIterations : 0;
    summary.pre_allocated_vus = run.preAllocatedVUs;
    summary.max_vus = run.maxVUs;
  } else {
    summary.max_approvals_per_vu = run.maxApprovalsPerVu;
    summary.approval_id_capacity = run.vus * run.maxApprovalsPerVu;
    summary.think_time_seconds = run.thinkTimeSeconds;
  }

  const summaryOutput = JSON.stringify(summary, null, 2);
  const outputs = { stdout: summaryOutput };

  if (__ENV.REPORT_JSON) {
    outputs[__ENV.REPORT_JSON] = JSON.stringify(data, null, 2);
  }
  if (__ENV.REPORT_SUMMARY_JSON) {
    outputs[__ENV.REPORT_SUMMARY_JSON] = summaryOutput;
  }

  return outputs;
}
