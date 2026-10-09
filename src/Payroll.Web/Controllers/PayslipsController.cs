using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Services;
namespace Payroll.Web.Controllers;

public sealed class PayslipsController : Controller
{
    private readonly IPayrollService _payroll;
    public PayslipsController(IPayrollService payroll) => _payroll = payroll;

    public async Task<IActionResult> My(CancellationToken ct) => View(await _payroll.GetMyPayslipsAsync(ct));
    public async Task<IActionResult> Details(int id, CancellationToken ct) => View(await _payroll.GetPayslipAsync(id, ct));
}
