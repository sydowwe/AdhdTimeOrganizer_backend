// using brutto.domain.helper;
// using brutto.domain.model.dto;
// using FastEndpoints;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.Logging;
// using MojaDigitalnaFirma.AdminPortal.application.commands;
// using MojaDigitalnaFirma.AdminPortal.infrastructure.persistence;
//
// namespace MojaDigitalnaFirma.AdminPortal.application.endpoint.servis.command;
//
// public class ServisUploadAttachmentsEndpoint(AppCommandDbContext dbContext, IConfiguration configuration, ILogger<ServisUploadAttachmentsEndpoint> logger)
//     : EndpointWithoutRequest
// {
//     public override void Configure()
//     {
//         Patch("/servis/{id:long:required}/attachments");
//         AllowFileUploads();
//
//         Summary(s =>
//         {
//             s.Summary = "Upload attachments";
//             s.Description = "Upload attachments to a servis";
//             // s.Response<ServisProtocolResponse>(200, "Uploaded attachments");
//             s.Response(400, "Bad request");
//         });
//     }
//
//     public override async Task HandleAsync(CancellationToken ct)
//     {
//         if (Files.Count == 0)
//         {
//             AddError("No files were uploaded");
//             await SendErrorsAsync(cancellation: ct);
//             return;
//         }
//
//         var id = Route<long>("id");
//         var servis = await dbContext.Servis.FindAsync([id], ct);
//
//         if (servis == null)
//         {
//             await SendNotFoundAsync(ct);
//             return;
//         }
//
//         List<AttachmentDto> attachments = [];
//         foreach (var file in Files)
//         {
//             try
//             {
//                 using var memoryStream = new MemoryStream();
//                 await file.OpenReadStream().CopyToAsync(memoryStream, ct);
//                 var fileBytes = memoryStream.ToArray();
//
//                 var uploadCommand = new UploadToSharePointCommand
//                 {
//                     FileBytes = fileBytes,
//                     FileName = file.FileName,
//                     ContentType = file.ContentType,
//                     DriveId = configuration.GetSpPrilohyDriveId(),
//                     FolderPath = $"{servis.ReceivedDate.Year}/{servis.ReceivedDate.Month:00}/s-{servis.ServiceNumber}"
//                 };
//
//                 var uploadResult = await uploadCommand.ExecuteAsync(ct);
//
//                 if (uploadResult.Failed || string.IsNullOrEmpty(uploadResult.Data.WebUrl))
//                 {
//                     logger.LogError("Failed to upload file {FileName}: {Error}",
//                         file.FileName, uploadResult.ErrorMessage);
//                     AddError($"Failed to upload {file.FileName}: {uploadResult.ErrorMessage}");
//                     await SendErrorsAsync(500, ct);
//                     return;
//                 }
//
//                 attachments.Add(new AttachmentDto
//                 {
//                     Id = uploadResult.Data.Id,
//                     Name = file.FileName,
//                     Url = uploadResult.Data.WebUrl,
//                     Type = AttachmentDto.GetType(file.ContentType)
//                 });
//             }
//             catch (Exception ex)
//             {
//                 logger.LogError(ex, "Error processing file {FileName}", file.FileName);
//                 AddError($"Error processing {file.FileName}: {ex.Message}");
//                 await SendErrorsAsync(500, ct);
//                 return;
//             }
//         }
//
//         try
//         {
//             servis.AttachmentList ??= [];
//             servis.AttachmentList.AddRange(attachments);
//             dbContext.Update(servis);
//             await dbContext.SaveChangesAsync(ct);
//         }
//         catch (Exception ex)
//         {
//             logger.LogError(ex, "Failed to save servis attachments to database");
//             AddError($"Failed to save attachment information to database: {ex.Message}");
//             await SendErrorsAsync(500, ct);
//             return;
//         }
//
//         await SendNoContentAsync(ct);
//     }
// }

