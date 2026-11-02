SET NOCOUNT ON;

USE Leave_DB;

DELETE request
FROM dbo.LeaveRequests AS request
WHERE request.Reason LIKE N'LOADTEST:%'
  AND request.EmployeeId IN
  (
      SELECT Id
      FROM HR_DB.dbo.Employees
      WHERE Email LIKE N'loadtest.user.%@example.invalid'
  );

DELETE balance
FROM dbo.LeaveBalances AS balance
WHERE balance.EmployeeId IN
(
    SELECT Id
    FROM HR_DB.dbo.Employees
    WHERE Email LIKE N'loadtest.user.%@example.invalid'
);

USE HR_DB;

DELETE employee
FROM dbo.Employees AS employee
WHERE employee.Email LIKE N'loadtest.user.%@example.invalid';
