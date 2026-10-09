using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payroll.Core.Abstractions;
using Payroll.Core.Domain;
using Payroll.Core.Models;
namespace Payroll.Data.Infrastructure;

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = "";
    public int CommandTimeoutSeconds { get; set; } = 60;
}

public interface ISqlDatabase
{
    Task<int> ExecuteAsync(string proc, IReadOnlyList<Param>? ps, CancellationToken ct);
    Task<List<T>> QueryAsync<T>(string proc, Func<SqlDataReader, T> map, IReadOnlyList<Param>? ps, CancellationToken ct);
    Task QueryMultipleAsync(string proc, IReadOnlyList<Param>? ps, Func<SqlDataReader, CancellationToken, Task> handler, CancellationToken ct);
}

/// <summary>
/// The only place that touches ADO.NET. Every call is a stored procedure (CommandType.StoredProcedure) with typed parameters,
/// so SQL injection is structurally impossible. Supports an ambient transaction via <see cref="RunAsync{T}"/> and retries deadlocks.
/// </summary>
public sealed class SqlDatabase : ISqlDatabase, ITransactionRunner
{
    private sealed record Ambient(SqlConnection Connection, SqlTransaction Transaction);
    private static readonly AsyncLocal<Ambient?> Current = new();
    private const int DeadlockNumber = 1205;

    private readonly DatabaseOptions _opt;
    private readonly ILogger<SqlDatabase> _log;
    public SqlDatabase(IOptions<DatabaseOptions> options, ILogger<SqlDatabase> log) => (_opt, _log) = (options.Value, log);

    public Task<int> ExecuteAsync(string proc, IReadOnlyList<Param>? ps, CancellationToken ct) =>
        Run(proc, ps, async cmd => await cmd.ExecuteNonQueryAsync(ct), ct);

    public Task<List<T>> QueryAsync<T>(string proc, Func<SqlDataReader, T> map, IReadOnlyList<Param>? ps, CancellationToken ct) =>
        Run(proc, ps, async cmd =>
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<T>();
            while (await r.ReadAsync(ct)) list.Add(map(r));
            return list;
        }, ct);

    public Task QueryMultipleAsync(string proc, IReadOnlyList<Param>? ps, Func<SqlDataReader, CancellationToken, Task> handler, CancellationToken ct) =>
        Run<int>(proc, ps, async cmd =>
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await handler(r, ct);
            return 0;
        }, ct);

    /// <summary>Runs <paramref name="work"/> inside one SQL transaction; nested calls join the outer transaction.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        if (Current.Value is not null) return await work();
        await using var con = new SqlConnection(_opt.ConnectionString);
        await con.OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        Current.Value = new Ambient(con, tx);
        try
        {
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch (Exception ex) { _log.LogError(ex, "Rollback failed"); }
            throw;
        }
        finally { Current.Value = null; }
    }

    private async Task<T> Run<T>(string proc, IReadOnlyList<Param>? ps, Func<SqlCommand, Task<T>> action, CancellationToken ct)
    {
        var ambient = Current.Value;
        for (var attempt = 1; ; attempt++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (ambient is not null)
                {
                    await using var cmd = Build(ambient.Connection, ambient.Transaction, proc, ps);
                    var r = await action(cmd);
                    CopyOutputs(cmd, ps);
                    return r;
                }
                await using var con = new SqlConnection(_opt.ConnectionString);
                await con.OpenAsync(ct);
                await using var command = Build(con, null, proc, ps);
                var result = await action(command);
                CopyOutputs(command, ps);
                _log.LogDebug("{Proc} completed in {Ms} ms", proc, sw.ElapsedMilliseconds);
                return result;
            }
            catch (SqlException ex) when (ambient is null && ex.Number == DeadlockNumber && attempt < 3)
            {
                _log.LogWarning("Deadlock victim in {Proc}; retry {Attempt}", proc, attempt);
                await Task.Delay(100 * attempt, ct);
            }
            catch (SqlException ex) { throw Translate(proc, ex); }
        }
    }

    private SqlCommand Build(SqlConnection con, SqlTransaction? tx, string proc, IReadOnlyList<Param>? ps)
    {
        var cmd = con.CreateCommand();
        cmd.CommandText = proc;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = _opt.CommandTimeoutSeconds;
        cmd.Transaction = tx;
        if (ps is null) return cmd;
        foreach (var p in ps)
        {
            var sp = new SqlParameter("@" + p.Name, p.Type) { Direction = p.Direction };
            if (p.TypeName is not null) sp.TypeName = p.TypeName;
            if (p.Size > 0) sp.Size = p.Size;
            if (p.Type == SqlDbType.Decimal) { sp.Precision = 18; sp.Scale = p.Scale; }
            sp.Value = p.Value ?? DBNull.Value;
            cmd.Parameters.Add(sp);
        }
        return cmd;
    }

    private static void CopyOutputs(SqlCommand cmd, IReadOnlyList<Param>? ps)
    {
        if (ps is null) return;
        foreach (var p in ps.Where(p => p.Direction != ParameterDirection.Input))
        {
            var v = cmd.Parameters["@" + p.Name].Value;
            p.Value = v is DBNull ? null : v;
        }
    }

    /// <summary>Maps stored-procedure THROW numbers to domain exceptions; anything else is logged and wrapped (message never leaks SQL details).</summary>
    private Exception Translate(string proc, SqlException ex) => ex.Number switch
    {
        50001 => new NotFoundException(ex.Message),
        50010 => new ConcurrencyException(ex.Message),
        50020 or 50021 or 50022 => new ForbiddenException(ex.Message),
        >= 50000 => new BusinessRuleException(ex.Message, ex.Number),
        2627 or 2601 => new BusinessRuleException("A record with the same unique value already exists.", ex.Number),
        547 => new BusinessRuleException("The operation conflicts with related data.", ex.Number),
        _ => LogAndWrap(proc, ex)
    };
    private Exception LogAndWrap(string proc, SqlException ex)
    {
        _log.LogError(ex, "SQL error {Number} in {Proc}", ex.Number, proc);
        return new DataAccessException("A database error occurred. Please try again or contact support.", ex);
    }
}

internal static class SqlDatabaseExtensions
{
    public static async Task<T?> QuerySingleAsync<T>(this ISqlDatabase db, string proc, Func<SqlDataReader, T> map, IReadOnlyList<Param>? ps, CancellationToken ct) where T : class
        => (await db.QueryAsync(proc, map, ps, ct)).FirstOrDefault();

    public static async Task<PagedResult<T>> QueryPagedAsync<T>(this ISqlDatabase db, string proc, Func<SqlDataReader, T> map, IReadOnlyList<Param> ps, int page, int pageSize, CancellationToken ct)
    {
        var rows = await db.QueryAsync(proc, r => (Item: map(r), Total: r.Int("TotalCount")), ps, ct);
        return new PagedResult<T>(rows.Select(x => x.Item).ToList(), rows.Count == 0 ? 0 : rows[0].Total, page, pageSize);
    }
}
