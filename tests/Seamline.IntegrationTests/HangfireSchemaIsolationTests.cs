using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Hangfire.Storage;
using Xunit;

namespace Seamline.IntegrationTests;

#pragma warning disable CS0618 // PostgreSqlStorage ctor is obsolete but functional in 1.x

public sealed class HangfireSchemaIsolationTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public void Recurring_jobs_in_one_schema_are_invisible_to_the_other()
    {
        var valuationStorage = new PostgreSqlStorage(
            factory.ConnectionString,
            new PostgreSqlStorageOptions { SchemaName = "hangfire_test_valuation", PrepareSchemaIfNecessary = true });

        var reportingStorage = new PostgreSqlStorage(
            factory.ConnectionString,
            new PostgreSqlStorageOptions { SchemaName = "hangfire_test_reporting", PrepareSchemaIfNecessary = true });

        using var valuationConnection = valuationStorage.GetConnection();
        using var reportingConnection = reportingStorage.GetConnection();

        var valuationManager = new RecurringJobManager(valuationStorage);
        var reportingManager = new RecurringJobManager(reportingStorage);

        valuationManager.AddOrUpdate("eod-valuation", Job.FromExpression(() => StubJob.Run()), Cron.Daily(), new RecurringJobOptions());
        reportingManager.AddOrUpdate("remit-reporting", Job.FromExpression(() => StubJob.Run()), Cron.Daily(), new RecurringJobOptions());

        var valuationJobs = valuationConnection.GetRecurringJobs();
        var reportingJobs = reportingConnection.GetRecurringJobs();

        Assert.Single(valuationJobs);
        Assert.Equal("eod-valuation", valuationJobs[0].Id);

        Assert.Single(reportingJobs);
        Assert.Equal("remit-reporting", reportingJobs[0].Id);
    }

    [Fact]
    public void Removing_a_job_from_one_schema_does_not_affect_the_other()
    {
        var storageA = new PostgreSqlStorage(
            factory.ConnectionString,
            new PostgreSqlStorageOptions { SchemaName = "hangfire_test_a", PrepareSchemaIfNecessary = true });

        var storageB = new PostgreSqlStorage(
            factory.ConnectionString,
            new PostgreSqlStorageOptions { SchemaName = "hangfire_test_b", PrepareSchemaIfNecessary = true });

        var managerA = new RecurringJobManager(storageA);
        var managerB = new RecurringJobManager(storageB);

        managerA.AddOrUpdate("shared-name", Job.FromExpression(() => StubJob.Run()), Cron.Daily(), new RecurringJobOptions());
        managerB.AddOrUpdate("shared-name", Job.FromExpression(() => StubJob.Run()), Cron.Daily(), new RecurringJobOptions());

        managerA.RemoveIfExists("shared-name");

        using var connectionA = storageA.GetConnection();
        using var connectionB = storageB.GetConnection();

        Assert.Empty(connectionA.GetRecurringJobs());
        Assert.Single(connectionB.GetRecurringJobs());
    }
}

public static class StubJob
{
    public static void Run() { }
}
