namespace AdhdTimeOrganizer.domain.helper;

public static class DirectoryPathsHelper
{
    public static string GetImagesDirectoryPath(string directoryName)
    {
        var directoryPath = Path.Combine("wwwroot", "images", directoryName);
        if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    public static string GetProductImagesDirectoryPath(string? imageFileWithExtension)
    {
        return imageFileWithExtension == null
            ? GetImagesDirectoryPath("product")
            : Path.Combine(GetImagesDirectoryPath("product"), imageFileWithExtension);
    }
}