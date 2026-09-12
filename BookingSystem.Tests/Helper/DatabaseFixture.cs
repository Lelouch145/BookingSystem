using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Respawn;
using Respawn.Graph;

namespace BookingSystem.Tests;

public class DatabaseFixture : IAsyncLifetime
{

    private SqlConnection _connection = null!;
    private Respawner _respawner = null!;
    public async Task InitializeAsync()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        await dbContext.Database.MigrateAsync();
        
        var config = new ConfigurationBuilder().AddUserSecrets<DatabaseFixture>().Build();
        var connectionString = config.GetConnectionString("DefaultConnection");

        _connection = new SqlConnection(connectionString);
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            TablesToIgnore = new Respawn.Graph.Table[]
            {
                "__EFMigrationsHistory"
            }
        });
    }
    public async Task ResetDatabaseAsync()
    {
        await _respawner.ResetAsync(_connection);
    }
    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}