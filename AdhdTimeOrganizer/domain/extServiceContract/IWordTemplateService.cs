namespace AdhdTimeOrganizer.domain.extServiceContract;

public interface IWordTemplateService
{
    Task<string> FillTemplateAndConvertToPdf(
        string templatePath,
        string outputDirectory,
        string outputFileName,
        Dictionary<string, string> fieldValues);
}