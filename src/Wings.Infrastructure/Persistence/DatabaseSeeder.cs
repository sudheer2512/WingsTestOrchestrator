using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wings.Application.Features;
using Wings.Application.Security;
using Wings.Application.Testing;
using Wings.Domain.Features;
using Wings.Domain.Testing;
using Wings.Infrastructure.Identity;

namespace Wings.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and seeds reference data (types, statuses, frameworks, roles, numbering scopes). Users and the
/// sample Wings product tree are seeded only when configured, never with built-in credentials.
/// </summary>
public sealed class DatabaseSeeder(
    WingsDbContext db, UserManager<ApplicationUser> users, RoleManager<ApplicationRole> roles, IConfiguration config, ILogger<DatabaseSeeder> log,
    IProductService products, IFeatureService features, ITestCaseService testCases, ITestSuiteService suites)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (db.Database.IsRelational()) await db.Database.MigrateAsync(ct);
        if (db.Database.IsSqlite()) await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);

        await SeedReferenceDataAsync(ct);
        await SeedRolesAsync();
        await SeedUsersAsync();
        if (config.GetValue("Seed:SampleData", false)) await SeedSampleDataAsync(ct);
    }

    private async Task SeedReferenceDataAsync(CancellationToken ct)
    {
        (string Code, string Name, bool Container)[] featureTypes =
        [
            (FeatureTypes.Module, "Module", true), (FeatureTypes.Submodule, "Submodule", true), (FeatureTypes.Feature, "Feature", false),
            (FeatureTypes.Subfeature, "Subfeature", false), (FeatureTypes.Operation, "Operation", false),
        ];
        for (var i = 0; i < featureTypes.Length; i++)
            if (!await db.FeatureTypes.AnyAsync(t => t.Code == featureTypes[i].Code, ct))
                db.FeatureTypes.Add(new FeatureType { Code = featureTypes[i].Code, Name = featureTypes[i].Name, IsContainer = featureTypes[i].Container, SortOrder = (i + 1) * 10 });

        (string Code, string Name, bool Terminal)[] statuses =
        [
            (FeatureStatuses.Planned, "Planned", false), (FeatureStatuses.InDevelopment, "In development", false), (FeatureStatuses.Active, "Active", false),
            (FeatureStatuses.Deprecated, "Deprecated", false), (FeatureStatuses.Retired, "Retired", true),
        ];
        for (var i = 0; i < statuses.Length; i++)
            if (!await db.FeatureStatuses.AnyAsync(t => t.Code == statuses[i].Code, ct))
                db.FeatureStatuses.Add(new FeatureStatus { Code = statuses[i].Code, Name = statuses[i].Name, IsTerminal = statuses[i].Terminal, SortOrder = (i + 1) * 10 });

        string[] testTypes =
        [
            "Unit", "Functional", "Integration", "End-to-end", "Regression", "Smoke", "Sanity", "Acceptance", "Negative", "Boundary-value", "Security",
            "Performance", "Load", "Database consistency", "User permission", "Installation and upgrade", "Cross-version compatibility", "Accessibility",
        ];
        for (var i = 0; i < testTypes.Length; i++)
        {
            var code = testTypes[i].ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
            if (!await db.TestTypes.AnyAsync(t => t.Code == code, ct)) db.TestTypes.Add(new TestType { Code = code, Name = testTypes[i], SortOrder = (i + 1) * 10 });
        }

        (string Code, string Name, string Language, ApplicationType Platform)[] frameworks =
        [
            ("FLAUI", "FlaUI (Windows UI Automation)", "csharp", ApplicationType.WinForms),
            ("PLAYWRIGHT_CS", "Playwright for .NET", "csharp", ApplicationType.Browser),
            ("PLAYWRIGHT_TS", "Playwright Test (TypeScript/JavaScript)", "typescript", ApplicationType.Browser),
            ("PLAYWRIGHT_PY", "Playwright for Python", "python", ApplicationType.Browser),
            ("HTTP_API", ".NET HttpClient API tests", "csharp", ApplicationType.Api),
            ("XUNIT", "xUnit.net", "csharp", ApplicationType.None),
            ("NUNIT", "NUnit", "csharp", ApplicationType.None),
            ("MSTEST", "MSTest", "csharp", ApplicationType.None),
            ("PYTEST", "pytest", "python", ApplicationType.None),
            ("STRUCTURED", "Structured definition (JSON/YAML)", "structured", ApplicationType.None),
        ];
        foreach (var f in frameworks)
            if (!await db.TestingFrameworks.AnyAsync(t => t.Code == f.Code, ct))
                db.TestingFrameworks.Add(new TestingFramework { Code = f.Code, Name = f.Name, Language = f.Language, Platform = f.Platform });

        foreach (var (key, name, prefix) in new[] { (TestCaseService.ScopeKey, "Test cases", "TC-"), (TestSuiteService.ScopeKey, "Test suites", "TS-"), (PackageService.ScopeKey, "Test packages", "PKG-"), (Application.Execution.ExecutionService.ScopeKey, "Execution runs", "RUN-"), (Application.Orchestration.PlanService.ScopeKey, "Test plans", "PLAN-"), (Application.Orchestration.CampaignService.ScopeKey, "Test campaigns", "CMP-"), (Application.Quality.DefectService.ScopeKey, "Defects", "DEF-") })
            if (!await db.SequenceScopes.AnyAsync(s => s.Key == key, ct))
                db.SequenceScopes.Add(new SequenceScope { Key = key, Name = name, Prefix = prefix, DisplayWidth = 5 });

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedRolesAsync()
    {
        const string seedMarker = "perm-seed";
        var latest = Roles.Additions.Max(a => a.Version);
        foreach (var (roleName, perms) in Roles.Defaults)
        {
            var role = await roles.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new ApplicationRole(roleName);
                await roles.CreateAsync(role);
                foreach (var p in perms) await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, p));
                await roles.AddClaimAsync(role, new Claim(seedMarker, latest.ToString()));
                continue;
            }
            // Existing roles keep administrator edits; only permissions introduced by later versions are added, once.
            var claims = await roles.GetClaimsAsync(role);
            var seeded = claims.Where(c => c.Type == seedMarker).Select(c => int.Parse(c.Value)).DefaultIfEmpty(1).Max();
            foreach (var (version, codes) in Roles.Additions.Where(a => a.Version > seeded))
                foreach (var code in codes.Where(c => perms.Contains(c) && claims.All(x => x.Type != Permissions.ClaimType || x.Value != c)))
                    await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, code));
            foreach (var old in claims.Where(c => c.Type == seedMarker)) await roles.RemoveClaimAsync(role, old);
            await roles.AddClaimAsync(role, new Claim(seedMarker, latest.ToString()));
        }
    }

    /// <summary>Seeds users listed under Seed:Users (UserName, DisplayName, Password, Role) when no user with that name exists.</summary>
    private async Task SeedUsersAsync()
    {
        foreach (var section in config.GetSection("Seed:Users").GetChildren())
        {
            var userName = section["UserName"];
            var password = section["Password"];
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password) || await users.FindByNameAsync(userName) is not null) continue;
            var user = new ApplicationUser { UserName = userName, DisplayName = section["DisplayName"] ?? userName, Email = section["Email"], CreatedAt = DateTimeOffset.UtcNow };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded) { log.LogWarning("Could not seed user {User}: {Errors}", userName, string.Join("; ", result.Errors.Select(e => e.Description))); continue; }
            if (section["Role"] is { } role) await users.AddToRoleAsync(user, role);
        }
    }

    /// <summary>Clearly labelled sample data mirroring the Wings product structure, for demonstration and development.</summary>
    private async Task SeedSampleDataAsync(CancellationToken ct)
    {
        if (await db.Products.AnyAsync(ct)) return;
        log.LogInformation("Seeding sample Wings product data");

        var auto = await products.CreateAsync(new ProductUpsert("AUTO", "Wings Auto", "SAMPLE DATA – automobile dealership management (sales, service, inventory)."), ct);
        foreach (var (code, name) in new[] { ("BOOKS", "Wings Books"), ("PAYROLL", "Wings Payroll"), ("POS", "Wings POS"), ("FA", "Wings Fixed Assets"), ("ERP", "Wings ERP Classic") })
            await products.CreateAsync(new ProductUpsert(code, name, "SAMPLE DATA"), ct);
        await products.CreateVersionAsync(auto.Id, new ProductVersionUpsert("2026.1", new DateOnly(2026, 3, 31), true, "Sample released version"), ct);
        var next = await products.CreateVersionAsync(auto.Id, new ProductVersionUpsert("2026.2", null, false, "Sample upcoming release"), ct);

        async Task<Guid> Node(Guid? parent, string name, string type, ApplicationType app = ApplicationType.None, FeatureFields? extra = null)
            => (await features.CreateAsync(new CreateFeatureRequest(auto.Id, parent, (extra ?? new FeatureFields()) with
            {
                Name = name, FeatureTypeCode = type, StatusCode = FeatureStatuses.Active, ApplicationType = app,
            }), ct)).Id;

        var sales = await Node(null, "Vehicle Sales", FeatureTypes.Module);
        var enquiries = await Node(sales, "Enquiries", FeatureTypes.Submodule);
        var createEnquiry = await Node(enquiries, "Create Enquiry", FeatureTypes.Feature, ApplicationType.WinForms, new FeatureFields
        {
            Description = "Records a new customer enquiry for a vehicle model, capturing contact details, model interest and source.",
            BusinessObjective = "Capture every walk-in, phone and web lead so that follow-up and conversion can be tracked.",
            AssemblyName = "Wings.Auto.Sales.dll", Namespace = "Wings.Auto.Sales.Enquiries", ClassName = "frmEnquiry", FormOrRoute = "frmEnquiry",
            DatabaseObjects = "Tables: SalesEnquiry, Customer\nProcedures: usp_SalesEnquiry_Insert",
            Preconditions = "User has the Sales Executive role; at least one vehicle model is configured.",
            BusinessRules = "- Mobile number is mandatory and must be 10 digits\n- Duplicate enquiries for the same mobile and model within 30 days raise a warning",
            ValidationRules = "- Customer name required\n- Expected purchase date cannot be in the past",
            UserRoles = "Sales Executive, Sales Manager",
        });
        await Node(enquiries, "Modify Enquiry", FeatureTypes.Feature, ApplicationType.WinForms);
        await Node(enquiries, "Enquiry Follow-up", FeatureTypes.Feature, ApplicationType.WinForms);
        var convert = await Node(enquiries, "Convert Enquiry to Booking", FeatureTypes.Feature, ApplicationType.WinForms);
        var bookings = await Node(sales, "Bookings", FeatureTypes.Submodule);
        var createBooking = await Node(bookings, "Create Booking", FeatureTypes.Feature, ApplicationType.WinForms);
        await Node(bookings, "Cancel Booking", FeatureTypes.Feature, ApplicationType.WinForms);
        await Node(bookings, "Booking Amendment", FeatureTypes.Feature, ApplicationType.WinForms);
        var invoicing = await Node(sales, "Vehicle Invoicing", FeatureTypes.Submodule);
        var invoice = await Node(invoicing, "Generate Invoice", FeatureTypes.Feature, ApplicationType.WinForms, new FeatureFields
        {
            Description = "Generates the vehicle sales invoice from a confirmed booking, applying price list, discounts and GST.",
            BusinessRules = "- Tax calculation per HSN and place of supply\n- Discount cannot exceed the user's approval limit\n- Ledger posting to sales, output GST and customer accounts",
            ApiEndpoints = "POST /api/sales/invoices", DatabaseObjects = "Tables: SalesInvoice, SalesInvoiceLine, LedgerEntry",
            IntroducedInVersionId = next.Id,
        });
        var taxes = await Node(invoicing, "Calculate Taxes", FeatureTypes.Feature, ApplicationType.Service);
        await Node(invoicing, "Apply Discounts", FeatureTypes.Feature, ApplicationType.WinForms);
        await Node(invoicing, "Cancel Invoice", FeatureTypes.Feature, ApplicationType.WinForms);
        var service = await Node(null, "Service", FeatureTypes.Module);
        var jobCards = await Node(service, "Job Cards", FeatureTypes.Submodule);
        foreach (var n in new[] { "Open Job Card", "Add Labour", "Issue Spare Parts", "Close Job Card" }) await Node(jobCards, n, FeatureTypes.Feature, ApplicationType.WinForms);
        var inventory = await Node(null, "Inventory", FeatureTypes.Module);
        foreach (var n in new[] { "Purchases", "Stock Transfers", "Stock Valuation" }) await Node(inventory, n, FeatureTypes.Submodule);
        var portal = await Node(null, "Customer Portal", FeatureTypes.Module, ApplicationType.Browser);
        var portalBooking = await Node(portal, "Online Booking", FeatureTypes.Feature, ApplicationType.Browser, new FeatureFields { FormOrRoute = "/booking/new", Description = "Angular customer portal page for booking a test drive or vehicle." });

        await features.AddRequirementAsync(createEnquiry, new RequirementUpsert("REQ-SAL-001", "Capture enquiry with mandatory contact details", null, "HIGH"), ct);
        await features.AddDependencyAsync(convert, new DependencyUpsert(createEnquiry, DependencyType.Prerequisite, "An enquiry must exist before conversion"), ct);
        await features.AddDependencyAsync(createBooking, new DependencyUpsert(convert, DependencyType.Prerequisite, null), ct);
        await features.AddDependencyAsync(invoice, new DependencyUpsert(createBooking, DependencyType.Prerequisite, null), ct);
        await features.AddDependencyAsync(invoice, new DependencyUpsert(taxes, DependencyType.SetupDependency, null), ct);
        await features.AddSourceMappingAsync(invoice, new SourceMappingUpsert(SourceMappingKind.Class, "Wings.Auto.Sales.Invoicing.InvoiceCalculator", "wings-auto", "Calculation service"), ct);

        var smoke = await suites.CreateAsync(new SuiteUpsert("Wings Auto Daily Smoke Tests", "SAMPLE – critical path checks run after every build.", auto.Id, "smoke,daily", null), ct);
        var created = new List<Guid>();
        foreach (var (name, feature, type, automation) in new[]
                 {
                     ("Create enquiry with valid mandatory fields", createEnquiry, "FUNCTIONAL", AutomationStatus.AutomationPlanned),
                     ("Reject enquiry without mobile number", createEnquiry, "NEGATIVE", AutomationStatus.Manual),
                     ("Mobile number boundary: 9, 10 and 11 digits", createEnquiry, "BOUNDARY_VALUE", AutomationStatus.Manual),
                     ("Invoice GST calculation for intra-state sale", invoice, "FUNCTIONAL", AutomationStatus.Manual),
                     ("Invoice posts balanced ledger entries", invoice, "DATABASE_CONSISTENCY", AutomationStatus.Manual),
                     ("Portal booking happy path", portalBooking, "END_TO_END", AutomationStatus.AutomationPlanned),
                 })
        {
            var tc = await testCases.CreateAsync(new SaveTestCaseRequest(new TestCaseFields
            {
                Name = name, TestTypeCode = type, AutomationStatus = automation, Priority = "P1", FeatureIds = [feature], PrimaryFeatureId = feature,
                Platform = feature == portalBooking ? ApplicationType.Browser : ApplicationType.WinForms, Tags = ["sample"],
                Steps = [new TestStepDto(1, "Open the form from the main menu", "Form opens with empty fields", null, null), new TestStepDto(2, "Enter the test data and save", "Record is saved and a number is shown", null, null)],
            }, ChangeSummary: "Sample data"), ct);
            created.Add(tc.Id);
        }
        await suites.AddItemsAsync(smoke.Suite.Id, new SuiteItemsRequest(created), ct);
    }
}
