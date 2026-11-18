using LeaveService.Application.Common;
using LeaveService.Application.Dtos;
using LeaveService.Domain.Entities;
using LeaveService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LeaveService.Tests;

public sealed class LeavePersistenceIntegrationTests : IClassFixture<LeaveSqlIntegrationFixture>
{
    private static LeaveAccess ManagerAccess() =>
        new([
            new LeavePermissionGrant("leaves.read", ["Self", "Team"]),
            new LeavePermissionGrant("leaves.decide", ["Team"])
        ]);

    private readonly LeaveSqlIntegrationFixture _fixture;

    public LeavePersistenceIntegrationTests(LeaveSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    // Chạy trên SQL Server thật: gọi hai lần vẫn chỉ có một row cho mỗi leave type/năm.
    [LeaveSqlFact]
    public async Task SynchronizeBalancesAsync_CreatesRowsAndIsIdempotent()
    {
        var employeeId = Guid.NewGuid();
        var entitlements = new[]
        {
            new LeaveBalanceEntitlement(2, 15m),
            new LeaveBalanceEntitlement(3, 10m)
        };

        await using (var context = _fixture.CreateContext())
        {
            var balances = await new LeaveRequestRepository(context).SynchronizeBalancesAsync(
                employeeId, 2027, entitlements);
            Assert.Equal(2, balances.Count);
        }

        await using (var context = _fixture.CreateContext())
        {
            var balances = await new LeaveRequestRepository(context).SynchronizeBalancesAsync(
                employeeId, 2027, entitlements);
            Assert.Equal(2, balances.Count);
            Assert.All(balances, item =>
            {
                Assert.Equal(0m, item.Used);
                Assert.Equal(0m, item.Pending);
            });
        }

        await using var verifyContext = _fixture.CreateContext();
        Assert.Equal(2, await verifyContext.LeaveBalances.CountAsync(
            item => item.EmployeeId == employeeId && item.Year == 2027));
    }

    // Khi quota tăng từ 12 lên 18, chỉ Entitled đổi; Used và Pending phải được giữ nguyên.
    [LeaveSqlFact]
    public async Task SynchronizeBalancesAsync_UpdatesEntitlementAndPreservesUsedAndPending()
    {
        var employeeId = Guid.NewGuid();
        await using (var seedContext = _fixture.CreateContext())
        {
            seedContext.LeaveBalances.Add(new LeaveBalance
            {
                EmployeeId = employeeId,
                LeaveTypeId = 2,
                Year = 2027,
                Entitled = 12m,
                Used = 3m,
                Pending = 2m
            });
            await seedContext.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var result = await new LeaveRequestRepository(context).SynchronizeBalancesAsync(
                employeeId,
                2027,
                [new LeaveBalanceEntitlement(2, 18m)]);
            var balance = Assert.Single(result);
            Assert.Equal(18m, balance.Entitled);
            Assert.Equal(3m, balance.Used);
            Assert.Equal(2m, balance.Pending);
            Assert.Equal(13m, balance.Entitled - balance.Used - balance.Pending);
        }
    }

    // HR hạ PolicyDays dưới Used + Pending: Entitled vẫn giữ lịch sử và Available bằng 0.
    [LeaveSqlFact]
    public async Task SynchronizeBalancesAsync_AcceptsQuotaBelowUsedAndPendingWithoutNegativeAvailable()
    {
        var employeeId = Guid.NewGuid();
        await using (var seedContext = _fixture.CreateContext())
        {
            seedContext.LeaveBalances.Add(new LeaveBalance
            {
                EmployeeId = employeeId,
                LeaveTypeId = 2,
                Year = 2027,
                Entitled = 12m,
                Used = 5m,
                Pending = 3m
            });
            await seedContext.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var result = await new LeaveRequestRepository(context).SynchronizeBalancesAsync(
                employeeId,
                2027,
                [new LeaveBalanceEntitlement(2, 7m, 2027)]);

            var balance = Assert.Single(result);
            Assert.Equal(7m, balance.PolicyDays);
            Assert.Equal(8m, balance.Entitled);
            Assert.Equal(0m, balance.Available);
            Assert.Equal(5m, balance.Used);
            Assert.Equal(3m, balance.Pending);
        }

        await using var verifyContext = _fixture.CreateContext();
        var saved = await verifyContext.LeaveBalances.SingleAsync(
            item => item.EmployeeId == employeeId && item.LeaveTypeId == 2 && item.Year == 2027);
        Assert.Equal(7m, saved.PolicyDays);
        Assert.Equal(8m, saved.Entitled);
        Assert.Equal(0m, saved.Available);
    }

    // Tám tác vụ với DbContext riêng chạy đồng thời; database chỉ được phép có một balance row.
    [LeaveSqlFact]
    public async Task SynchronizeBalancesAsync_ConcurrentCallsCreateOneRowPerLeaveType()
    {
        var employeeId = Guid.NewGuid();
        const int attempts = 8;

        // Mỗi tác vụ tạo DbContext riêng để mô phỏng nhiều request đồng thời, không chia sẻ ChangeTracker.
        async Task SynchronizeAsync()
        {
            await using var context = _fixture.CreateContext();
            await new LeaveRequestRepository(context).SynchronizeBalancesAsync(
                employeeId,
                2027,
                [new LeaveBalanceEntitlement(2, 15m)]);
        }

        await Task.WhenAll(Enumerable.Range(0, attempts).Select(_ => SynchronizeAsync()));

        await using var verifyContext = _fixture.CreateContext();
        Assert.Equal(1, await verifyContext.LeaveBalances.CountAsync(
            item => item.EmployeeId == employeeId && item.LeaveTypeId == 2 && item.Year == 2027));
    }

    // Kiểm tra SQL transaction lưu request, history và tăng Pending cùng nhau; không được chỉ lưu một phần.
    [LeaveSqlFact]
    public async Task CreateAsync_PersistsRequestHistoryAndPendingBalanceAtomically()
    {
        var employeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            EmployeeId = employeeId,
            DepartmentId = 3,
            LeaveTypeId = 2,
            StartDate = new DateOnly(2026, 11, 2),
            EndDate = new DateOnly(2026, 11, 3),
            TotalDays = 2m,
            Reason = "Integration test",
            CreatedAtUtc = DateTime.UtcNow
        };
        await using var context = _fixture.CreateContext();
        var repository = new LeaveRequestRepository(context);
        var created = await repository.CreateAsync(
            request,
            [new AnnualLeaveAllocation(2026, 2m, 3m, 42)],
            new LeaveRequestHistory
            {
                LeaveRequestId = request.Id,
                ActorId = employeeId,
                Action = "Created",
                ToStatus = LeaveRequestStatus.Pending,
                CreatedAtUtc = request.CreatedAtUtc
            });

        Assert.Equal(2m, created.Balances.Single().Pending);
        var savedAllocation = Assert.Single(created.Request.YearAllocations);
        Assert.Equal(42, savedAllocation.PolicyVersionId);
        Assert.Equal(3m, savedAllocation.PolicyDaysAtCreation);
        await using (var historyContext = _fixture.CreateContext())
        {
            Assert.Equal(
                1,
                await historyContext.LeaveRequestHistories.CountAsync(
                    item => item.LeaveRequest!.EmployeeId == employeeId));
        }

        await Assert.ThrowsAsync<LeaveConflictException>(() => repository.CreateAsync(
            new LeaveRequest
            {
                EmployeeId = employeeId,
                DepartmentId = 3,
                LeaveTypeId = 2,
                StartDate = new DateOnly(2026, 11, 3),
                EndDate = new DateOnly(2026, 11, 4),
                TotalDays = 2m,
                Reason = "Overlapping",
                CreatedAtUtc = DateTime.UtcNow
            },
            [new AnnualLeaveAllocation(2026, 2m, 3m)],
            new LeaveRequestHistory
            {
                ActorId = employeeId,
                Action = "Created",
                ToStatus = LeaveRequestStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            }));

        await Assert.ThrowsAsync<LeaveConflictException>(() => repository.CreateAsync(
            new LeaveRequest
            {
                EmployeeId = employeeId,
                DepartmentId = 3,
                LeaveTypeId = 2,
                StartDate = new DateOnly(2026, 11, 5),
                EndDate = new DateOnly(2026, 11, 6),
                TotalDays = 2m,
                Reason = "Exceeds balance",
                CreatedAtUtc = DateTime.UtcNow
            },
            [new AnnualLeaveAllocation(2026, 2m, 3m)],
            new LeaveRequestHistory
            {
                ActorId = employeeId,
                Action = "Created",
                ToStatus = LeaveRequestStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            }));

        await using var verifyContext = _fixture.CreateContext();
        Assert.Equal(1, await verifyContext.LeaveRequests.CountAsync(item => item.EmployeeId == employeeId));
        Assert.Equal(
            1,
            await verifyContext.LeaveRequestHistories.CountAsync(
                item => item.LeaveRequest!.EmployeeId == employeeId));
        var balance = await verifyContext.LeaveBalances.SingleAsync(item => item.EmployeeId == employeeId);
        Assert.Equal(2m, balance.Pending);
        Assert.Equal(3m, balance.Entitled);
    }

    // Nhiều request đồng thời cùng sử dụng balance mới không được làm tổng Pending vượt Entitled.
    [LeaveSqlFact]
    public async Task CreateAsync_ConcurrentRequestsCannotOverdrawNewBalance()
    {
        var employeeId = Guid.NewGuid();

        async Task<bool> TryCreateAsync(DateOnly startDate)
        {
            await using var context = _fixture.CreateContext();
            var request = new LeaveRequest
            {
                EmployeeId = employeeId,
                DepartmentId = 3,
                LeaveTypeId = 2,
                StartDate = startDate,
                EndDate = startDate.AddDays(1),
                TotalDays = 2m,
                Reason = "Concurrent integration test",
                CreatedAtUtc = DateTime.UtcNow
            };
            try
            {
                await new LeaveRequestRepository(context).CreateAsync(
                    request,
                    [new AnnualLeaveAllocation(2026, 2m, 3m)],
                    new LeaveRequestHistory
                    {
                        LeaveRequestId = request.Id,
                        ActorId = employeeId,
                        Action = "Created",
                        ToStatus = LeaveRequestStatus.Pending,
                        CreatedAtUtc = request.CreatedAtUtc
                    });
                return true;
            }
            catch (LeaveConflictException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(
            TryCreateAsync(new DateOnly(2026, 11, 2)),
            TryCreateAsync(new DateOnly(2026, 11, 4)));

        Assert.Single(results, created => created);
        Assert.Single(results, created => !created);

        await using var verifyContext = _fixture.CreateContext();
        var balance = await verifyContext.LeaveBalances.SingleAsync(item => item.EmployeeId == employeeId);
        Assert.Equal(3m, balance.Entitled);
        Assert.Equal(2m, balance.Pending);
        Assert.Equal(1, await verifyContext.LeaveRequests.CountAsync(item => item.EmployeeId == employeeId));
    }

    // Khi tạo đơn với policy mới, cập nhật Entitled trước rồi mới cộng số ngày của đơn vào Pending.
    [LeaveSqlFact]
    public async Task CreateAsync_RefreshesExistingEntitlementAndPreservesHistoricalUsage()
    {
        var employeeId = Guid.NewGuid();
        await using (var seedContext = _fixture.CreateContext())
        {
            seedContext.LeaveBalances.Add(new LeaveBalance
            {
                EmployeeId = employeeId,
                LeaveTypeId = 2,
                Year = 2027,
                Entitled = 12m,
                Used = 3m,
                Pending = 2m
            });
            await seedContext.SaveChangesAsync();
        }

        var request = new LeaveRequest
        {
            EmployeeId = employeeId,
            DepartmentId = 3,
            LeaveTypeId = 2,
            StartDate = new DateOnly(2027, 11, 10),
            EndDate = new DateOnly(2027, 11, 10),
            TotalDays = 1m,
            Reason = "Policy refresh on create",
            CreatedAtUtc = DateTime.UtcNow
        };

        await using (var context = _fixture.CreateContext())
        {
            var result = await new LeaveRequestRepository(context).CreateAsync(
                request,
                [new AnnualLeaveAllocation(2027, 1m, 18m)],
                new LeaveRequestHistory
                {
                    LeaveRequestId = request.Id,
                    ActorId = employeeId,
                    Action = "Created",
                    ToStatus = LeaveRequestStatus.Pending,
                    CreatedAtUtc = request.CreatedAtUtc
                });

            var balance = Assert.Single(result.Balances);
            Assert.Equal(18m, balance.Entitled);
            Assert.Equal(3m, balance.Used);
            Assert.Equal(3m, balance.Pending);
        }
    }

    // PolicyDays giảm xuống dưới Used + Pending thì Available bằng 0; request mới phải rollback vì không còn ngày khả dụng.
    [LeaveSqlFact]
    public async Task CreateAsync_RejectsPolicyBelowUsedAndPendingWithoutPersistingRequest()
    {
        var employeeId = Guid.NewGuid();
        await using (var seedContext = _fixture.CreateContext())
        {
            seedContext.LeaveBalances.Add(new LeaveBalance
            {
                EmployeeId = employeeId,
                LeaveTypeId = 2,
                Year = 2027,
                Entitled = 12m,
                Used = 5m,
                Pending = 3m
            });
            await seedContext.SaveChangesAsync();
        }

        var request = new LeaveRequest
        {
            EmployeeId = employeeId,
            DepartmentId = 3,
            LeaveTypeId = 2,
            StartDate = new DateOnly(2027, 11, 11),
            EndDate = new DateOnly(2027, 11, 11),
            TotalDays = 1m,
            Reason = "Policy reduction conflict",
            CreatedAtUtc = DateTime.UtcNow
        };

        await using (var context = _fixture.CreateContext())
        {
            await Assert.ThrowsAsync<LeaveConflictException>(() =>
                new LeaveRequestRepository(context).CreateAsync(
                    request,
                    [new AnnualLeaveAllocation(2027, 1m, 7m)],
                    new LeaveRequestHistory
                    {
                        LeaveRequestId = request.Id,
                        ActorId = employeeId,
                        Action = "Created",
                        ToStatus = LeaveRequestStatus.Pending,
                        CreatedAtUtc = request.CreatedAtUtc
                    }));
        }

        // Dùng context mới để xác nhận quota và bộ đếm không đổi sau rollback.
        await using var verifyContext = _fixture.CreateContext();
        var balance = await verifyContext.LeaveBalances.SingleAsync(
            item => item.EmployeeId == employeeId && item.LeaveTypeId == 2 && item.Year == 2027);
        Assert.Equal(12m, balance.Entitled);
        Assert.Equal(5m, balance.Used);
        Assert.Equal(3m, balance.Pending);
        Assert.False(await verifyContext.LeaveRequests.AnyAsync(item => item.Id == request.Id));
        Assert.False(await verifyContext.LeaveRequestHistories.AnyAsync(item => item.LeaveRequestId == request.Id));
    }

    // Approve chuyển Pending sang Used; Reject/Cancel giải phóng Pending và mọi thay đổi phải đi cùng history.
    [LeaveSqlFact]
    public async Task TransitionAsync_UpdatesBalancesAndHistoryForApprovalRejectionAndCancellation()
    {
        async Task<LeaveRequest> CreateRequestAsync(Guid employeeId, Guid approverId, int day)
        {
            var request = new LeaveRequest
            {
                EmployeeId = employeeId,
                ApproverId = approverId,
                DepartmentId = 3,
                LeaveTypeId = 2,
                StartDate = new DateOnly(2026, 12, day),
                EndDate = new DateOnly(2026, 12, day + 1),
                TotalDays = 2m,
                Reason = "Decision integration test",
                CreatedAtUtc = DateTime.UtcNow
            };
            await using var context = _fixture.CreateContext();
            await new LeaveRequestRepository(context).CreateAsync(
                request,
                [new AnnualLeaveAllocation(2026, 2m, 5m)],
                new LeaveRequestHistory
                {
                    LeaveRequestId = request.Id,
                    ActorId = employeeId,
                    Action = "Created",
                    ToStatus = LeaveRequestStatus.Pending,
                    CreatedAtUtc = request.CreatedAtUtc
                });
            return request;
        }

        var approvedEmployee = Guid.NewGuid();
        var approvedManager = Guid.NewGuid();
        var approvedRequest = await CreateRequestAsync(approvedEmployee, approvedManager, 1);
        await using (var pendingContext = _fixture.CreateContext())
        {
            var repository = new LeaveRequestRepository(pendingContext);
            var managerQueue = await repository.ListPendingByEmployeesAsync(new[] { approvedEmployee }, default);
            Assert.Contains(managerQueue, item => item.Request.Id == approvedRequest.Id);
            await Assert.ThrowsAsync<LeaveForbiddenException>(() => repository.TransitionAsync(
                approvedRequest.Id,
                Guid.NewGuid(),
                ManagerAccess(),
                LeaveRequestStatus.Approved,
                null,
                DateTime.UtcNow));
        }

        await using (var context = _fixture.CreateContext())
        {
            var result = await new LeaveRequestRepository(context).TransitionAsync(
                approvedRequest.Id,
                approvedManager,
                ManagerAccess(),
                LeaveRequestStatus.Approved,
                "Approved by manager",
                DateTime.UtcNow,
                verifiedApproverId: approvedManager);
            Assert.Equal(LeaveRequestStatus.Approved, result.Request.Status);
            Assert.Equal(0m, result.Balances.Single().Pending);
            Assert.Equal(2m, result.Balances.Single().Used);
        }

        var rejectedEmployee = Guid.NewGuid();
        var rejectedManager = Guid.NewGuid();
        var rejectedRequest = await CreateRequestAsync(rejectedEmployee, rejectedManager, 5);
        await using (var context = _fixture.CreateContext())
        {
            var balance = await context.LeaveBalances.SingleAsync(
                item => item.EmployeeId == rejectedEmployee &&
                        item.LeaveTypeId == 2 &&
                        item.Year == 2026);
            balance.PolicyDays = 12m;
            balance.Used = 6m;
            await context.SaveChangesAsync();

            var result = await new LeaveRequestRepository(context).TransitionAsync(
                rejectedRequest.Id,
                rejectedManager,
                ManagerAccess(),
                LeaveRequestStatus.Rejected,
                "Not enough coverage",
                DateTime.UtcNow,
                verifiedApproverId: rejectedManager,
                currentPolicyDays: [new LeaveBalanceEntitlement(2, 8m, 2026)]);
            var updatedBalance = Assert.Single(result.Balances);
            Assert.Equal(LeaveRequestStatus.Rejected, result.Request.Status);
            Assert.Equal(8m, updatedBalance.PolicyDays);
            Assert.Equal(8m, updatedBalance.Entitled);
            Assert.Equal(2m, updatedBalance.Available);
            Assert.Equal(0m, updatedBalance.Pending);
            Assert.Equal(6m, updatedBalance.Used);
        }

        var cancelledEmployee = Guid.NewGuid();
        var cancelledRequest = await CreateRequestAsync(cancelledEmployee, Guid.NewGuid(), 9);
        await using (var context = _fixture.CreateContext())
        {
            var result = await new LeaveRequestRepository(context).CancelAsync(
                cancelledEmployee,
                cancelledRequest.Id,
                "Changed plans",
                DateTime.UtcNow);
            Assert.Equal(LeaveRequestStatus.Cancelled, result.Request.Status);
            Assert.Equal(0m, result.Balances.Single().Pending);
            Assert.Equal(0m, result.Balances.Single().Used);

            await Assert.ThrowsAsync<LeaveConflictException>(() =>
                new LeaveRequestRepository(context).CancelAsync(
                    cancelledEmployee,
                    cancelledRequest.Id,
                    null,
                    DateTime.UtcNow));
        }

        await using var verifyContext = _fixture.CreateContext();
        var histories = await verifyContext.LeaveRequestHistories
            .Where(item =>
                item.LeaveRequestId == approvedRequest.Id ||
                item.LeaveRequestId == rejectedRequest.Id ||
                item.LeaveRequestId == cancelledRequest.Id)
            .OrderBy(item => item.CreatedAtUtc)
            .ToArrayAsync();
        Assert.Equal(6, histories.Length);
        Assert.Contains(histories, item =>
            item.LeaveRequestId == approvedRequest.Id &&
            item.Action == "Approved" &&
            item.FromStatus == LeaveRequestStatus.Pending &&
            item.ToStatus == LeaveRequestStatus.Approved);
        Assert.Contains(histories, item =>
            item.LeaveRequestId == rejectedRequest.Id &&
            item.Action == "Rejected" &&
            item.ToStatus == LeaveRequestStatus.Rejected);
        Assert.Contains(histories, item =>
            item.LeaveRequestId == cancelledRequest.Id &&
            item.Action == "Cancelled" &&
            item.ActorId == cancelledEmployee);

        var persistedRequests = await verifyContext.LeaveRequests
            .Where(item =>
                item.Id == approvedRequest.Id ||
                item.Id == rejectedRequest.Id ||
                item.Id == cancelledRequest.Id)
            .ToDictionaryAsync(item => item.Id);
        Assert.Equal(LeaveRequestStatus.Approved, persistedRequests[approvedRequest.Id].Status);
        Assert.Equal(LeaveRequestStatus.Rejected, persistedRequests[rejectedRequest.Id].Status);
        Assert.Equal(LeaveRequestStatus.Cancelled, persistedRequests[cancelledRequest.Id].Status);

        var persistedBalances = await verifyContext.LeaveBalances
            .Where(item =>
                item.EmployeeId == approvedEmployee ||
                item.EmployeeId == rejectedEmployee ||
                item.EmployeeId == cancelledEmployee)
            .ToDictionaryAsync(item => item.EmployeeId);
        Assert.Equal(2m, persistedBalances[approvedEmployee].Used);
        Assert.Equal(0m, persistedBalances[approvedEmployee].Pending);
        Assert.Equal(0m, persistedBalances[rejectedEmployee].Used);
        Assert.Equal(0m, persistedBalances[rejectedEmployee].Pending);
        Assert.Equal(0m, persistedBalances[cancelledEmployee].Used);
        Assert.Equal(0m, persistedBalances[cancelledEmployee].Pending);
    }

    // Nhiều request trùng ngày của cùng nhân viên chạy song song: chỉ một request được tạo thành công.
    [LeaveSqlFact]
    public async Task CreateAsync_ManyConcurrentOverlappingRequestsForSameEmployeeCreateExactlyOne()
    {
        var employeeId = Guid.NewGuid();
        const int attempts = 12;

        async Task<bool> TryCreateAsync()
        {
            await using var context = _fixture.CreateContext();
            var request = new LeaveRequest
            {
                EmployeeId = employeeId,
                DepartmentId = 3,
                LeaveTypeId = 2,
                StartDate = new DateOnly(2026, 11, 9),
                EndDate = new DateOnly(2026, 11, 10),
                TotalDays = 2m,
                Reason = "Stress overlap test",
                CreatedAtUtc = DateTime.UtcNow
            };
            try
            {
                await new LeaveRequestRepository(context).CreateAsync(
                    request,
                    [new AnnualLeaveAllocation(2026, 2m, 30m)],
                    new LeaveRequestHistory
                    {
                        LeaveRequestId = request.Id,
                        ActorId = employeeId,
                        Action = "Created",
                        ToStatus = LeaveRequestStatus.Pending,
                        CreatedAtUtc = request.CreatedAtUtc
                    });
                return true;
            }
            catch (LeaveConflictException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, attempts).Select(_ => TryCreateAsync()));

        Assert.Single(results, created => created);

        await using var verifyContext = _fixture.CreateContext();
        var balance = await verifyContext.LeaveBalances.SingleAsync(item => item.EmployeeId == employeeId);
        Assert.Equal(2m, balance.Pending);
        Assert.Equal(1, await verifyContext.LeaveRequests.CountAsync(item => item.EmployeeId == employeeId));
        Assert.Equal(1, await verifyContext.LeaveRequestHistories.CountAsync(
            item => verifyContext.LeaveRequests.Any(r => r.Id == item.LeaveRequestId && r.EmployeeId == employeeId)));
    }

    // Khi quan hệ Manager hiện tại đã đổi, repository dùng approver đã được HR xác minh thay vì snapshot cũ.
    [LeaveSqlFact]
    public async Task TransitionAsync_UsesVerifiedApproverWhenStoredSnapshotIsStale()
    {
        var employeeId = Guid.NewGuid();
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            EmployeeId = employeeId,
            ApproverId = oldManagerId,
            DepartmentId = 3,
            LeaveTypeId = 2,
            StartDate = new DateOnly(2026, 12, 14),
            EndDate = new DateOnly(2026, 12, 15),
            TotalDays = 2m,
            Reason = "Stale approver test",
            CreatedAtUtc = DateTime.UtcNow
        };
        await using (var context = _fixture.CreateContext())
        {
            await new LeaveRequestRepository(context).CreateAsync(
                request,
                [new AnnualLeaveAllocation(2026, 2m, 5m)],
                new LeaveRequestHistory
                {
                    LeaveRequestId = request.Id,
                    ActorId = employeeId,
                    Action = "Created",
                    ToStatus = LeaveRequestStatus.Pending,
                    CreatedAtUtc = request.CreatedAtUtc
                });
        }

        await using (var context = _fixture.CreateContext())
        {
            var result = await new LeaveRequestRepository(context).TransitionAsync(
                request.Id,
                newManagerId,
                ManagerAccess(),
                LeaveRequestStatus.Approved,
                null,
                DateTime.UtcNow,
                verifiedApproverId: newManagerId);
            Assert.Equal(LeaveRequestStatus.Approved, result.Request.Status);
            Assert.Equal(newManagerId, result.Request.ApproverId);
        }
    }

    // Approve và Reject đồng thời trên một request phải chỉ có một chuyển trạng thái thành công.
    [LeaveSqlFact]
    public async Task TransitionAsync_ConcurrentApprovalAndRejectionApplyOnlyOneDecision()
    {
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            EmployeeId = employeeId,
            ApproverId = managerId,
            DepartmentId = 3,
            LeaveTypeId = 2,
            StartDate = new DateOnly(2026, 12, 21),
            EndDate = new DateOnly(2026, 12, 22),
            TotalDays = 2m,
            Reason = "Concurrent decision test",
            CreatedAtUtc = DateTime.UtcNow
        };
        await using (var context = _fixture.CreateContext())
        {
            await new LeaveRequestRepository(context).CreateAsync(
                request,
                [new AnnualLeaveAllocation(2026, 2m, 5m)],
                new LeaveRequestHistory
                {
                    LeaveRequestId = request.Id,
                    ActorId = employeeId,
                    Action = "Created",
                    ToStatus = LeaveRequestStatus.Pending,
                    CreatedAtUtc = request.CreatedAtUtc
                });
        }

        async Task<bool> TryTransitionAsync(LeaveRequestStatus status)
        {
            await using var context = _fixture.CreateContext();
            try
            {
                await new LeaveRequestRepository(context).TransitionAsync(
                    request.Id,
                    managerId,
                    ManagerAccess(),
                    status,
                    null,
                    DateTime.UtcNow,
                    verifiedApproverId: managerId);
                return true;
            }
            catch (LeaveConflictException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(
            TryTransitionAsync(LeaveRequestStatus.Approved),
            TryTransitionAsync(LeaveRequestStatus.Rejected));

        Assert.Single(results, transitioned => transitioned);
        Assert.Single(results, transitioned => !transitioned);

        await using var verifyContext = _fixture.CreateContext();
        var savedRequest = await verifyContext.LeaveRequests.SingleAsync(item => item.Id == request.Id);
        var balance = await verifyContext.LeaveBalances.SingleAsync(item => item.EmployeeId == employeeId);
        Assert.True(savedRequest.Status is LeaveRequestStatus.Approved or LeaveRequestStatus.Rejected);
        Assert.Equal(0m, balance.Pending);
        Assert.Equal(
            savedRequest.Status == LeaveRequestStatus.Approved ? 2m : 0m,
            balance.Used);
        Assert.Equal(
            2,
            await verifyContext.LeaveRequestHistories.CountAsync(item => item.LeaveRequestId == request.Id));
    }
}

public sealed class LeaveSqlFactAttribute : FactAttribute
{
    public LeaveSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("HR_TEST_SQL_CONNECTION_STRING")))
        {
            Skip = "Set HR_TEST_SQL_CONNECTION_STRING to run SQL Server integration tests.";
        }
    }
}

public sealed class LeaveSqlIntegrationFixture : IAsyncLifetime
{
    private readonly string? _masterConnectionString =
        Environment.GetEnvironmentVariable("HR_TEST_SQL_CONNECTION_STRING");
    private bool _databaseCreated;

    public string DatabaseName { get; } = $"LeaveServiceIntegration_{Guid.NewGuid():N}";
    public string DatabaseConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_masterConnectionString))
        {
            return;
        }

        var master = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5,
            Encrypt = false,
            TrustServerCertificate = true
        };
        var database = new SqlConnectionStringBuilder(master.ConnectionString)
        {
            InitialCatalog = DatabaseName
        };
        DatabaseConnectionString = database.ConnectionString;

        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{DatabaseName}]";
            await command.ExecuteNonQueryAsync();
            _databaseCreated = true;
        }

        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await DropDatabaseAsync(master.ConnectionString);
            throw;
        }
    }

    public LeaveDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseSqlServer(DatabaseConnectionString)
            .Options;
        return new LeaveDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (!_databaseCreated || string.IsNullOrWhiteSpace(_masterConnectionString))
        {
            return;
        }

        var master = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5,
            Encrypt = false,
            TrustServerCertificate = true
        };
        await DropDatabaseAsync(master.ConnectionString);
    }

    private async Task DropDatabaseAsync(string masterConnectionString)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]";
        await command.ExecuteNonQueryAsync();
        _databaseCreated = false;
    }
}
