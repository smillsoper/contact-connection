namespace ContactConnection.Domain.Entities;

public class Role
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsBuiltIn { get; private set; }
    public List<string> Permissions { get; private set; } = [];
    public string DefaultLandingPage { get; private set; } = LandingPage.AgentPortal;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Role() { }

    public static Role Create(
        Guid tenantId,
        string name,
        List<string> permissions,
        string defaultLandingPage = LandingPage.AgentPortal,
        bool isBuiltIn = false)
    {
        return new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            IsBuiltIn = isBuiltIn,
            Permissions = permissions,
            DefaultLandingPage = defaultLandingPage,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Update(string name, List<string> permissions, string defaultLandingPage)
    {
        Name = name;
        Permissions = permissions;
        DefaultLandingPage = defaultLandingPage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}

public static class Permission
{
    public const string AgentsView   = "agents.view";
    public const string AgentsManage = "agents.manage";

    public const string RolesManage = "roles.manage";

    public const string FlowsView    = "flows.view";
    public const string FlowsManage  = "flows.manage";
    public const string FlowsPublish = "flows.publish";

    public const string TelephonyView   = "telephony.view";
    public const string TelephonyManage = "telephony.manage";

    public const string CallsView   = "calls.view";
    public const string CallsExport = "calls.export";

    public const string IntegrationsView   = "integrations.view";
    public const string IntegrationsManage = "integrations.manage";

    public const string SupervisorMonitor  = "supervisor.monitor";
    public const string SupervisorOverride = "supervisor.override";

    public const string ReportsView   = "reports.view";
    public const string ReportsManage = "reports.manage";

    public static readonly IReadOnlyList<string> All = [
        AgentsView, AgentsManage, RolesManage,
        FlowsView, FlowsManage, FlowsPublish,
        TelephonyView, TelephonyManage,
        CallsView, CallsExport,
        IntegrationsView, IntegrationsManage,
        SupervisorMonitor, SupervisorOverride,
        ReportsView, ReportsManage
    ];

    // Permissions derived from legacy AgentRole strings (for agents without a custom RoleId)
    public static IReadOnlyList<string> ForLegacyRole(string role) => role switch
    {
        AgentRole.Admin      => All.ToList(),
        AgentRole.Supervisor => [AgentsView, FlowsView, CallsView, CallsExport, SupervisorMonitor, SupervisorOverride, ReportsView],
        _                    => [CallsView]
    };
}

public static class LandingPage
{
    public const string AgentPortal    = "agent_portal";
    public const string AdminDashboard = "admin_dashboard";
    public const string FlowDesigner   = "flows";
    public const string Telephony      = "telephony";
    public const string Reports        = "reports";

    public static readonly IReadOnlyList<string> All = [
        AgentPortal, AdminDashboard, FlowDesigner, Telephony, Reports
    ];

    public static string ForLegacyRole(string role) => role switch
    {
        AgentRole.Admin      => AdminDashboard,
        AgentRole.Supervisor => AgentPortal,
        _                    => AgentPortal
    };
}
