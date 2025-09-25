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

                var tableNames = await GetTableNamesAsync(context);
                logger.LogInformation("Database contains {Count} tables: {Tables}",
                    tableNames.Count, string.Join(", ", tableNames));

                logger.LogInformation("Checking for existing data source configurations...");
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
                        PollingIntervalMinutes = 5,
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
