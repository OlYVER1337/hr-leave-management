:setvar ManagerCount "25"
:setvar ExpectedPendingPerManager "800"

SET NOCOUNT ON;

DECLARE @ManagerCount int = TRY_CONVERT(int, '$(ManagerCount)');
DECLARE @ExpectedPendingPerManager int = TRY_CONVERT(int, '$(ExpectedPendingPerManager)');

IF @ManagerCount NOT BETWEEN 1 AND 200
    THROW 51100, 'ManagerCount must be between 1 and 200.', 1;
IF @ExpectedPendingPerManager < 1
    THROW 51101, 'ExpectedPendingPerManager must be positive.', 1;

USE HR_DB;

DECLARE @PendingByManager TABLE
(
    ManagerCode nvarchar(20) NOT NULL PRIMARY KEY,
    PendingLoadTestRequests int NOT NULL
);

INSERT INTO @PendingByManager (ManagerCode, PendingLoadTestRequests)
SELECT
    manager.Code,
    COUNT(request.Id)
FROM dbo.Employees AS manager
LEFT JOIN dbo.Employees AS employee
    ON employee.ManagerId = manager.Id
   AND employee.IsActive = 1
   AND employee.Email LIKE N'loadtest.user.%@example.invalid'
LEFT JOIN Leave_DB.dbo.LeaveRequests AS request
    ON request.EmployeeId = employee.Id
   AND request.Status = N'Pending'
   AND request.Reason LIKE N'LOADTEST:%'
WHERE manager.Code LIKE N'LTM%'
  AND manager.Role = N'Manager'
  AND manager.IsActive = 1
  AND TRY_CONVERT(int, SUBSTRING(manager.Code, 4, 4)) BETWEEN 1 AND @ManagerCount
GROUP BY manager.Code;

IF (SELECT COUNT(*) FROM @PendingByManager) <> @ManagerCount
    THROW 51102, 'The seeded active Manager count does not match ManagerCount.', 1;

SELECT
    ManagerCode,
    PendingLoadTestRequests,
    @ExpectedPendingPerManager AS ExpectedPendingPerManager,
    CASE
        WHEN PendingLoadTestRequests >= @ExpectedPendingPerManager THEN N'PASS'
        ELSE N'FAIL'
    END AS Verification
FROM @PendingByManager
ORDER BY ManagerCode;

IF EXISTS
(
    SELECT 1
    FROM @PendingByManager
    WHERE PendingLoadTestRequests < @ExpectedPendingPerManager
)
    THROW 51103, 'At least one Manager has fewer pending load-test requests than required.', 1;

SELECT
    SUM(PendingLoadTestRequests) AS TotalPendingLoadTestRequests,
    COUNT(*) AS VerifiedManagers
FROM @PendingByManager;
