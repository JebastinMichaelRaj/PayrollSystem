using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payroll.Core.Abstractions;
using Payroll.Core.Calculation;
using Payroll.Core.Configuration;
using Payroll.Core.Domain;
using Payroll.Core.Models;
namespace Payroll.Core.Services;

public interface IPayrollService
{
    Task<IReadOnlyList<PayrollRunDto>> GetRunsAsync(CancellationToken ct = default);
    Task<PayrollRunDto> GetRunAsync(int runId, CancellationToken ct = default);
    Task<int> CreateRunAsync(int year, int month, CancellationToken ct = default);
    Task<PayrollProcessingResult> ProcessRunAsync(int runId, CancellationToken ct = default);
    Task ApproveRunAsync(int runId, CancellationToken ct = default);
    Task MarkPaidAsync(int runId, CancellationToken ct = default);
    Task<PayslipDetailDto> GetPayslipAsync(int payslipId, CancellationToken ct = default);
    Task<IReadOnlyList<PayslipListItem>> GetMyPayslipsAsync(CancellationToken ct = default);
}

public sealed class PayrollService : IPayrollService
{
    private static readonly string[] Viewers = { Roles.Admin, Roles.Hr, Roles.Payroll };
    private static readonly string[] Operators = { Roles.Admin, Roles.Payroll };
    private readonly IPayrollRepository _repo;
    private readonly IPayrollCalculator _calculator;
    private readonly ICurrentUser _user;
    private readonly PayrollOptions _opt;
    private readonly ILogger<PayrollService> _log;

    public PayrollService(IPayrollRepository repo, IPayrollCalculator calculator, ICurrentUser user, IOptions<PayrollOptions> opt, ILogger<PayrollService> log)
        => (_repo, _calculator, _user, _opt, _log) = (repo, calculator, user, opt.Value, log);

    public Task<IReadOnlyList<PayrollRunDto>> GetRunsAsync(CancellationToken ct = default) { _user.Require(Viewers); return _repo.GetRunsAsync(ct); }

    public async Task<PayrollRunDto> GetRunAsync(int runId, CancellationToken ct = default)
    {
        _user.Require(Viewers);
        return await _repo.GetRunAsync(runId, ct) ?? throw new NotFoundException("Payroll run not found.");
    }

    public async Task<int> CreateRunAsync(int year, int month, CancellationToken ct = default)
    {
        _user.Require(Operators);
        if (month is < 1 or > 12 || year is < 2000 or > 2100) throw new ValidationFailedException("period", "Enter a valid year and month.");
        var id = await _repo.CreateRunAsync(year, month, _user.UserId, ct);
        _log.LogInformation("Payroll run {RunId} created for {Year}-{Month:00} by user {UserId}", id, year, month, _user.UserId);
        return id;
    }

    /// <summary>
    /// 1) one DB round trip loads every input  2) pure in-memory calculation  3) one transactional bulk save.
    /// Re-processing is allowed until the run is approved; any calculation error aborts the whole run (no partial payroll).
    /// </summary>
    public async Task<PayrollProcessingResult> ProcessRunAsync(int runId, CancellationToken ct = default)
    {
        _user.Require(Operators);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var set = await _repo.GetCalculationInputsAsync(runId, _opt.FinancialYearStartMonth, ct);
        if (set.RunStatus is RunStatus.Approved or RunStatus.Paid) throw new BusinessRuleException("A finalised payroll run cannot be re-processed.");

        var results = new List<PayslipResult>(set.Employees.Count);
        var warnings = new List<string>();
        var errors = new List<string>();
        foreach (var emp in set.Employees)
        {
            if (emp.BasicSalary is null) { warnings.Add($"{emp.Code} {emp.Name}: no salary structure - skipped."); continue; }
            try
            {
                var slip = _calculator.Calculate(set, emp);
                if (slip is null) continue;
                results.Add(slip);
                warnings.AddRange(slip.Warnings.Select(w => $"{emp.Code}: {w}"));
            }
            catch (BusinessRuleException ex) { errors.Add(ex.Message); }
        }
        if (errors.Count > 0)
            throw new BusinessRuleException($"Payroll aborted - {errors.Count} employee(s) failed: {string.Join(" | ", errors.Take(10))}");
        if (results.Count == 0) throw new BusinessRuleException("No employees with a salary structure were found for this period.");

        await _repo.SavePayslipsAsync(runId, _user.UserId, results, ct);
        _log.LogInformation("Payroll run {RunId} processed: {Count} payslips, {Warnings} warnings, {Ms} ms", runId, results.Count, warnings.Count, sw.ElapsedMilliseconds);
        return new PayrollProcessingResult(runId, results.Count, results.Sum(r => r.GrossEarnings), results.Sum(r => r.NetPay), warnings);
    }

    public async Task ApproveRunAsync(int runId, CancellationToken ct = default)
    {
        _user.Require(Operators);
        await _repo.ApproveAsync(runId, _user.UserId, _opt.RequireSeparateApprover, ct);
        _log.LogInformation("Payroll run {RunId} approved by user {UserId}", runId, _user.UserId);
    }

    public async Task MarkPaidAsync(int runId, CancellationToken ct = default)
    {
        _user.Require(Operators);
        await _repo.MarkPaidAsync(runId, _user.UserId, ct);
        _log.LogInformation("Payroll run {RunId} marked paid by user {UserId}", runId, _user.UserId);
    }

    public async Task<PayslipDetailDto> GetPayslipAsync(int payslipId, CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        int? self = _user.IsInRole(Viewers) ? null : _user.RequireEmployeeId();      // the stored procedure enforces ownership + finalised status
        return await _repo.GetPayslipAsync(payslipId, self, ct) ?? throw new NotFoundException("Payslip not found.");
    }

    public Task<IReadOnlyList<PayslipListItem>> GetMyPayslipsAsync(CancellationToken ct = default)
    {
        _user.Require(Roles.All);
        return _repo.ListPayslipsAsync(_user.RequireEmployeeId(), true, ct);
    }
}
