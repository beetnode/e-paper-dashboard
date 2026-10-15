using EPaperDashboard.Services;
using Microsoft.AspNetCore.Http;

namespace EPaperDashboard.Authorization;

public sealed class ApiKeyPolicyEvaluator(DeviceService deviceService)
{
    public bool Evaluate(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return false;
        }

        if (!httpContext.Request.Headers.TryGetValue("X-Api-Key", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        return deviceService.GetDeviceByApiKey(apiKey!) is not null;
    }
}
