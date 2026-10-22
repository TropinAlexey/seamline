using Hangfire.Dashboard;

namespace Seamline.Reporting.Worker;

// Hangfire's default LocalRequestsOnlyAuthorizationFilter rejects every
// request from outside the container — including the developer's own
// browser on the Docker host. The dashboard is mapped only in Development
// (Program.cs), where letting anyone reach it is the point: it's how the
// demo triggers a job on demand instead of waiting for its daily schedule
// (ADR-0031). Never registered outside Development.
internal sealed class DevelopmentDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
