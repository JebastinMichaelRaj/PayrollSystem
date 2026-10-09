/* 03 - Identity, lookups and employee procedures */
USE PayrollDb;
GO
/* ============================ Identity ============================ */
CREATE OR ALTER PROCEDURE dbo.usp_User_GetByUsername @Username NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT UserId, Username, PasswordHash, [Role], EmployeeId, IsActive, FailedLoginCount, LockoutEndUtc, MustChangePassword
    FROM dbo.AppUser WHERE Username = @Username;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_User_GetById @UserId INT
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT UserId, Username, PasswordHash, [Role], EmployeeId, IsActive, FailedLoginCount, LockoutEndUtc, MustChangePassword
    FROM dbo.AppUser WHERE UserId = @UserId;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_User_RegisterLogin
    @UserId INT, @Success BIT, @MaxFailures INT = 5, @LockoutMinutes INT = 15, @IpAddress NVARCHAR(45) = NULL
AS
BEGIN
    SET NOCOUNT OFF;
    IF @Success = 1
    BEGIN
        UPDATE dbo.AppUser SET FailedLoginCount = 0, LockoutEndUtc = NULL, LastLoginUtc = SYSUTCDATETIME() WHERE UserId = @UserId;
        EXEC dbo.usp_Audit_Write @UserId = @UserId, @EntityName = N'AppUser', @EntityId = @UserId, @Action = N'LoginSuccess', @IpAddress = @IpAddress;
    END
    ELSE
    BEGIN
        UPDATE dbo.AppUser
        SET FailedLoginCount = FailedLoginCount + 1,
            LockoutEndUtc = CASE WHEN FailedLoginCount + 1 >= @MaxFailures THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME()) ELSE LockoutEndUtc END
        WHERE UserId = @UserId;
        EXEC dbo.usp_Audit_Write @UserId = @UserId, @EntityName = N'AppUser', @EntityId = @UserId, @Action = N'LoginFailed', @IpAddress = @IpAddress;
    END
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_User_Create
    @Username NVARCHAR(100), @PasswordHash NVARCHAR(500), @Role NVARCHAR(30),
    @EmployeeId INT = NULL, @MustChangePassword BIT = 1, @ActingUserId INT = NULL, @NewUserId INT OUTPUT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    IF @Role NOT IN (N'Admin', N'HR', N'Payroll', N'Manager', N'Employee') THROW 50005, 'Invalid role.', 1;
    IF EXISTS (SELECT 1 FROM dbo.AppUser WHERE Username = @Username) THROW 50004, 'That username is already taken.', 1;
    IF @EmployeeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @EmployeeId) THROW 50001, 'Employee not found.', 1;
    IF @EmployeeId IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.AppUser WHERE EmployeeId = @EmployeeId) THROW 50004, 'That employee already has a login.', 1;
    INSERT dbo.AppUser(Username, PasswordHash, [Role], EmployeeId, MustChangePassword) VALUES(@Username, @PasswordHash, @Role, @EmployeeId, @MustChangePassword);
    SET @NewUserId = SCOPE_IDENTITY();
    DECLARE @d NVARCHAR(200) = CONCAT(N'{"username":"', @Username, N'","role":"', @Role, N'"}');
    EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'AppUser', @EntityId = @NewUserId, @Action = N'Create', @Details = @d;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_User_ChangePassword @UserId INT, @NewHash NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT OFF;
    UPDATE dbo.AppUser SET PasswordHash = @NewHash, MustChangePassword = 0 WHERE UserId = @UserId;
    IF @@ROWCOUNT = 0 THROW 50001, 'User not found.', 1;
    EXEC dbo.usp_Audit_Write @UserId = @UserId, @EntityName = N'AppUser', @EntityId = @UserId, @Action = N'PasswordChange';
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_User_List
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT u.UserId, u.Username, u.[Role], u.EmployeeId, e.FirstName + N' ' + e.LastName AS EmployeeName, u.IsActive, u.LastLoginUtc
    FROM dbo.AppUser u LEFT JOIN dbo.Employee e ON e.EmployeeId = u.EmployeeId ORDER BY u.Username;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_User_SetActive @UserId INT, @IsActive BIT, @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF;
    IF @UserId = @ActingUserId AND @IsActive = 0 THROW 50005, 'You cannot deactivate your own account.', 1;
    UPDATE dbo.AppUser SET IsActive = @IsActive, FailedLoginCount = 0, LockoutEndUtc = NULL WHERE UserId = @UserId;
    IF @@ROWCOUNT = 0 THROW 50001, 'User not found.', 1;
    DECLARE @a NVARCHAR(40) = CASE WHEN @IsActive = 1 THEN N'Activate' ELSE N'Deactivate' END;
    EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'AppUser', @EntityId = @UserId, @Action = @a;
END
GO
/* ============================ Lookups ============================ */
CREATE OR ALTER PROCEDURE dbo.usp_Lookup_All
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT DepartmentId AS Id, Name FROM dbo.Department WHERE IsActive = 1 ORDER BY Name;
    SELECT DesignationId AS Id, Title AS Name FROM dbo.Designation WHERE IsActive = 1 ORDER BY Title;
    SELECT EmployeeId AS Id, EmployeeCode + N' - ' + FirstName + N' ' + LastName AS Name FROM dbo.Employee WHERE ExitDate IS NULL ORDER BY FirstName, LastName;
END
GO
/* ============================ Employees ============================ */
CREATE OR ALTER PROCEDURE dbo.usp_Employee_Create
    @FirstName NVARCHAR(60), @LastName NVARCHAR(60), @Email NVARCHAR(256), @Phone NVARCHAR(30) = NULL,
    @DateOfBirth DATE, @JoinDate DATE, @DepartmentId INT, @DesignationId INT, @ManagerId INT = NULL,
    @EmploymentType NVARCHAR(20), @TaxIdNumber NVARCHAR(30) = NULL, @BankName NVARCHAR(100) = NULL,
    @BankAccountNumber NVARCHAR(40) = NULL, @ActingUserId INT, @NewEmployeeId INT OUTPUT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        IF NOT EXISTS (SELECT 1 FROM dbo.Department WHERE DepartmentId = @DepartmentId AND IsActive = 1) THROW 50005, 'Department is invalid.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Designation WHERE DesignationId = @DesignationId AND IsActive = 1) THROW 50005, 'Designation is invalid.', 1;
        IF @ManagerId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @ManagerId AND ExitDate IS NULL) THROW 50005, 'Manager is invalid.', 1;
        IF EXISTS (SELECT 1 FROM dbo.Employee WHERE Email = @Email) THROW 50004, 'An employee with that email already exists.', 1;
        DECLARE @Seq INT, @Code NVARCHAR(20);
        SET @Seq = NEXT VALUE FOR dbo.seq_EmployeeCode;
        SET @Code = N'EMP' + RIGHT(N'00000' + CAST(@Seq AS NVARCHAR(10)), 5);
        INSERT dbo.Employee(EmployeeCode, FirstName, LastName, Email, Phone, DateOfBirth, JoinDate, DepartmentId, DesignationId, ManagerId,
                            EmploymentType, TaxIdNumber, BankName, BankAccountNumber, CreatedBy)
        VALUES(@Code, @FirstName, @LastName, @Email, @Phone, @DateOfBirth, @JoinDate, @DepartmentId, @DesignationId, @ManagerId,
               @EmploymentType, @TaxIdNumber, @BankName, @BankAccountNumber, @ActingUserId);
        SET @NewEmployeeId = SCOPE_IDENTITY();
        DECLARE @d NVARCHAR(300) = CONCAT(N'{"code":"', @Code, N'","name":"', @FirstName, N' ', @LastName, N'"}');
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'Employee', @EntityId = @NewEmployeeId, @Action = N'Create', @Details = @d;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Employee_Update
    @EmployeeId INT, @FirstName NVARCHAR(60), @LastName NVARCHAR(60), @Email NVARCHAR(256), @Phone NVARCHAR(30) = NULL,
    @DateOfBirth DATE, @JoinDate DATE, @DepartmentId INT, @DesignationId INT, @ManagerId INT = NULL,
    @EmploymentType NVARCHAR(20), @TaxIdNumber NVARCHAR(30) = NULL, @BankName NVARCHAR(100) = NULL,
    @BankAccountNumber NVARCHAR(40) = NULL, @RowVer VARBINARY(8), @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        IF @ManagerId = @EmployeeId THROW 50005, 'An employee cannot be their own manager.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Department WHERE DepartmentId = @DepartmentId) THROW 50005, 'Department is invalid.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Designation WHERE DesignationId = @DesignationId) THROW 50005, 'Designation is invalid.', 1;
        IF EXISTS (SELECT 1 FROM dbo.Employee WHERE Email = @Email AND EmployeeId <> @EmployeeId) THROW 50004, 'Another employee already uses that email.', 1;
        UPDATE dbo.Employee
        SET FirstName = @FirstName, LastName = @LastName, Email = @Email, Phone = @Phone, DateOfBirth = @DateOfBirth, JoinDate = @JoinDate,
            DepartmentId = @DepartmentId, DesignationId = @DesignationId, ManagerId = @ManagerId, EmploymentType = @EmploymentType,
            TaxIdNumber = @TaxIdNumber, BankName = @BankName, BankAccountNumber = @BankAccountNumber,
            UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ActingUserId
        WHERE EmployeeId = @EmployeeId AND RowVer = @RowVer;
        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @EmployeeId) THROW 50001, 'Employee not found.', 1;
            THROW 50010, 'This record was changed by someone else. Reload and try again.', 1;
        END
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'Employee', @EntityId = @EmployeeId, @Action = N'Update';
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Employee_GetById @EmployeeId INT
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT e.EmployeeId, e.EmployeeCode, e.FirstName, e.LastName, e.Email, e.Phone, e.DateOfBirth, e.JoinDate, e.ExitDate,
           e.DepartmentId, d.Name AS Department, e.DesignationId, g.Title AS Designation, e.ManagerId,
           m.FirstName + N' ' + m.LastName AS ManagerName, e.EmploymentType, e.TaxIdNumber, e.BankName, e.BankAccountNumber,
           e.IsActive, e.RowVer
    FROM dbo.Employee e
    JOIN dbo.Department d ON d.DepartmentId = e.DepartmentId
    JOIN dbo.Designation g ON g.DesignationId = e.DesignationId
    LEFT JOIN dbo.Employee m ON m.EmployeeId = e.ManagerId
    WHERE e.EmployeeId = @EmployeeId;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Employee_Search
    @Search NVARCHAR(100) = NULL, @DepartmentId INT = NULL, @IsActive BIT = NULL, @PageNumber INT = 1, @PageSize INT = 25
AS
BEGIN
    SET NOCOUNT OFF;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 OR @PageSize > 200 SET @PageSize = 25;
    SET @Search = NULLIF(LTRIM(RTRIM(@Search)), N'');
    IF @Search IS NOT NULL   -- neutralise LIKE wildcards supplied by the user
        SET @Search = REPLACE(REPLACE(REPLACE(REPLACE(@Search, N'\', N'\\'), N'%', N'\%'), N'_', N'\_'), N'[', N'\[');
    SELECT e.EmployeeId, e.EmployeeCode, e.FirstName + N' ' + e.LastName AS FullName, e.Email,
           d.Name AS Department, g.Title AS Designation, e.JoinDate, e.IsActive, COUNT(*) OVER() AS TotalCount
    FROM dbo.Employee e
    JOIN dbo.Department d ON d.DepartmentId = e.DepartmentId
    JOIN dbo.Designation g ON g.DesignationId = e.DesignationId
    WHERE (@IsActive IS NULL OR e.IsActive = @IsActive)
      AND (@DepartmentId IS NULL OR e.DepartmentId = @DepartmentId)
      AND (@Search IS NULL OR e.EmployeeCode LIKE @Search + N'%' ESCAPE N'\' OR e.FirstName LIKE @Search + N'%' ESCAPE N'\'
           OR e.LastName LIKE @Search + N'%' ESCAPE N'\' OR e.Email LIKE @Search + N'%' ESCAPE N'\')
    ORDER BY e.LastName, e.FirstName, e.EmployeeId
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Employee_Terminate @EmployeeId INT, @ExitDate DATE, @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @Join DATE, @Exit DATE;
        SELECT @Join = JoinDate, @Exit = ExitDate FROM dbo.Employee WITH (UPDLOCK) WHERE EmployeeId = @EmployeeId;
        IF @Join IS NULL THROW 50001, 'Employee not found.', 1;
        IF @Exit IS NOT NULL THROW 50002, 'Employee has already left.', 1;
        IF @ExitDate < @Join THROW 50005, 'Exit date cannot be before the join date.', 1;
        UPDATE dbo.Employee SET ExitDate = @ExitDate, IsActive = 0, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ActingUserId WHERE EmployeeId = @EmployeeId;
        UPDATE dbo.AppUser SET IsActive = 0 WHERE EmployeeId = @EmployeeId;
        UPDATE dbo.LeaveRequest SET [Status] = N'Cancelled', DecidedAt = SYSUTCDATETIME(), DecisionRemarks = N'Employee left the organisation'
        WHERE EmployeeId = @EmployeeId AND [Status] = N'Pending';
        DECLARE @d NVARCHAR(100) = CONCAT(N'{"exitDate":"', CONVERT(NVARCHAR(10), @ExitDate, 23), N'"}');
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'Employee', @EntityId = @EmployeeId, @Action = N'Terminate', @Details = @d;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
