using APIMonitorWorkerService.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIMonitorWorkerService.Data
{
    public class DatabaseInitializer
    {
        public static async Task InitializeAsync(AppDbContext context, ILogger logger)
        {
            logger.LogInformation("=== Starting Database Initialization ===");

            try
            {
                logger.LogInformation("Testing database connection...");
                var canConnect = await context.Database.CanConnectAsync();
                if (!canConnect)
                {
                    logger.LogWarning("Cannot connect to database, attempting to create...");
                }
                else
                {
                    logger.LogInformation("Database connection test successful");
                }

                logger.LogInformation("Ensuring database exists and is up to date...");
                var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                if (pendingMigrations.Any())
                {
                    logger.LogInformation("Found {Count} pending migrations: {Migrations}",
                        pendingMigrations.Count(), string.Join(", ", pendingMigrations));
                }
                else
                {
                    logger.LogInformation("No pending migrations found");
                }

                await context.Database.EnsureCreatedAsync();
                logger.LogInformation("Database schema ensured successfully");

                // Ensure all required tables exist
                var allTablesExist = await EnsureAllTablesExistAsync(context, logger);
                if (!allTablesExist)
                {
                    logger.LogError("Failed to ensure all required tables exist. Database initialization is incomplete.");
                    logger.LogError("Application may not function correctly without all required tables.");
                    return;
                }

                var tableNames = await GetTableNamesAsync(context);
                logger.LogInformation("Database contains {Count} tables: {Tables}",
                    tableNames.Count, string.Join(", ", tableNames));

                logger.LogInformation("Checking for APIDataSourceConfig table and existing data...");
                
                try
                {
                    var existingConfigs = await context.APIDataSourceConfigs.CountAsync();
                    logger.LogInformation("Found {Count} existing data source configurations", existingConfigs);

                    if (!await context.APIDataSourceConfigs.AnyAsync())
                    {
                        logger.LogInformation("No data source configurations found, seeding defaults...");
                        await SeedDataSourcesIfEmptyAsync(context, logger);
                    }
                    else
                    {
                        logger.LogInformation("Data source configurations already exist, skipping seeding");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error accessing APIDataSourceConfig table");
                }

                logger.LogInformation("Seeding essential configuration values...");
                await SeedEssentialConfigurationsAsync(context, logger);

                logger.LogInformation("=== Database Initialization Complete ===");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while initializing the database");
                throw;
            }
        }

        private static async Task<List<string>> GetTableNamesAsync(AppDbContext context)
        {
            try
            {
                var tableNames = new List<string>();
                using var connection = context.Database.GetDbConnection();
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";

                using var result = await command.ExecuteReaderAsync();
                while (await result.ReadAsync())
                {
                    tableNames.Add(result.GetString(0));
                }

                return tableNames;
            }
            catch
            {
                return new List<string>();
            }
        }

        private static async Task<bool> CheckIfTableExistsAsync(AppDbContext context, string tableName)
        {
            try
            {
                using var connection = context.Database.GetDbConnection();
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@tableName";
                
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@tableName";
                parameter.Value = tableName;
                command.Parameters.Add(parameter);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
            catch (Exception ex)
            {
                // Log the exception but don't throw - this is a helper method
                return false;
            }
        }

        /// <summary>
        /// Ensures all required tables exist in the database. Creates them if they don't exist.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="logger">Logger instance</param>
        /// <returns>True if all tables exist or were created successfully</returns>
        public static async Task<bool> EnsureAllTablesExistAsync(AppDbContext context, ILogger logger)
        {
            try
            {
                logger.LogInformation("Checking if all required tables exist...");
                
                var requiredTables = new[]
                {
                    "APIDataSourceConfigs",
                    "Configurations", 
                    "APIMonitorServiceHeartBeats"
                };

                var missingTables = new List<string>();
                
                foreach (var tableName in requiredTables)
                {
                    var exists = await CheckIfTableExistsAsync(context, tableName);
                    if (!exists)
                    {
                        missingTables.Add(tableName);
                        logger.LogWarning("Table '{TableName}' does not exist", tableName);
                    }
                    else
                    {
                        logger.LogInformation("Table '{TableName}' exists", tableName);
                    }
                }

                if (missingTables.Count == 0)
                {
                    logger.LogInformation("All required tables exist");
                    return true;
                }

                logger.LogInformation("Creating {Count} missing tables: {Tables}", 
                    missingTables.Count, string.Join(", ", missingTables));

                using var connection = context.Database.GetDbConnection();
                await connection.OpenAsync();

                foreach (var tableName in missingTables)
                {
                    try
                    {
                        var createTableSql = GetCreateTableSql(tableName);
                        var command = connection.CreateCommand();
                        command.CommandText = createTableSql;
                        await command.ExecuteNonQueryAsync();
                        
                        logger.LogInformation("Successfully created table '{TableName}'", tableName);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to create table '{TableName}'", tableName);
                        return false;
                    }
                }

                logger.LogInformation("All required tables have been created successfully");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error ensuring all tables exist");
                return false;
            }
        }

        private static async Task CreateAPIDataSourceConfigTableManually(AppDbContext context, ILogger logger)
        {
            try
            {
                using var connection = context.Database.GetDbConnection();
                await connection.OpenAsync();

                var createTableSql = GetCreateTableSql("APIDataSourceConfigs");

                var command = connection.CreateCommand();
                command.CommandText = createTableSql;
                await command.ExecuteNonQueryAsync();

                logger.LogInformation("APIDataSourceConfig table created manually");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create APIDataSourceConfig table manually");
                throw;
            }
        }

        /// <summary>
        /// Gets the SQL CREATE TABLE statement for the specified table name
        /// </summary>
        /// <param name="tableName">Name of the table</param>
        /// <returns>SQL CREATE TABLE statement</returns>
        private static string GetCreateTableSql(string tableName)
        {
            return tableName switch
            {
                "APIDataSourceConfigs" => @"
                    CREATE TABLE IF NOT EXISTS APIDataSourceConfigs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        IsEnabled INTEGER NOT NULL DEFAULT 1,
                        IsRefreshing INTEGER NOT NULL DEFAULT 1,
                        TempFolderPath TEXT,
                        ApiEndpoint TEXT,
                        ApiKey TEXT,
                        PollingIntervalMinutes INTEGER NOT NULL DEFAULT 5,
                        CreatedAt TEXT NOT NULL,
                        LastProcessedAt TEXT,
                        AdditionalSettings TEXT
                    )",
                "Configurations" => @"
                    CREATE TABLE IF NOT EXISTS Configurations (
                        Key TEXT PRIMARY KEY,
                        Value TEXT NOT NULL,
                        Description TEXT,
                        UpdatedAt TEXT NOT NULL,
                        Category TEXT,
                        IsEncrypted INTEGER NOT NULL DEFAULT 0
                    )",
                "APIMonitorServiceHeartBeats" => @"
                    CREATE TABLE IF NOT EXISTS APIMonitorServiceHeartBeats (
                        Id INTEGER PRIMARY KEY,
                        LastRun TEXT
                    )",
                _ => throw new ArgumentException($"Unknown table name: {tableName}")
            };
        }

        private static async Task SeedDataSourcesIfEmptyAsync(AppDbContext context, ILogger logger)
        {
            try
            {
                var hasAny = await context.APIDataSourceConfigs.AnyAsync();

                if (hasAny)
                {
                    logger.LogInformation("Data source configs already exist. Skipping seeding.");
                    return;
                }

                logger.LogInformation("Seeding default data source configurations...");

                var defaultSources = new[]
                {
                    new APIDataSourceConfig
                    {
                        Name = "APIMonitor1",
                        IsEnabled = false,
                        IsRefreshing = false,
                        TempFolderPath = "",
                        ApiEndpoint="",
                        ApiKey="",
                        AdditionalSettings="",
                        LastProcessedAt = null,
                        PollingIntervalMinutes = 1,
                        CreatedAt = DateTime.UtcNow
                    }
                };

                await context.APIDataSourceConfigs.AddRangeAsync(defaultSources);
                await context.SaveChangesAsync();
                logger.LogInformation("Successfully seeded {Count} default data source configurations", defaultSources.Length);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error seeding data source configurations");
            }
        }

        private static async Task SeedEssentialConfigurationsAsync(AppDbContext context, ILogger logger)
        {
            try
            {
                // Ensure a minimal set of configuration keys exist if missing
                var defaults = new List<Configuration>
                {
                    new Configuration { Key = Constants.ProcessingIntervalSeconds, Value = "10", Category = "App", Description = "Default processing interval (seconds)" },
                };

                foreach (var item in defaults)
                {
                    var exists = await context.Configurations.AnyAsync(c => c.Key == item.Key);
                    if (!exists)
                    {
                        await context.Configurations.AddAsync(item);
                    }
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error seeding essential configuration values");
            }
        }
    }
}
