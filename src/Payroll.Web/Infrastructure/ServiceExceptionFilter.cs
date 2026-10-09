using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Payroll.Core.Domain;
namespace Payroll.Web.Infrastructure;

/// <summary>Translates domain exceptions into user-friendly outcomes: 403/404 pages or a flash message + redirect back.</summary>
public sealed class ServiceExceptionFilter : IExceptionFilter
{
    private readonly ITempDataDictionaryFactory _tempData;
    private readonly ILogger<ServiceExceptionFilter> _log;
    public ServiceExceptionFilter(ITempDataDictionaryFactory tempData, ILogger<ServiceExceptionFilter> log) => (_tempData, _log) = (tempData, log);

    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case ForbiddenException ex:
                _log.LogWarning("Forbidden: {Message} (user {User})", ex.Message, context.HttpContext.User.Identity?.Name);
                context.Result = new ForbidResult();
                break;
            case NotFoundException:
                context.Result = new NotFoundResult();
                break;
            case ValidationFailedException ex:
                Flash(context, string.Join(" ", ex.Errors.SelectMany(e => e.Value)));
                break;
            case BusinessRuleException ex:
                Flash(context, ex.Message);
                break;
            case DataAccessException ex:
                Flash(context, ex.Message);
                break;
            default:
                return;                       // unexpected: fall through to the global exception handler (logged, generic error page)
        }
        context.ExceptionHandled = true;
    }

    private void Flash(ExceptionContext context, string message)
    {
        _tempData.GetTempData(context.HttpContext)["Error"] = message;
        var request = context.HttpContext.Request;
        var target = "/";
        if (Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer) && referer.Host == request.Host.Host)
            target = referer.PathAndQuery;                       // same-site referer only: no open redirect
        context.Result = new LocalRedirectResult(target);
    }
}
