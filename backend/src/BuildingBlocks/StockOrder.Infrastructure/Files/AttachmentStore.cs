using System.Security.Cryptography;
using Dapper;
using StockOrder.Infrastructure.Database;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Ids;

namespace StockOrder.Infrastructure.Files;

public sealed record StoredAttachment(Guid Id, string FileName, string ContentType, long SizeBytes, string StoragePath);

/// <summary>
/// platform.Attachment rows plus their files in the NTFS store (section 2 "Files"), for the
/// current tenant. The row is written in the request's transaction; the file is written first,
/// so a rolled-back request can leave an orphan file but never a row without its file.
/// </summary>
public interface IAttachmentStore
{
    Task<StoredAttachment> SaveAsync(string entityName, Guid entityId, string fileName, string contentType, byte[] content, CancellationToken ct);

    Task<StoredAttachment?> FindAsync(Guid attachmentId, CancellationToken ct);

    Task<Stream> OpenReadAsync(StoredAttachment attachment, CancellationToken ct);
}

internal sealed class AttachmentStore(
    IUnitOfWork unitOfWork, ICurrentUserContext currentUser, IFileStore files, IIdGenerator idGenerator) : IAttachmentStore
{
    public async Task<StoredAttachment> SaveAsync(
        string entityName, Guid entityId, string fileName, string contentType, byte[] content, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        string storagePath;
        using (var stream = new MemoryStream(content, writable: false))
        {
            // One folder per tenant keeps each company's files apart on disk.
            storagePath = await files.SaveAsync(tenantId.ToString("N"), fileName, stream, ct);
        }

        var attachment = new StoredAttachment(idGenerator.NewId(), fileName, contentType, content.LongLength, storagePath);
        await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT platform.Attachment (TenantId, Id, FileName, ContentType, SizeBytes, StoragePath, Sha256, EntityName, EntityId, UploadedBy)
            VALUES (@tenantId, @id, @fileName, @contentType, @sizeBytes, @storagePath, @sha256, @entityName, @entityId, @uploadedBy);
            """,
            new
            {
                tenantId,
                id = attachment.Id,
                fileName,
                contentType,
                sizeBytes = attachment.SizeBytes,
                storagePath,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
                entityName,
                entityId,
                uploadedBy = currentUser.UserId,
            },
            unitOfWork.Transaction,
            cancellationToken: ct));

        return attachment;
    }

    public async Task<StoredAttachment?> FindAsync(Guid attachmentId, CancellationToken ct) =>
        await unitOfWork.Connection.QuerySingleOrDefaultAsync<StoredAttachment>(new CommandDefinition(
            """
            SELECT Id, FileName, ContentType, SizeBytes, StoragePath
            FROM platform.Attachment
            WHERE TenantId = @tenantId AND Id = @attachmentId;
            """,
            new { tenantId = currentUser.TenantId, attachmentId },
            unitOfWork.Transaction,
            cancellationToken: ct));

    public Task<Stream> OpenReadAsync(StoredAttachment attachment, CancellationToken ct) => files.OpenReadAsync(attachment.StoragePath, ct);
}
