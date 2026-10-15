namespace EPaperDashboard.Authorization;

/// <summary>Marks endpoints a signed-in user can still reach while they must replace a temporary password.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowedDuringPasswordChangeAttribute : Attribute;
