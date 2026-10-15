using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Providers;

/// <summary>
/// Provides todo/task list data for the todo widget.
/// </summary>
public interface ITodoDataProvider
{
    Task<Result<List<TodoItem>>> FetchTodoItemsAsync(string dashboardId, string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all available todo entities and fetches items for each.
    /// </summary>
    Task<Result<Dictionary<string, List<TodoItem>>>> FetchAllTodoItemsAsync(string dashboardId, CancellationToken cancellationToken = default);
}
