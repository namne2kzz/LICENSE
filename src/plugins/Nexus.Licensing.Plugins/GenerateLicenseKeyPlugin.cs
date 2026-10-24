using System;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Nexus.Licensing.Plugins
{
    /// <summary>
    /// Generates the license key and fills in the fields derived from the selected plan
    /// when a new <c>nx_licensekey</c> record is created.
    /// </summary>
    /// <remarks>
    /// <para><b>Registration:</b> Message <c>Create</c> · Table <c>nx_licensekey</c> ·
    /// Stage <b>PreOperation</b> (20) · Mode <b>Synchronous</b>.</para>
    /// <para>PreOperation is used because the plug-in mutates <c>Target</c> <i>before</i> Dataverse
    /// writes it to the database — assigning values is enough, no second Update call is needed.</para>
    /// </remarks>
    public sealed class GenerateLicenseKeyPlugin : PluginBase
    {
        /// <summary>Initializes the plug-in and passes the class name to <see cref="PluginBase"/> tracing.</summary>
        public GenerateLicenseKeyPlugin() : base(typeof(GenerateLicenseKeyPlugin))
        {
        }

        /// <summary>Business entry point; <see cref="PluginBase"/> already wraps tracing and error handling.</summary>
        /// <param name="localPluginContext">Context unpacked from <c>IServiceProvider</c>.</param>
        /// <exception cref="InvalidPluginExecutionException">When no plan is selected or the plan is retired.</exception>
        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;

            // --- 1. Guard: only run in the registered context -----------------------------
            // The step may be registered against the wrong table, or re-entered because another
            // plug-in wrote to this table. Verify instead of trusting the configuration.
            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target) ||
                target.LogicalName != Tables.LicenseKey)
            {
                return;
            }

            // Depth > 1 means this plug-in was triggered by another plug-in or flow.
            // Skip it to avoid infinite recursion.
            if (context.Depth > 1)
            {
                localPluginContext.Trace($"Skipped: Depth = {context.Depth}");
                return;
            }

            // --- 2. Read the plan the user selected ---------------------------------------
            if (!target.Contains(LicenseKeyFields.Plan) ||
                !(target[LicenseKeyFields.Plan] is EntityReference planRef))
            {
                throw new InvalidPluginExecutionException(
                    "Select a License Plan before creating a license key.");
            }

            // PluginUserService (not InitiatingUserService): the plan is shared configuration
            // data and Sales users may not have read access to that table.
            var plan = localPluginContext.PluginUserService.Retrieve(
                Tables.LicensePlan,
                planRef.Id,
                new ColumnSet(
                    PlanFields.PlanCode,
                    PlanFields.RepoCapacity,
                    PlanFields.DurationMonths,
                    PlanFields.GraceDays,
                    PlanFields.IsActive));

            if (plan.GetAttributeValue<bool>(PlanFields.IsActive) == false)
            {
                throw new InvalidPluginExecutionException(
                    "This license plan is retired. Pick a different plan.");
            }

            var planCode = plan.GetAttributeValue<string>(PlanFields.PlanCode);
            var repoCapacity = plan.GetAttributeValue<int>(PlanFields.RepoCapacity);
            var durationMonths = plan.GetAttributeValue<int>(PlanFields.DurationMonths);

            // nx_gracedays is optional and has no default value (see DATA-MODEL §1):
            // null and 0 yield the same result, so normalise null to 0.
            var graceDays = plan.GetAttributeValue<int?>(PlanFields.GraceDays) ?? 0;

            localPluginContext.Trace(
                $"Plan {planCode}: capacity={repoCapacity}, duration={durationMonths}m, grace={graceDays}d");

            // --- 3. Compute the dates -----------------------------------------------------
            // DateTime.UtcNow, NOT DateTime.Now: the Dataverse sandbox runs in the time zone of
            // the Microsoft datacenter, not the user's.
            // .Date drops the time part because nx_duedate/nx_expiredate use Date Only behavior.
            var today = DateTime.UtcNow.Date;
            var expireDate = today.AddMonths(durationMonths);
            var dueDate = expireDate.AddDays(-graceDays);

            // --- 4. Assign onto Target ----------------------------------------------------
            // Being in PreOperation, assigning is enough — Dataverse persists the whole entity
            // once the plug-in returns. Never call Update here (it would recurse).
            target[LicenseKeyFields.Key] = BuildLicenseKey(planCode);
            target[LicenseKeyFields.DueDate] = dueDate;
            target[LicenseKeyFields.ExpireDate] = expireDate;
            target[LicenseKeyFields.Status] = new OptionSetValue(LicenseStatus.Issued);

            // Capacity ALWAYS comes from the plan, overwriting anything the user typed.
            // An earlier version only filled it when left blank, which let a key end up with a
            // capacity that silently contradicted its plan (e.g. plan Standard = 10 repos but the
            // key says 2). The plan is the single source of truth; to sell a different capacity,
            // create a new plan. The form marks this column read-only to match.
            target[LicenseKeyFields.RepoCapacity] = repoCapacity;

            localPluginContext.Trace(
                $"Key generated, due={dueDate:yyyy-MM-dd}, expire={expireDate:yyyy-MM-dd}");
        }

        /// <summary>
        /// Builds an opaque base64 license key in the form <c>NEX-{planCode}-{random}</c>.
        /// </summary>
        /// <param name="planCode">Plan code, e.g. <c>STD</c>. Null or blank falls back to <c>GEN</c>.</param>
        /// <returns>A URL-safe base64 string containing no characters that need escaping.</returns>
        /// <remarks>
        /// The key is <b>opaque</b>: the actual data (dates, capacity) lives in dedicated columns
        /// rather than being embedded. Decoding the base64 only reveals the plan code and a random
        /// string, so nothing sensitive leaks.
        /// </remarks>
        private static string BuildLicenseKey(string planCode)
        {
            var code = string.IsNullOrWhiteSpace(planCode) ? "GEN" : planCode.Trim().ToUpperInvariant();

            // Guid.NewGuid() is random enough for an identifier; this is not a cryptographic secret.
            var random = Guid.NewGuid().ToString("N").Substring(0, 16).ToUpperInvariant();
            var raw = $"NEX-{code}-{random}";

            // URL-safe base64: swap '+' and '/' and drop '=' so the key is safe inside URLs and JSON.
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}
