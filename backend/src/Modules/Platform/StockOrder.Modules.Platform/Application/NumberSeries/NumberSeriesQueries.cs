using Mediator;
using Microsoft.Data.SqlClient;
using StockOrder.Infrastructure.Database;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Platform.Application.NumberSeries;

public sealed record NumberSeriesSummary(
    Guid Id, string DocumentType, Guid? BranchId, string Prefix, long NextNumber, byte PaddingLength);

public sealed record GetNumberSeriesQuery : IQuery<Result<IReadOnlyList<NumberSeriesSummary>>>;

/// <summary>platform.NumberSeries belongs to Platform (section 4), queried on the request's shared connection.</summary>
public sealed class GetNumberSeriesQueryHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUser)
    : IQueryHandler<GetNumberSeriesQuery, Result<IReadOnlyList<NumberSeriesSummary>>>
{
    public async ValueTask<Result<IReadOnlyList<NumberSeriesSummary>>> Handle(GetNumberSeriesQuery query, CancellationToken ct)
    {
        await using var command = unitOfWork.Connection.CreateCommand();
        command.Transaction = unitOfWork.Transaction;
        command.CommandText = """
            SELECT Id, DocumentType, BranchId, Prefix, NextNumber, PaddingLength
            FROM platform.NumberSeries
            WHERE TenantId = @tenantId
            ORDER BY DocumentType;
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", currentUser.TenantId));

        var series = new List<NumberSeriesSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            series.Add(new NumberSeriesSummary(
                reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3), reader.GetInt64(4), reader.GetByte(5)));
        }

        return Result.Success<IReadOnlyList<NumberSeriesSummary>>(series);
    }
}

public sealed record UpdateNumberSeriesCommand(Guid Id, string Prefix, byte PaddingLength) : ICommand<Result>;

public sealed class UpdateNumberSeriesCommandHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUser)
    : ICommandHandler<UpdateNumberSeriesCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateNumberSeriesCommand command, CancellationToken ct)
    {
        await using var sqlCommand = unitOfWork.Connection.CreateCommand();
        sqlCommand.Transaction = unitOfWork.Transaction;
        sqlCommand.CommandText = """
            UPDATE platform.NumberSeries SET Prefix = @prefix, PaddingLength = @padding
            WHERE TenantId = @tenantId AND Id = @id;
            """;
        sqlCommand.Parameters.Add(new SqlParameter("@prefix", command.Prefix));
        sqlCommand.Parameters.Add(new SqlParameter("@padding", command.PaddingLength));
        sqlCommand.Parameters.Add(new SqlParameter("@tenantId", currentUser.TenantId));
        sqlCommand.Parameters.Add(new SqlParameter("@id", command.Id));

        var rows = await sqlCommand.ExecuteNonQueryAsync(ct);
        return rows == 0 ? Result.Failure(Error.NotFound("NumberSeries")) : Result.Success();
    }
}
