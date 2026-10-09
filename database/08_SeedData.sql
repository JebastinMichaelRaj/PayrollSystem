/* 08 - Reference data. ILLUSTRATIVE values only: replace statutory rates, tax slabs and leave policy with your jurisdiction's rules. */
USE PayrollDb;
GO
MERGE dbo.Department AS t USING (VALUES (N'ENG',N'Engineering'),(N'HR',N'Human Resources'),(N'FIN',N'Finance'),(N'OPS',N'Operations'),(N'SAL',N'Sales')) s(Code,Name)
  ON t.Code = s.Code WHEN NOT MATCHED THEN INSERT(Code,Name) VALUES(s.Code,s.Name);
MERGE dbo.Designation AS t USING (VALUES (N'Software Engineer'),(N'Senior Engineer'),(N'Manager'),(N'HR Executive'),(N'Accountant'),(N'Director'),(N'Sales Executive')) s(Title)
  ON t.Title = s.Title WHEN NOT MATCHED THEN INSERT(Title) VALUES(s.Title);
MERGE dbo.SalaryComponent AS t USING (VALUES
   (N'HRA',N'House Rent Allowance',N'Earning',N'PercentOfBasic',1),
   (N'TRANSPORT',N'Transport Allowance',N'Earning',N'Fixed',0),
   (N'SPECIAL',N'Special Allowance',N'Earning',N'Fixed',1),
   (N'CANTEEN',N'Canteen Recovery',N'Deduction',N'Fixed',0),
   (N'OTHERDED',N'Other Deduction',N'Deduction',N'Fixed',0)) s(Code,Name,ComponentType,CalcMethod,IsTaxable)
  ON t.Code = s.Code WHEN NOT MATCHED THEN INSERT(Code,Name,ComponentType,CalcMethod,IsTaxable) VALUES(s.Code,s.Name,s.ComponentType,s.CalcMethod,s.IsTaxable);
MERGE dbo.LeaveType AS t USING (VALUES
   (N'AL',N'Annual Leave',18.0,N'Monthly',5.0,1,1,NULL),
   (N'SL',N'Sick Leave',12.0,N'Monthly',0.0,1,1,NULL),
   (N'CL',N'Casual Leave',6.0,N'Annual',0.0,1,1,3),
   (N'UL',N'Unpaid Leave',0.0,N'Monthly',0.0,0,1,NULL)) s(Code,Name,Ent,Method,Carry,IsPaid,Half,MaxConsec)
  ON t.Code = s.Code WHEN NOT MATCHED THEN INSERT(Code,Name,AnnualEntitlement,AccrualMethod,MaxCarryForward,IsPaid,AllowHalfDay,MaxConsecutiveDays)
  VALUES(s.Code,s.Name,s.Ent,s.Method,s.Carry,s.IsPaid,s.Half,s.MaxConsec);
IF NOT EXISTS (SELECT 1 FROM dbo.StatutoryRule)
    INSERT dbo.StatutoryRule(Code,Name,EmployeeRatePercent,EmployerRatePercent,WageCeiling,BaseOn,IsPreTax,EffectiveFrom) VALUES
      (N'PF',N'Provident Fund',12,12,15000,N'Basic',1,'2020-01-01'),
      (N'INS',N'Health Insurance',1,2,NULL,N'Gross',0,'2020-01-01');
-- Sample progressive slabs for two financial years (annual income, after standard deduction)
INSERT dbo.TaxParameter(FinancialYear, AnnualStandardDeduction, CessPercent)
SELECT y.FY, 75000, 4 FROM (VALUES (2025),(2026)) y(FY) WHERE NOT EXISTS (SELECT 1 FROM dbo.TaxParameter p WHERE p.FinancialYear = y.FY);
INSERT dbo.TaxSlab(FinancialYear, FromAmount, ToAmount, RatePercent)
SELECT y.FY, s.F, s.T, s.R FROM (VALUES (2025),(2026)) y(FY)
CROSS JOIN (VALUES (0,400000,0),(400000,800000,5),(800000,1200000,10),(1200000,1600000,15),(1600000,2000000,20),(2000000,2400000,25),(2400000,NULL,30)) s(F,T,R)
WHERE NOT EXISTS (SELECT 1 FROM dbo.TaxSlab x WHERE x.FinancialYear = y.FY AND x.FromAmount = s.F);
GO
