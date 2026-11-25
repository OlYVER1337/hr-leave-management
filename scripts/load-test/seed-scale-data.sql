SET NOCOUNT ON;
-- Bắt buộc: bảng UserRoles có unique index có điều kiện (filtered), cần QUOTED_IDENTIFIER ON.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @EmployeeCount int = TRY_CONVERT(int, '$(EmployeeCount)');
DECLARE @RequestsPerEmployee int = TRY_CONVERT(int, '$(RequestsPerEmployee)');
DECLARE @ManagerCount int = TRY_CONVERT(int, '$(ManagerCount)');
DECLARE @PendingPercent int = TRY_CONVERT(int, '$(PendingPercent)');

PRINT CONCAT(
    'Seed configuration: EmployeeCount=', @EmployeeCount,
    '; RequestsPerEmployee=', @RequestsPerEmployee,
    '; ManagerCount=', @ManagerCount,
    '; PendingPercent=', @PendingPercent, '.'
);

IF @EmployeeCount IS NULL OR @EmployeeCount NOT BETWEEN 1000 AND 5000
    THROW 51000, 'EmployeeCount must be between 1000 and 5000.', 1;
IF @RequestsPerEmployee IS NULL OR @RequestsPerEmployee NOT BETWEEN 1 AND 20
    THROW 51001, 'RequestsPerEmployee must be between 1 and 20.', 1;
IF @ManagerCount IS NULL OR @ManagerCount NOT BETWEEN 1 AND 200
    THROW 51005, 'ManagerCount must be between 1 and 200.', 1;
IF @PendingPercent IS NULL OR @PendingPercent NOT IN (25, 50)
    THROW 51008, 'PendingPercent must be 25 (standard seed) or 50 (approval sustained seed).', 1;
IF @PendingPercent = 50 AND @RequestsPerEmployee <> 20
    THROW 51010, 'PendingPercent=50 requires RequestsPerEmployee=20 to preserve the per-employee balance quota.', 1;

CREATE TABLE #LoadTestEmployees
(
    EmployeeId uniqueidentifier NOT NULL PRIMARY KEY,
    Code nvarchar(20) NOT NULL
);

USE HR_DB;

DECLARE @ManagerId uniqueidentifier;
DECLARE @DepartmentId int;
DECLARE @PasswordHash nvarchar(max);

SELECT TOP (1)
    @ManagerId = Id,
    @DepartmentId = DepartmentId,
    @PasswordHash = PasswordHash
FROM dbo.Employees
WHERE Role = N'Manager' AND IsActive = 1
ORDER BY Code;

IF @ManagerId IS NULL
    THROW 51002, 'An active Manager is required before seeding load-test employees.', 1;

DECLARE @ManagerRoleId int = (SELECT Id FROM dbo.Roles WHERE Code = N'Manager' AND IsActive = 1);
IF @ManagerRoleId IS NULL
    THROW 51006, 'The Manager role must exist. Start hr-service once so authorization is seeded.', 1;

CREATE TABLE #LoadTestManagers
(
    ManagerId uniqueidentifier NOT NULL PRIMARY KEY,
    Number int NOT NULL
);

;WITH NumberSource AS
(
    SELECT CONVERT(int, ROW_NUMBER() OVER (ORDER BY (SELECT NULL))) AS Number
    FROM sys.all_objects AS firstSource
    CROSS JOIN sys.all_objects AS secondSource
),
ManagerNumbers AS
(
    SELECT Number
    FROM NumberSource
    WHERE Number <= @ManagerCount
)
INSERT INTO dbo.Employees
(
    Id, Code, FullName, Email, PasswordHash, Role, HireDate, IsActive,
    DepartmentId, ManagerId
)
SELECT
    NEWID(),
    N'LTM' + RIGHT(N'0000' + CONVERT(nvarchar(4), Number), 4),
    N'Load Test Manager ' + CONVERT(nvarchar(4), Number),
    N'loadtest.manager.' + RIGHT(N'0000' + CONVERT(nvarchar(4), Number), 4) + N'@example.invalid',
    @PasswordHash,
    N'Manager',
    DATEADD(day, -Number % 1800, CONVERT(date, SYSUTCDATETIME())),
    1,
    @DepartmentId,
    NULL
FROM ManagerNumbers
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Employees AS existing
    WHERE existing.Code = N'LTM' + RIGHT(N'0000' + CONVERT(nvarchar(4), Number), 4)
);

INSERT INTO #LoadTestManagers (ManagerId, Number)
SELECT Id, TRY_CONVERT(int, SUBSTRING(Code, 4, 4))
FROM dbo.Employees
WHERE Code LIKE N'LTM%' AND Role = N'Manager' AND IsActive = 1
  AND TRY_CONVERT(int, SUBSTRING(Code, 4, 4)) BETWEEN 1 AND @ManagerCount;

IF (SELECT COUNT(*) FROM #LoadTestManagers) <> @ManagerCount
    THROW 51007, 'The requested load-test manager set could not be prepared.', 1;

INSERT INTO dbo.UserRoles (EmployeeId, RoleId, GrantedByEmployeeId, GrantedAtUtc)
SELECT m.ManagerId, @ManagerRoleId, NULL, SYSUTCDATETIME()
FROM #LoadTestManagers AS m
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.UserRoles AS ur
    WHERE ur.EmployeeId = m.ManagerId AND ur.RoleId = @ManagerRoleId AND ur.RevokedAtUtc IS NULL
);

INSERT INTO #LoadTestEmployees (EmployeeId, Code)
SELECT TOP (@EmployeeCount) Id, Code
FROM dbo.Employees
WHERE Email LIKE N'loadtest.user.%@example.invalid'
ORDER BY Code;

;WITH NumberSource AS
(
    SELECT CONVERT(int, ROW_NUMBER() OVER (ORDER BY (SELECT NULL))) AS Number
    FROM sys.all_objects AS firstSource
    CROSS JOIN sys.all_objects AS secondSource
),
Numbers AS
(
    SELECT Number
    FROM NumberSource
    WHERE Number <= @EmployeeCount
)
INSERT INTO dbo.Employees
(
    Id, Code, FullName, Email, PasswordHash, Role, HireDate, IsActive,
    DepartmentId, ManagerId
)
OUTPUT inserted.Id, inserted.Code INTO #LoadTestEmployees (EmployeeId, Code)
SELECT
    NEWID(),
    N'LT' + RIGHT(N'0000000' + CONVERT(nvarchar(7), Numbers.Number), 7),
    N'Load Test Employee ' + CONVERT(nvarchar(7), Numbers.Number),
    N'loadtest.user.' + RIGHT(N'0000000' + CONVERT(nvarchar(7), Numbers.Number), 7)
        + N'@example.invalid',
    @PasswordHash,
    N'Employee',
    DATEADD(day, -Numbers.Number % 1800, CONVERT(date, SYSUTCDATETIME())),
    1,
    @DepartmentId,
    NULL
FROM Numbers
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Employees AS existing
    WHERE existing.Code = N'LT' + RIGHT(N'0000000' + CONVERT(nvarchar(7), Numbers.Number), 7)
);

;WITH Assignment AS
(
    SELECT
        loadTest.EmployeeId,
        ((TRY_CONVERT(int, SUBSTRING(loadTest.Code, 3, 7)) - 1) % @ManagerCount) + 1 AS ManagerNumber
    FROM #LoadTestEmployees AS loadTest
)
UPDATE e
SET e.ManagerId = m.ManagerId
FROM dbo.Employees AS e
INNER JOIN Assignment AS a ON a.EmployeeId = e.Id
INNER JOIN #LoadTestManagers AS m ON m.Number = a.ManagerNumber;

IF (SELECT COUNT(*) FROM #LoadTestEmployees) <> @EmployeeCount
    THROW 51003, 'The requested load-test employee set could not be prepared.', 1;

USE Leave_DB;

DECLARE @LeaveTypeId int;
DECLARE @RequestCount int = @EmployeeCount * @RequestsPerEmployee;
DECLARE @Year int = YEAR(SYSUTCDATETIME());
DECLARE @YearStart date = DATEFROMPARTS(@Year, 1, 1);
DECLARE @DaysInYear int = DATEDIFF(day, @YearStart, DATEFROMPARTS(@Year + 1, 1, 1));

SELECT @LeaveTypeId = Id
FROM HR_DB.dbo.LeaveTypes
WHERE Code = N'ANNUAL' AND IsActive = 1;

IF @LeaveTypeId IS NULL
    THROW 51004, 'An active ANNUAL leave type is required before seeding requests.', 1;

CREATE TABLE #LoadTestRequests
(
    RequestId uniqueidentifier NOT NULL PRIMARY KEY,
    EmployeeId uniqueidentifier NOT NULL,
    ApproverId uniqueidentifier NULL,
    DepartmentId int NOT NULL,
    StartDate date NOT NULL,
    EndDate date NOT NULL,
    TotalDays decimal(6,1) NOT NULL,
    Status nvarchar(20) NOT NULL,
    CreatedAtUtc datetime2 NOT NULL,
    RequestNumber int NOT NULL
);

BEGIN TRANSACTION;

DELETE request
FROM dbo.LeaveRequests AS request
INNER JOIN #LoadTestEmployees AS employee ON employee.EmployeeId = request.EmployeeId
WHERE request.Reason LIKE N'LOADTEST:%';

DELETE balance
FROM dbo.LeaveBalances AS balance
INNER JOIN #LoadTestEmployees AS employee ON employee.EmployeeId = balance.EmployeeId;

;WITH CalendarNumberSource AS
(
    SELECT CONVERT(int, ROW_NUMBER() OVER (ORDER BY (SELECT NULL))) AS Number
    FROM sys.all_objects AS firstSource
    CROSS JOIN sys.all_objects AS secondSource
),
CalendarDays AS
(
    SELECT DATEADD(day, Number - 1, @YearStart) AS CalendarDate
    FROM CalendarNumberSource
    WHERE Number <= @DaysInYear
),
BusinessDays AS
(
    -- Ngày làm việc: thứ Hai đến thứ Sáu, loại các ngày lễ đã cấu hình HR.
    SELECT
        CalendarDate,
        CONVERT(int, ROW_NUMBER() OVER (ORDER BY CalendarDate)) AS BusinessDayNumber
    FROM CalendarDays
    WHERE DATEDIFF(day, CONVERT(date, '19000101'), CalendarDate) % 7 BETWEEN 0 AND 4
      AND NOT EXISTS
      (
          SELECT 1
          FROM HR_DB.dbo.Holidays AS holiday
          WHERE holiday.HolidayDate = CalendarDays.CalendarDate
      )
),
ScheduleDates AS
(
    -- Chọn một ngày sau mỗi 5 ngày làm việc để các request cách nhau tối thiểu một tuần làm việc.
    SELECT
        CalendarDate,
        CONVERT(int, ROW_NUMBER() OVER (ORDER BY BusinessDayNumber)) AS RequestSlot
    FROM BusinessDays
    WHERE (BusinessDayNumber - 1) % 5 = 0
),
EmployeeNumbers AS
(
    SELECT
        EmployeeId,
        Code,
        TRY_CONVERT(int, SUBSTRING(Code, 3, 7)) AS EmployeeNumber
    FROM #LoadTestEmployees
),
RequestNumberSource AS
(
    SELECT CONVERT(int, ROW_NUMBER() OVER (ORDER BY (SELECT NULL))) AS Number
    FROM sys.all_objects AS firstSource
    CROSS JOIN sys.all_objects AS secondSource
),
RequestNumbers AS
(
    SELECT Number
    FROM RequestNumberSource
    WHERE Number <= @RequestCount
)
INSERT INTO #LoadTestRequests
(
    RequestId, EmployeeId, ApproverId, DepartmentId, StartDate, EndDate,
    TotalDays, Status, CreatedAtUtc, RequestNumber
)
SELECT
    NEWID(),
    employee.EmployeeId,
    NULL,
    @DepartmentId,
    schedule.CalendarDate,
    schedule.CalendarDate,
    CONVERT(decimal(6,1), 1.0),
    CASE
        -- Seed tiêu chuẩn giữ phân phối 25% Pending như trước.
        WHEN @PendingPercent = 25 THEN
            CASE numbers.Number % 4
                WHEN 0 THEN N'Pending'
                WHEN 1 THEN N'Approved'
                WHEN 2 THEN N'Rejected'
                ELSE N'Cancelled'
            END
        -- Seed sustained: mỗi nhân viên có 20 request:
        -- 10 Pending, 2 Approved, 4 Rejected, 4 Cancelled.
        -- Used + Pending = 2 + 10 = 12 ngày, không vượt PolicyDays=12.
        WHEN ((numbers.Number - 1) % @RequestsPerEmployee) % 10 < 5 THEN N'Pending'
        WHEN ((numbers.Number - 1) % @RequestsPerEmployee) % 10 = 5 THEN N'Approved'
        WHEN ((numbers.Number - 1) % @RequestsPerEmployee) % 10 IN (6, 7) THEN N'Rejected'
        ELSE N'Cancelled'
    END,
    DATEADD(minute, -numbers.Number, SYSUTCDATETIME()),
    numbers.Number
FROM RequestNumbers AS numbers
INNER JOIN EmployeeNumbers AS employee
    ON employee.EmployeeNumber =
        ((numbers.Number - 1) / @RequestsPerEmployee) % @EmployeeCount + 1
INNER JOIN ScheduleDates AS schedule
    ON schedule.RequestSlot = ((numbers.Number - 1) % @RequestsPerEmployee) + 1;

IF (SELECT COUNT(*) FROM #LoadTestRequests) <> @RequestCount
    THROW 51012, 'Could not allocate enough unique non-holiday business dates for the load-test requests.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        SELECT
            EmployeeId,
            StartDate,
            LAG(EndDate) OVER
                (PARTITION BY EmployeeId ORDER BY StartDate, RequestNumber) AS PreviousEndDate
        FROM #LoadTestRequests
    ) AS orderedRequests
    WHERE orderedRequests.PreviousEndDate >= orderedRequests.StartDate
)
    THROW 51009, 'Generated load-test requests overlap for the same employee.', 1;

IF EXISTS
(
    SELECT EmployeeId
    FROM #LoadTestRequests
    GROUP BY EmployeeId
    HAVING SUM
    (
        CASE WHEN Status IN (N'Pending', N'Approved') THEN TotalDays ELSE 0 END
    ) > 12.0
)
    THROW 51011, 'Generated load-test Used + Pending days exceed the seeded PolicyDays quota.', 1;

UPDATE request
SET request.ApproverId = employee.ManagerId
FROM #LoadTestRequests AS request
INNER JOIN HR_DB.dbo.Employees AS employee ON employee.Id = request.EmployeeId;

INSERT INTO dbo.LeaveRequests
(
    Id, EmployeeId, ApproverId, DepartmentId, LeaveTypeId, StartDate, EndDate,
    TotalDays, Reason, Status, CreatedAtUtc, DecidedAtUtc
)
SELECT
    RequestId,
    EmployeeId,
    ApproverId,
    DepartmentId,
    @LeaveTypeId,
    StartDate,
    EndDate,
    TotalDays,
    N'LOADTEST:' + CONVERT(nvarchar(10), RequestNumber),
    Status,
    CreatedAtUtc,
    CASE WHEN Status = N'Pending' THEN NULL ELSE DATEADD(hour, 1, CreatedAtUtc) END
FROM #LoadTestRequests;

INSERT INTO dbo.LeaveRequestYearAllocations (LeaveRequestId, Year, Days)
SELECT RequestId, @Year, TotalDays
FROM #LoadTestRequests;

INSERT INTO dbo.LeaveRequestHistories
(
    Id, LeaveRequestId, ActorId, Action, FromStatus, ToStatus, Note, CreatedAtUtc
)
SELECT
    NEWID(),
    RequestId,
    EmployeeId,
    N'Created',
    NULL,
    Status,
    N'Scale-test fixture',
    CreatedAtUtc
FROM #LoadTestRequests;

INSERT INTO dbo.LeaveBalances (Id, EmployeeId, LeaveTypeId, Year, PolicyDays, Used, Pending)
SELECT
    NEWID(),
    employee.EmployeeId,
    @LeaveTypeId,
    @Year,
    12.0,
    COALESCE(SUM(CASE WHEN request.Status = N'Approved' THEN request.TotalDays ELSE 0 END), 0),
    COALESCE(SUM(CASE WHEN request.Status = N'Pending' THEN request.TotalDays ELSE 0 END), 0)
FROM #LoadTestEmployees AS employee
LEFT JOIN #LoadTestRequests AS request ON request.EmployeeId = employee.EmployeeId
GROUP BY employee.EmployeeId;

COMMIT TRANSACTION;

SELECT
    @EmployeeCount AS SeededEmployees,
    @RequestCount AS SeededLeaveRequests,
    @Year AS LeaveYear;
