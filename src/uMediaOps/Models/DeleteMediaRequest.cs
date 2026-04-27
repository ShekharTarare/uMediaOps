namespace uMediaOps.Models;

/// <summary>
/// Request model for bulk deletion of unused media items
/// </summary>
public class DeleteMediaRequest
{
    /// <summary>
    /// List of media IDs to delete
    /// </summary>
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MinLength(1, ErrorMessage = "At least one media ID is required")]
    public List<int> MediaIds { get; set; } = new();
}
