namespace AdhdTimeOrganizer.Reminders.application.export;

/// <summary>A rendered export ready to stream back to the client. Mirrors Attendance's <c>ExportFile</c>.</summary>
/// <param name="Content">Raw file bytes.</param>
/// <param name="ContentType">MIME type to send as the response content-type.</param>
/// <param name="FileName">Suggested download filename (with extension).</param>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName);