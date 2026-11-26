import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';
import {
  approvalConfig,
  approvalMetrics,
  leaveBaseUrl,
  loadManagerPools,
  recordHrCalls,
  writeApprovalSummary,
} from './approval-benchmark-common.js';

const config = approvalConfig;
const seed = config.seed;
const workload = config.workload;
const arrival = workload.constantArrivalRate;
const ratePerSecond = arrival.ratePerSecond;
const timeUnit = arrival.timeUnit || '1s';
const durationSeconds = workload.durationSeconds;
const duration = durationSeconds + 's';
const plannedIterations = ratePerSecond * durationSeconds;
const preAllocatedVUs = Number(__ENV.PREALLOCATED_VUS || arrival.defaultPreAllocatedVUs);
const maxVUs = Number(__ENV.MAX_VUS || arrival.maxVUs || preAllocatedVUs);
const managerCount = seed.managerCount;

if (!Number.isSafeInteger(ratePerSecond) || ratePerSecond < 1) {
  throw new Error('constantArrivalRate.ratePerSecond must be a positive integer.');
}
if (timeUnit !== '1s') {
  throw new Error('This workload currently requires timeUnit=1s so capacity can be calculated as rate x durationSeconds.');
}
if (!Number.isSafeInteger(plannedIterations) || plannedIterations < 1) {
  throw new Error('ratePerSecond x durationSeconds must be a positive safe integer.');
}
if (!Number.isSafeInteger(preAllocatedVUs) || preAllocatedVUs < 1) {
  throw new Error('PREALLOCATED_VUS must be a positive integer.');
}
if (!Number.isSafeInteger(maxVUs) || maxVUs < preAllocatedVUs) {
  throw new Error('MAX_VUS must be an integer greater than or equal to PREALLOCATED_VUS.');
}

// Phân bổ ID round-robin theo iteration toàn scenario, độc lập với VU thực thi.
const requiredCounts = Array.from(
  { length: managerCount },
  (_, index) => Math.floor(plannedIterations / managerCount) +
    (index < plannedIterations % managerCount ? 1 : 0),
);
const expectedPendingPerManager = Math.max(...requiredCounts);

export const options = {
  setupTimeout: '5m',
  scenarios: {
    manager_decisions_arrival_rate: {
      executor: 'constant-arrival-rate',
      rate: ratePerSecond,
      timeUnit,
      duration,
      preAllocatedVUs,
      maxVUs,
      gracefulStop: '30s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],
    approval_http_errors: ['count==0'],
    dropped_iterations: ['count==0'],
  },
};

export function setup() {
  return { managers: loadManagerPools(requiredCounts) };
}

export default function (data) {
  const iteration = exec.scenario.iterationInTest;
  const managerIndex = iteration % managerCount;
  const requestIndex = Math.floor(iteration / managerCount);
  const manager = data.managers[managerIndex];
  const requestId = manager?.ids[requestIndex];

  if (!manager?.token || !requestId) {
    throw new Error(
      'Không tìm thấy Pending ID duy nhất cho scenario iteration ' + iteration +
      '; kiểm tra cấu hình seed và capacity trước khi chạy.',
    );
  }

  const response = http.patch(
    leaveBaseUrl + '/api/v1/leave-requests/' + requestId + '/decision',
    JSON.stringify({ decision: 'approve', note: 'Load-test constant-arrival-rate approval' }),
    {
      headers: {
        Authorization: 'Bearer ' + manager.token,
        'Content-Type': 'application/json',
      },
      tags: {
        name: 'PATCH /api/v1/leave-requests/:id/decision',
        endpoint: 'leave_decision',
        workload: 'approval_constant_arrival_rate',
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
  // Không sleep(): constant-arrival-rate đã tự điều tiết tốc độ bắt đầu iteration.
}

export function handleSummary(data) {
  return writeApprovalSummary(data, {
    workload: 'manager-approval-and-hr-lookup-constant-arrival-rate',
    executor: 'constant-arrival-rate',
    preAllocatedVUs,
    maxVUs,
    ratePerSecond,
    plannedIterations,
    expectedPendingPerManager,
  });
}
