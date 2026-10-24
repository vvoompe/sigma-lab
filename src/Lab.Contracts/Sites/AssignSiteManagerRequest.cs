using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Sites;

/// <summary>Призначення керівника лабораторії на майданчик.</summary>
/// <param name="ManagerId">Ідентифікатор користувача з роллю <c>LabManager</c>.</param>
public sealed record AssignSiteManagerRequest(
    [Required, Range(1, int.MaxValue)] int? ManagerId);
