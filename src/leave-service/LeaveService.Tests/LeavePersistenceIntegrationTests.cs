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
    private readonly LeaveSqlIntegrationFixture _fixture;

    public LeavePersistenceIntegrationTests(LeaveSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

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
            [new AnnualLeaveAllocation(2026, 2m, 3m)],
            new LeaveRequestHistory
            {
                LeaveRequestId = request.Id,
                ActorId = employeeId,
                Action = "Created",
                ToStatus = LeaveRequestStatus.Pending,
                CreatedAtUtc = request.CreatedAtUtc
            });

        Assert.Equal(2m, created.Balances.Single().Pending);
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
            var managerQueue = await repository.ListPendingAsync(approvedManager);
            Assert.Contains(managerQueue, item => item.Request.Id == approvedRequest.Id);
            await Assert.ThrowsAsync<LeaveForbiddenException>(() => repository.TransitionAsync(
                approvedRequest.Id,
                Guid.NewGuid(),
                isHr: false,
                LeaveRequestStatus.Approved,
                null,
                DateTime.UtcNow));
        }

        await using (var context = _fixture.CreateContext())
        {
            var result = await new LeaveRequestRepository(context).TransitionAsync(
                approvedRequest.Id,
                approvedManager,
                isHr: false,
                LeaveRequestStatus.Approved,
                "Approved by manager",
                DateTime.UtcNow);
            Assert.Equal(LeaveRequestStatus.Approved, result.Request.Status);
            Assert.Equal(0m, result.Balances.Single().Pending);
            Assert.Equal(2m, result.Balances.Single().Used);
        }

        var rejectedEmployee = Guid.NewGuid();
        var rejectedManager = Guid.NewGuid();
        var rejectedRequest = await CreateRequestAsync(rejectedEmployee, rejectedManager, 5);
        await using (var context = _fixture.CreateContext())
        {
            var result = await new LeaveRequestRepository(context).TransitionAsync(
                rejectedRequest.Id,
                rejectedManager,
                isHr: false,
                LeaveRequestStatus.Rejected,
                "Not enough coverage",
                DateTime.UtcNow);
            Assert.Equal(LeaveRequestStatus.Rejected, result.Request.Status);
            Assert.Equal(0m, result.Balances.Single().Pending);
            Assert.Equal(0m, result.Balances.Single().Used);
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
                    isHr: false,
                    status,
                    null,
                    DateTime.UtcNow);
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
