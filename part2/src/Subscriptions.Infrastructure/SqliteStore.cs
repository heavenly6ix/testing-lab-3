using System.Text.Json;
using Microsoft.Data.Sqlite;
using Subscriptions.Application;
using Subscriptions.Domain;
namespace Subscriptions.Infrastructure;
public interface IWriteObserver { void AfterWrite(string kind); }
public sealed class NoWriteObserver : IWriteObserver { public void AfterWrite(string kind) { } }

public sealed class SqliteStore(string path,IWriteObserver? observer=null) : IStore
{
    public string Path { get; }=System.IO.Path.GetFullPath(path);
    private readonly object gate=new();
    public T Read<T>(Func<IData,T> query) => Run(query,false);
    public T Write<T>(Func<IData,T> work) => Run(work,true);
    private T Run<T>(Func<IData,T> work,bool write)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                using var connection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path,DefaultTimeout=2,Pooling=false }.ToString());
                connection.Open();
                using (var schema=connection.CreateCommand())
                {
                    schema.CommandText="""
                    CREATE TABLE IF NOT EXISTS Records(Kind TEXT NOT NULL,Id TEXT NOT NULL,UserId TEXT NOT NULL,UniqueKey TEXT,Json TEXT NOT NULL,PRIMARY KEY(Kind,Id));
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_Records_Unique ON Records(Kind,UniqueKey);
                    CREATE INDEX IF NOT EXISTS IX_Records_Owner ON Records(Kind,UserId);
                    """;
                    schema.ExecuteNonQuery();
                }
                using var transaction=connection.BeginTransaction(deferred:!write);
                var result=work(new SqliteData(connection,transaction,observer));
                transaction.Commit(); return result;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode==19) { throw BusinessFault.Conflict("conflict"); }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6 or 8 or 10 or 11 or 13 or 14 or 26) { throw new StorageUnavailableException(ex); }
            catch (IOException ex) { throw new StorageUnavailableException(ex); }
            catch (UnauthorizedAccessException ex) { throw new StorageUnavailableException(ex); }
        }
    }
    private sealed class SqliteData(SqliteConnection connection,SqliteTransaction transaction,IWriteObserver? observer) : IData
    {
        public T? Get<T>(Guid id) where T:Entity
        {
            using var cmd=Command("SELECT Json FROM Records WHERE Kind=$kind AND Id=$id");cmd.Parameters.AddWithValue("$kind",typeof(T).Name);cmd.Parameters.AddWithValue("$id",id.ToString());
            var json=cmd.ExecuteScalar() as string;return json is null?null:JsonSerializer.Deserialize<T>(json);
        }
        public IReadOnlyList<T> All<T>(Guid? owner=null) where T:Entity
        {
            using var cmd=Command("SELECT Json FROM Records WHERE Kind=$kind"+(owner.HasValue?" AND UserId=$owner":""));
            cmd.Parameters.AddWithValue("$kind",typeof(T).Name);
            if (owner.HasValue) cmd.Parameters.AddWithValue("$owner",owner.Value.ToString());
            using var reader=cmd.ExecuteReader();var result=new List<T>();
            while (reader.Read()) result.Add(JsonSerializer.Deserialize<T>(reader.GetString(0))!);
            return result;
        }
        public void Put<T>(T entity) where T:Entity
        {
            using var cmd=Command("""
            INSERT INTO Records(Kind,Id,UserId,UniqueKey,Json) VALUES($kind,$id,$owner,$unique,$json)
            ON CONFLICT(Kind,Id) DO UPDATE SET UserId=$owner,UniqueKey=$unique,Json=$json
            """);
            cmd.Parameters.AddWithValue("$kind",typeof(T).Name);cmd.Parameters.AddWithValue("$id",entity.Id.ToString());cmd.Parameters.AddWithValue("$owner",entity.UserId.ToString());
            cmd.Parameters.AddWithValue("$unique",(object?)entity.UniqueKey ?? DBNull.Value);cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(entity));cmd.ExecuteNonQuery();
            observer?.AfterWrite(typeof(T).Name);
        }
        private SqliteCommand Command(string sql) { var cmd=connection.CreateCommand();cmd.Transaction=transaction;cmd.CommandText=sql;return cmd; }
    }
}
