namespace Nexus.Licensing.Plugins
{
    /// <summary>Logical names of the Dataverse tables in the NexusLicensing solution.</summary>
    public static class Tables
    {
        /// <summary>Pre-defined license plan (capacity, duration, grace period).</summary>
        public const string LicensePlan = "nx_licenseplan";

        /// <summary>Customer buying the license.</summary>
        public const string Customer = "nx_customer";

        /// <summary>License key — the aggregate root.</summary>
        public const string LicenseKey = "nx_licensekey";

        /// <summary>Org creation request, provisioned into DASHBOARD.</summary>
        public const string OrgRequest = "nx_orgrequest";

        /// <summary>Immutable audit trail.</summary>
        public const string Activation = "nx_activation";
    }

    /// <summary>Logical names of the columns on <c>nx_licenseplan</c>.</summary>
    public static class PlanFields
    {
        /// <summary>Plan name (primary column).</summary>
        public const string PlanName = "nx_planname";

        /// <summary>Plan code: TRIAL / STD / ENT — embedded in the license key.</summary>
        public const string PlanCode = "nx_plancode";

        /// <summary>Maximum number of repositories the license allows.</summary>
        public const string RepoCapacity = "nx_repocapacity";

        /// <summary>License duration in months.</summary>
        public const string DurationMonths = "nx_durationmonths";

        /// <summary>Grace period in days between the due date and the expiry date.</summary>
        public const string GraceDays = "nx_gracedays";

        /// <summary>Whether the plan is still on sale.</summary>
        public const string IsActive = "nx_isactive";
    }

    /// <summary>Logical names of the columns on <c>nx_licensekey</c>.</summary>
    public static class LicenseKeyFields
    {
        /// <summary>Primary key column (GUID).</summary>
        public const string LicenseKeyId = "nx_licensekeyid";

        /// <summary>Opaque base64 key string (primary column).</summary>
        public const string Key = "nx_key";

        /// <summary>Lookup to <c>nx_customer</c>.</summary>
        public const string Customer = "nx_customer";

        /// <summary>Lookup to <c>nx_licenseplan</c>.</summary>
        public const string Plan = "nx_plan";

        /// <summary>Lifecycle status — see <see cref="LicenseStatus"/>.</summary>
        public const string Status = "nx_status";

        /// <summary>Payment due date (Date Only).</summary>
        public const string DueDate = "nx_duedate";

        /// <summary>License expiry date (Date Only).</summary>
        public const string ExpireDate = "nx_expiredate";

        /// <summary>Maximum repository count — copied from the plan, still editable.</summary>
        public const string RepoCapacity = "nx_repocapacity";

        /// <summary>Alias of the DASHBOARD organization this key is bound to.</summary>
        public const string BoundOrgAlias = "nx_boundorgalias";

        /// <summary>GUID of the DASHBOARD organization this key is bound to.</summary>
        public const string BoundOrgId = "nx_boundorgid";

        /// <summary>Moment provisioning completed (User Local).</summary>
        public const string ActivatedOn = "nx_activatedon";

        /// <summary>Free-form notes.</summary>
        public const string Notes = "nx_notes";
    }

    /// <summary>Logical names of the columns on <c>nx_orgrequest</c>.</summary>
    public static class OrgRequestFields
    {
        /// <summary>Organization display name (primary column).</summary>
        public const string OrgName = "nx_orgname";

        /// <summary>URL-safe organization alias, unique across all of DASHBOARD.</summary>
        public const string OrgAlias = "nx_orgalias";

        /// <summary>Lookup to <c>nx_licensekey</c>.</summary>
        public const string LicenseKey = "nx_licensekey";

        /// <summary>Name of the first admin user.</summary>
        public const string AdminName = "nx_adminname";

        /// <summary>Email of the first admin user.</summary>
        public const string AdminEmail = "nx_adminemail";

        /// <summary>Organization description.</summary>
        public const string OrgAbout = "nx_orgabout";

        /// <summary>Provisioning state — see <see cref="ProvisionState"/>.</summary>
        public const string ProvisionState = "nx_provisionstate";

        /// <summary>GUID of the organization returned by DASHBOARD.</summary>
        public const string DashboardOrgId = "nx_dashboardorgid";

        /// <summary>Login URL handed back to the customer.</summary>
        public const string LoginUrl = "nx_loginurl";

        /// <summary>Moment the request was raised (User Local).</summary>
        public const string RequestedOn = "nx_requestedon";

        /// <summary>Moment provisioning completed (User Local).</summary>
        public const string CompletedOn = "nx_completedon";

        /// <summary>Failure details written by the provisioning flow.</summary>
        public const string ErrorMessage = "nx_errormessage";

        /// <summary>Number of provisioning retries.</summary>
        public const string RetryCount = "nx_retrycount";
    }

    /// <summary>Logical names of the columns on <c>nx_activation</c>.</summary>
    public static class ActivationFields
    {
        /// <summary>Human-readable summary of the event (primary column).</summary>
        public const string Subject = "nx_subject";

        /// <summary>Lookup to <c>nx_licensekey</c>.</summary>
        public const string LicenseKey = "nx_licensekey";

        /// <summary>What happened — see <see cref="ActivationAction"/>.</summary>
        public const string Action = "nx_action";

        /// <summary>When it happened (User Local).</summary>
        public const string ActedOn = "nx_actedon";

        /// <summary>Who triggered it.</summary>
        public const string Actor = "nx_actor";

        /// <summary>Status before the change, stored as text so history stays stable.</summary>
        public const string FromStatus = "nx_fromstatus";

        /// <summary>Status after the change, stored as text so history stays stable.</summary>
        public const string ToStatus = "nx_tostatus";

        /// <summary>Free-form note.</summary>
        public const string Note = "nx_note";
    }

    /// <summary>
    /// Values of the <c>nx_orgrequestprovisionstate</c> option set (Org Request Provision State).
    /// Global option set, so values start at 0.
    /// </summary>
    public static class ProvisionState
    {
        /// <summary>Being filled in by Sales.</summary>
        public const int Draft = 0;

        /// <summary>Approved by Ops, ready to provision.</summary>
        public const int Approved = 1;

        /// <summary>Provisioning call in flight.</summary>
        public const int Provisioning = 2;

        /// <summary>Organization created successfully in DASHBOARD.</summary>
        public const int Completed = 3;

        /// <summary>Provisioning failed — see <c>nx_errormessage</c>.</summary>
        public const int Failed = 4;
    }

    /// <summary>
    /// Values of the <c>nx_draft</c> option set (License Key Status).
    /// This option set is <b>global</b>, so its values start at 0 rather than 100000000.
    /// </summary>
    public static class LicenseStatus
    {
        /// <summary>Just created, key not generated yet.</summary>
        public const int Draft = 0;

        /// <summary>Key generated and sent to the customer, not yet used.</summary>
        public const int Issued = 1;

        /// <summary>Bound to a DASHBOARD organization.</summary>
        public const int Active = 2;

        /// <summary>Past <c>nx_expiredate</c>.</summary>
        public const int Expired = 3;

        /// <summary>Revoked (breach of terms, refund).</summary>
        public const int Revoked = 4;
    }

    /// <summary>Values of the <c>nx_activationaction</c> option set (Activation Action).</summary>
    public static class ActivationAction
    {
        /// <summary>Key issued for the first time.</summary>
        public const int Issue = 0;

        /// <summary>Activated — bound to an organization.</summary>
        public const int Activate = 1;

        /// <summary>Renewed.</summary>
        public const int Renew = 2;

        /// <summary>Revoked.</summary>
        public const int Revoke = 3;

        /// <summary>Expired.</summary>
        public const int Expire = 4;
    }

    /// <summary>Dataverse pipeline message names the plug-ins register against.</summary>
    public static class Messages
    {
        /// <summary>The Create message.</summary>
        public const string Create = "Create";

        /// <summary>The Update message.</summary>
        public const string Update = "Update";
    }
}
