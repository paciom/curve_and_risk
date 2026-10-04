using System.Data.Common;

namespace Shop.Accounts;

public sealed class UserRepository(DbConnection connection)
{
    public async Task<IReadOnlyList<UserSummary>> SearchAsync(string nameFilter, string sortColumn, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT id, display_name, email FROM users WHERE display_name LIKE @filter ORDER BY {sortColumn}";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@filter";
        parameter.Value = $"%{nameFilter}%";
        command.Parameters.Add(parameter);

        var results = new List<UserSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new UserSummary(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return results;
    }
}
