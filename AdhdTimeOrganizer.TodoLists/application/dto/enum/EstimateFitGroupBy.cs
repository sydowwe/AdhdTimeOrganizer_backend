namespace AdhdTimeOrganizer.TodoLists.application.dto.@enum;

/// <summary>What the estimate fit (F3-01) groups completed to-do items by.</summary>
public enum EstimateFitGroupBy
{
    /// <summary>The item's list. An item with no list groups under a <c>null</c> key.</summary>
    List,

    /// <summary>The category of the item's list. Uncategorised lists group under a <c>null</c> key.</summary>
    Category,

    /// <summary>The item's <c>TaskPriority</c>.</summary>
    Priority
}
