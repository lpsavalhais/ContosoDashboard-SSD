namespace ContosoDashboard.Models;

public static class DocumentCategories
{
    public const string ProjectDocuments = "Project Documents";
    public const string TeamResources = "Team Resources";
    public const string PersonalFiles = "Personal Files";
    public const string Reports = "Reports";
    public const string Presentations = "Presentations";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } =
    [
        ProjectDocuments,
        TeamResources,
        PersonalFiles,
        Reports,
        Presentations,
        Other
    ];

    public static bool IsValid(string category) => All.Contains(category, StringComparer.Ordinal);
}

public static class DocumentFileTypes
{
    public static IReadOnlyDictionary<string, string> Map { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".txt"] = "text/plain; charset=utf-8",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png"
        };

    public static bool IsAllowed(string extension) => Map.ContainsKey(extension);
}
