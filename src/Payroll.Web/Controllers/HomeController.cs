using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Services;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

public sealed class HomeController : Controller
{
    private readonly IReportService _reports;
    public HomeController(IReportService reports) => _reports = reports;

    public async Task<IActionResult> Index(CancellationToken ct) => View(await _reports.GetDashboardAsync(ct));

    [AllowAnonymous]
    public IActionResult Error() => View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    [AllowAnonymous]
    public IActionResult Status(int id) => View("Error", new ErrorViewModel { StatusCode = id, RequestId = HttpContext.TraceIdentifier });
}
