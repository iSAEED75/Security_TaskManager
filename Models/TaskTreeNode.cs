namespace TasksWebApp.Models;

public sealed class TaskTreeNode
{
    public required TaskItem Item { get; init; }
    public List<TaskTreeNode> Children { get; init; } = [];
}
