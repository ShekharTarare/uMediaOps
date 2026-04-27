using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace uMediaOps.Filters;

/// <summary>
/// Global exception filter that prevents internal error details from leaking to API responses.
/// In Development, full error details are returned. In Production, only generic messages are returned.
/// </summary>
public class uMediaOpsExceptionFilter : IExceptionFilter
{
    private readonly ILogger<uMediaOpsExceptionFilter> _logger;
    private readonly IWebHostEnvironment _environment;

    public uMediaOpsExceptionFilter(
        ILogger<uMediaOpsExceptionFilter> logger,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public void OnException(ExceptionContext context)
    {
        _logger.LogError(context.Exception, "Unhandled exception in {Controller}.{Action}",
            context.RouteData.Values["controller"],
            context.RouteData.Values["action"]);

        var isDevelopment = _environment.IsDevelopment();

        var response = new
        {
            message = "An internal error occurred. Please try again or contact your administrator.",
            error = isDevelopment ? context.Exception.Message : (string?)null,
            stackTrace = isDevelopment ? context.Exception.StackTrace : (string?)null
        };

        context.Result = new ObjectResult(response)
        {
            StatusCode = 500
        };

        context.ExceptionHandled = true;
    }
}
