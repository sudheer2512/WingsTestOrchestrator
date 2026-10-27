namespace Wings.Application.Security;

public static class Permissions
{
    public const string ClaimType = "perm";

    public const string FeaturesView = "features.view";
    public const string FeaturesEdit = "features.edit";
    public const string ProductsManage = "products.manage";
    public const string TestCasesView = "testcases.view";
    public const string TestCasesEdit = "testcases.edit";
    public const string TestCasesApprove = "testcases.approve";
    public const string TestCodeUpload = "testcode.upload";
    public const string TestCodeEdit = "testcode.edit";
    public const string TestCodeApprove = "testcode.approve";
    public const string TestCodePublish = "testcode.publish";
    public const string MasterDataManage = "masterdata.manage";
    public const string UsersManage = "users.manage";
    public const string AuditView = "audit.view";
    public const string TestsExecute = "tests.execute";
    public const string AgentsManage = "agents.manage";
    public const string EnvironmentsManage = "environments.manage";
    public const string ProductionRuns = "environments.production";
    public const string TestDataManage = "testdata.manage";
    public const string PlansManage = "plans.manage";
    public const string PlansApprove = "plans.approve";
    public const string SchedulesManage = "schedules.manage";
    public const string DefectsManage = "defects.manage";
    public const string ReleasesManage = "releases.manage";
    public const string ReleasesApprove = "releases.approve";
    public const string NotificationsManage = "notifications.manage";
    public const string IntegrationsManage = "integrations.manage";
    public const string ReportsView = "reports.view";
    public const string AiUse = "ai.use";
    public const string AiManage = "ai.manage";

    public static readonly IReadOnlyList<(string Code, string Description)> All =
    [
        (FeaturesView, "View products and features"),
        (FeaturesEdit, "Create, edit, move and deprecate features"),
        (ProductsManage, "Create and edit products and releases"),
        (TestCasesView, "View test cases, suites and test code"),
        (TestCasesEdit, "Create and edit test cases and suites"),
        (TestCasesApprove, "Approve or reject test cases"),
        (TestCodeUpload, "Upload test code packages"),
        (TestCodeEdit, "Edit test code (creates new versions)"),
        (TestCodeApprove, "Approve or reject test code packages"),
        (TestCodePublish, "Publish and deprecate test code packages"),
        (MasterDataManage, "Manage master data (types, statuses, frameworks)"),
        (UsersManage, "Manage users, roles and product access"),
        (AuditView, "Search the audit trail"),
        (TestsExecute, "Start, cancel and retry test executions"),
        (AgentsManage, "Register and manage execution agents"),
        (EnvironmentsManage, "Manage test environments, variables and secrets"),
        (ProductionRuns, "Start runs against production environments"),
        (TestDataManage, "Manage test data sets and records"),
        (PlansManage, "Create and run test plans and campaigns"),
        (PlansApprove, "Approve completion of test plans"),
        (SchedulesManage, "Create and change execution schedules and triggers"),
        (DefectsManage, "Create, update and transition defects; push them to issue trackers"),
        (ReleasesManage, "Edit release scope and quality gates; run release validation"),
        (ReleasesApprove, "Approve quality-gate exceptions and sign off releases"),
        (NotificationsManage, "Manage notification channels, rules, templates and escalation"),
        (IntegrationsManage, "Manage source repositories, integration keys and issue trackers"),
        (ReportsView, "View dashboards and run, save and export reports"),
        (AiUse, "Ask the AI testing assistant for suggestions (applying them needs the usual permissions)"),
        (AiManage, "Configure the AI assistant policy: enablement, data sent, masking, limits"),
    ];
}

public static class Roles
{
    public const string SystemAdministrator = "System Administrator";
    public const string QaAdministrator = "QA Administrator";
    public const string TestArchitect = "Test Architect";
    public const string TestDeveloper = "Test Developer";
    public const string Tester = "Tester";
    public const string DevelopmentLead = "Development Lead";
    public const string ProductManager = "Product Manager";
    public const string ReleaseManager = "Release Manager";
    public const string Auditor = "Auditor";
    public const string ReadOnly = "Read-only User";

    /// <summary>Default role → permission matrix seeded on first start. Editable afterwards through the role screen.</summary>
    /// <summary>Permissions introduced after the first release; added once to existing roles that the defaults grant them to.</summary>
    public static readonly IReadOnlyList<(int Version, string[] Codes)> Additions = [(2, [Permissions.TestsExecute, Permissions.AgentsManage]),
        (3, [Permissions.EnvironmentsManage, Permissions.ProductionRuns, Permissions.TestDataManage, Permissions.PlansManage, Permissions.PlansApprove, Permissions.SchedulesManage]),
        (4, [Permissions.DefectsManage, Permissions.ReleasesManage, Permissions.ReleasesApprove, Permissions.NotificationsManage, Permissions.IntegrationsManage, Permissions.ReportsView]),
        (5, [Permissions.AiUse, Permissions.AiManage])];

    public static readonly IReadOnlyDictionary<string, string[]> Defaults = new Dictionary<string, string[]>
    {
        [SystemAdministrator] = Permissions.All.Select(p => p.Code).ToArray(),
        [QaAdministrator] = [Permissions.FeaturesView, Permissions.FeaturesEdit, Permissions.ProductsManage, Permissions.TestCasesView, Permissions.TestCasesEdit, Permissions.TestCasesApprove, Permissions.TestCodeUpload, Permissions.TestCodeEdit, Permissions.TestCodeApprove, Permissions.TestCodePublish, Permissions.MasterDataManage, Permissions.AuditView, Permissions.TestsExecute, Permissions.AgentsManage, Permissions.EnvironmentsManage, Permissions.TestDataManage, Permissions.PlansManage, Permissions.PlansApprove, Permissions.SchedulesManage, Permissions.DefectsManage, Permissions.ReleasesManage, Permissions.ReleasesApprove, Permissions.NotificationsManage, Permissions.IntegrationsManage, Permissions.ReportsView, Permissions.AiUse, Permissions.AiManage],
        [TestArchitect] = [Permissions.FeaturesView, Permissions.FeaturesEdit, Permissions.TestCasesView, Permissions.TestCasesEdit, Permissions.TestCasesApprove, Permissions.TestCodeUpload, Permissions.TestCodeEdit, Permissions.TestCodeApprove, Permissions.TestsExecute, Permissions.TestDataManage, Permissions.PlansManage, Permissions.SchedulesManage, Permissions.DefectsManage, Permissions.ReleasesManage, Permissions.ReportsView, Permissions.AiUse],
        [TestDeveloper] = [Permissions.FeaturesView, Permissions.TestCasesView, Permissions.TestCasesEdit, Permissions.TestCodeUpload, Permissions.TestCodeEdit, Permissions.TestsExecute, Permissions.TestDataManage, Permissions.DefectsManage, Permissions.ReportsView, Permissions.AiUse],
        [Tester] = [Permissions.FeaturesView, Permissions.TestCasesView, Permissions.TestCasesEdit, Permissions.TestsExecute, Permissions.DefectsManage, Permissions.ReportsView, Permissions.AiUse],
        [DevelopmentLead] = [Permissions.FeaturesView, Permissions.FeaturesEdit, Permissions.TestCasesView, Permissions.DefectsManage, Permissions.IntegrationsManage, Permissions.ReportsView, Permissions.AiUse],
        [ProductManager] = [Permissions.FeaturesView, Permissions.FeaturesEdit, Permissions.ProductsManage, Permissions.TestCasesView, Permissions.ReleasesManage, Permissions.ReportsView],
        [ReleaseManager] = [Permissions.FeaturesView, Permissions.TestCasesView, Permissions.TestCodePublish, Permissions.AuditView, Permissions.TestsExecute, Permissions.PlansApprove, Permissions.ProductionRuns, Permissions.DefectsManage, Permissions.ReleasesManage, Permissions.ReleasesApprove, Permissions.ReportsView, Permissions.AiUse],
        [Auditor] = [Permissions.FeaturesView, Permissions.TestCasesView, Permissions.AuditView, Permissions.ReportsView],
        [ReadOnly] = [Permissions.FeaturesView, Permissions.TestCasesView],
    };
}
