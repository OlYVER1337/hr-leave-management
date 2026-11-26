import { check, sleep } from 'k6';
import {
  approvalConfig,
  approvalMetrics,
  loadManagerPools,
  recordHrCalls,
  writeApprovalSummary,
} from './approval-benchmark-common.js';

const config = approvalConfig;
const seed = config.seed;
const workload = config.workload.constantVus;
const vus = Number(__ENV.VUS || workload.defaultVus);
const maxApprovalsPerVu = workload.maxApprovalsPerVu;
const thinkTimeSeconds = workload.thinkTimeSeconds;
const duration = workload.durationSeconds + 's';
const managerCount = seed.managerCount;

if (!Number.isSafeInteger(vus) || vus < 1) {
  throw new Error('VUS must be a positive integer.');
}
if (!Number.isSafeInteger(maxApprovalsPerVu) || maxApprovalsPerVu < 1) {
  throw new Error('constantVus.maxApprovalsPerVu must be a positive integer.');
}
if (!Number.isFinite(thinkTimeSeconds) || thinkTimeSeconds < 1) {
  throw new Error('constantVus.thinkTimeSeconds must be at least 1.');
}

const requiredCounts = Array(managerCount).fill(0);
for (let vu = 1; vu <= vus; vu += 1) {
  requiredCounts[(vu - 1) % managerCount] += maxApprovalsPerVu;
}
const expectedPendingPerManager = Math.max(...requiredCounts);

export const options = {
  setupTimeout: '5m',
  scenarios: {
    manager_decisions_sustained: {
      executor: 'constant-vus',
      vus,
      duration,
      gracefulStop: '30s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],
    approval_http_errors: ['count==0'],
  },
};

export function setup() {
  return { managers: loadManagerPools(requiredCounts) };
}

export default function (data) {
  // Cùng cách chia Manager round-robin với phân bổ ID trong setup().
  const managerIndex = (__VU - 1) % managerCount;
  const managerVuIndex = Math.floor((__VU - 1) / managerCount);
  const manager = data.managers[managerIndex];
  const requestIndex = managerVuIndex * maxApprovalsPerVu + __ITER;
  const requestId = manager?.ids[requestIndex];

  if (!manager?.token || !requestId) {
    throw new Error(
      'VU ' + __VU + ' đã dùng hết Pending ID; tăng seed capacity hoặc giới hạn thời lượng.',
    );
  }

  const response = http.patch(
    'http://leave-service:8080/api/v1/leave-requests/' + requestId + '/decision',
    JSON.stringify({ decision: 'approve', note: 'Load-test constant-vus approval' }),
    {
      headers: {
        Authorization: 'Bearer ' + manager.token,
        'Content-Type': 'application/json',
      },
      tags: {
        name: 'PATCH /api/v1/leave-requests/:id/decision',
        endpoint: 'leave_decision',
        workload: 'approval_constant_vus',
      },
    },
  );

  approvalMetrics.requests.add(1);
  approvalMetrics.duration.add(response.timings.duration);
  recordHrCalls(response);

  if (response.status === 200) {
    approvalMetrics.successes.add(1);
  } else {
    approvalMetrics.errors.add(1);
    console.error(
      'Approval ' + requestId + ' failed with HTTP ' + response.status + ': ' +
      String(response.body || '').slice(0, 200),
    );
  }

  check(response, { 'approval succeeds': (result) => result.status === 200 });
  sleep(thinkTimeSeconds);
}

export function handleSummary(data) {
  return writeApprovalSummary(data, {
    workload: 'manager-approval-and-hr-lookup-sustained',
    executor: 'constant-vus',
    vus,
    maxApprovalsPerVu,
    thinkTimeSeconds,
    expectedPendingPerManager,
  });
}
