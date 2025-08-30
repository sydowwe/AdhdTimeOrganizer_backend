namespace AdhdTimeOrganizer.domain.extServiceContract;

public interface IPdfTemplateService
{
    /// <summary>
    /// Fills out an AcroForm template PDF with the supplied data and outputs a flattened PDF.
    /// </summary>
    /// <param name="templateName">Name of the PDF template</param>
    /// <param name="outputName">Path where the filled PDF will be saved</param>
    /// <param name="formData">Dictionary of form field names and their respective values</param>
    string FillUserContractTemplate(string templateName, string outputName, Dictionary<string, string> formData);
}