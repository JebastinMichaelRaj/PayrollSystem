using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Services;
using Payroll.Web.Infrastructure;
using Payroll.Web.ViewModels;
namespace Payroll.Web.Controllers;

public sealed class HolidaysController : Controller
{
    private readonly ICompensationService _comp;
    public HolidaysController(ICompensationService comp) => _comp = comp;

    public async Task<IActionResult> Index(int? year, CancellationToken ct)
    {
        var y = year ?? DateTime.UtcNow.Year;
        return View(new HolidaysViewModel { Year = y, Holidays = await _comp.GetHolidaysAsync(y, ct) });
    }

    [HttpPost, Authorize(Policy = Policies.ManageEmployees)]
    public async Task<IActionResult> Upsert(DateOnly date, string name, CancellationToken ct)
    {
        await _comp.UpsertHolidayAsync(date, name, ct);
        TempData["Success"] = "Holiday saved.";
        return RedirectToAction(nameof(Index), new { year = date.Year });
    }

    [HttpPost, Authorize(Policy = Policies.ManageEmployees)]
    public async Task<IActionResult> Delete(DateOnly date, CancellationToken ct)
    {
        await _comp.DeleteHolidayAsync(date, ct);
        TempData["Success"] = "Holiday removed.";
        return RedirectToAction(nameof(Index), new { year = date.Year });
    }
}
