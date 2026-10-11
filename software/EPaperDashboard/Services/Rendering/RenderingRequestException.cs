namespace EPaperDashboard.Services.Rendering;

// Only fixed, user-facing messages may cross the rendering API boundary.
internal sealed class RenderingRequestException(string message) : InvalidOperationException(message);
