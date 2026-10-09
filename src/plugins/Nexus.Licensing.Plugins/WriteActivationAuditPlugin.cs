using System;
using Microsoft.Xrm.Sdk;

namespace Nexus.Licensing.Plugins
{
    /// <summary>
    /// Appends an <c>nx_activation</c> audit row whenever the status of an
    /// <c>nx_licensekey</c> changes.
    /// </summary>
    /// <remarks>
    /// <para><b>Registration:</b> Message <c>Update</c> · Table <c>nx_licensekey</c> ·
    /// Stage <b>PostOperation</b> (40) · Mode <b>Asynchronous</b> ·
    /// Filtering attributes: <c>nx_status</c> ·
    /// Pre Image <c>PreImage</c> and Post Image <c>PostImage</c>, both with
    /// <c>nx_status</c>, <c>nx_key</c>, <c>nx_boundorgalias</c>.</para>
    /// <para><b>Why PostOperation:</b> the audit row must only exist if the update actually
    /// succeeded. In PreOperation the transaction could still roll back, leaving an audit
    /// entry for something that never happened.</para>
    /// <para><b>Why asynchronous:</b> writing history is not worth making the user wait for, and
    /// a failure here must not roll back the status change itself.</para>
    /// <para>This is why <c>nx_fromstatus</c>/<c>nx_tostatus</c> are text rather than option set
    /// columns: an audit trail has to keep the label that was true at the time, even if the
    /// option set is edited later.</para>
    /// </remarks>
    public sealed class WriteActivationAuditPlugin : PluginBase
    {
        /// <summary>Initializes the plug-in and passes the class name to <see cref="PluginBase"/> tracing.</summary>
        public WriteActivationAuditPlugin() : base(typeof(WriteActivationAuditPlugin))
        {
        }

        /// <summary>Business entry point; <see cref="PluginBase"/> already wraps tracing and error handling.</summary>
        /// <param name="localPluginContext">Context unpacked from <c>IServiceProvider</c>.</param>
        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target) ||
                target.LogicalName != Tables.LicenseKey)
            {
                return;
            }

            // This plug-in writes to nx_activation, which cannot re-trigger it. The depth guard
            // is kept anyway so a future flow updating nx_licensekey cannot cause a loop.
            if (context.Depth > 1)
            {
                localPluginContext.Trace($"Skipped: Depth = {context.Depth}");
                return;
            }

            // --- 1. Both images are required ---------------------------------------------
            // Target only carries the changed columns, so the previous status is only available
            // from the pre-image. Missing images mean the step was registered incorrectly —
            // trace and give up rather than writing a half-empty audit row.
            if (!context.PreEntityImages.Contains("PreImage") ||
                !context.PostEntityImages.Contains("PostImage"))
            {
                localPluginContext.Trace(
                    "PreImage/PostImage not registered — skipping audit. Check the step registration.");
                return;
            }

            var preImage = context.PreEntityImages["PreImage"];
            var postImage = context.PostEntityImages["PostImage"];

            var fromStatus = preImage.GetAttributeValue<OptionSetValue>(LicenseKeyFields.Status);
            var toStatus = postImage.GetAttributeValue<OptionSetValue>(LicenseKeyFields.Status);

            // --- 2. Ignore updates that did not move the status ---------------------------
            // The filtering attribute narrows this down already, but an update writing the same
            // value still fires the step.
            if (toStatus == null || (fromStatus != null && fromStatus.Value == toStatus.Value))
            {
                localPluginContext.Trace("Status unchanged — nothing to audit.");
                return;
            }

            var action = MapStatusToAction(toStatus.Value);
            if (action == null)
            {
                localPluginContext.Trace($"No audit action maps to status {toStatus.Value} — skipping.");
                return;
            }

            // --- 3. Build the audit row ---------------------------------------------------
            var licenseKeyText = postImage.GetAttributeValue<string>(LicenseKeyFields.Key);
            var boundAlias = postImage.GetAttributeValue<string>(LicenseKeyFields.BoundOrgAlias);

            var fromLabel = DescribeStatus(fromStatus);
            var toLabel = DescribeStatus(toStatus);

            var subject = string.IsNullOrWhiteSpace(boundAlias)
                ? $"{toLabel} {Shorten(licenseKeyText)}"
                : $"{toLabel} {Shorten(licenseKeyText)} -> {boundAlias}";

            var audit = new Entity(Tables.Activation)
            {
                [ActivationFields.Subject] = subject,
                [ActivationFields.LicenseKey] = new EntityReference(Tables.LicenseKey, target.Id),
                [ActivationFields.Action] = new OptionSetValue(action.Value),

                // nx_actedon is User Local behavior, so Dataverse stores the UTC instant and
                // renders it in each viewer's time zone.
                [ActivationFields.ActedOn] = DateTime.UtcNow,

                // Stored as plain text so the history keeps the label that was true at the time.
                [ActivationFields.FromStatus] = fromLabel,
                [ActivationFields.ToStatus] = toLabel,
                [ActivationFields.Actor] = ResolveActor(localPluginContext)
            };

            // PluginUserService: the audit row must be written even when the acting user has no
            // create privilege on nx_activation — history is not optional.
            localPluginContext.PluginUserService.Create(audit);

            localPluginContext.Trace($"Audit written: {fromLabel} -> {toLabel}");
        }

        /// <summary>Maps a license status to the audit action that records reaching it.</summary>
        /// <param name="status">Value from <see cref="LicenseStatus"/>.</param>
        /// <returns>A value from <see cref="ActivationAction"/>, or null when the status is not audited.</returns>
        private static int? MapStatusToAction(int status)
        {
            switch (status)
            {
                case LicenseStatus.Issued: return ActivationAction.Issue;
                case LicenseStatus.Active: return ActivationAction.Activate;
                case LicenseStatus.Revoked: return ActivationAction.Revoke;
                case LicenseStatus.Expired: return ActivationAction.Expire;

                // Draft is the starting point, not an event worth recording.
                default: return null;
            }
        }

        /// <summary>Maps a status option set value to its label.</summary>
        /// <param name="status">Status value, may be null.</param>
        /// <returns>A human-readable status name.</returns>
        private static string DescribeStatus(OptionSetValue status)
        {
            if (status == null)
            {
                return "(none)";
            }

            switch (status.Value)
            {
                case LicenseStatus.Draft: return "Draft";
                case LicenseStatus.Issued: return "Issued";
                case LicenseStatus.Active: return "Active";
                case LicenseStatus.Expired: return "Expired";
                case LicenseStatus.Revoked: return "Revoked";
                default: return $"Unknown ({status.Value})";
            }
        }

        /// <summary>
        /// Resolves a display value for whoever triggered the change.
        /// </summary>
        /// <param name="localPluginContext">Context carrying the initiating user id.</param>
        /// <returns>The user's email, full name, or the raw id when the lookup fails.</returns>
        /// <remarks>
        /// <para>Reads <c>InitiatingUserId</c> — the person who triggered the change — rather than
        /// <c>UserId</c>, which is the account the step is configured to run as. The audit has to
        /// name the human who acted, not the service identity.</para>
        /// <para>The lookup itself goes through <c>PluginUserService</c> because a regular user
        /// usually has no read access to other rows of <c>systemuser</c>.</para>
        /// </remarks>
        private static string ResolveActor(ILocalPluginContext localPluginContext)
        {
            var userId = localPluginContext.PluginExecutionContext.InitiatingUserId;

            try
            {
                var user = localPluginContext.PluginUserService.Retrieve(
                    "systemuser",
                    userId,
                    new Microsoft.Xrm.Sdk.Query.ColumnSet("fullname", "internalemailaddress"));

                var email = user.GetAttributeValue<string>("internalemailaddress");
                var fullName = user.GetAttributeValue<string>("fullname");

                if (!string.IsNullOrWhiteSpace(email))
                {
                    return email;
                }

                return string.IsNullOrWhiteSpace(fullName) ? userId.ToString() : fullName;
            }
            catch (Exception ex)
            {
                // Never let audit metadata break the audit row itself.
                localPluginContext.Trace($"Could not resolve actor {userId}: {ex.Message}");
                return userId.ToString();
            }
        }

        /// <summary>Truncates a license key so the subject line stays readable.</summary>
        /// <param name="licenseKey">Full key text, may be null.</param>
        /// <returns>The first 12 characters followed by an ellipsis, or a placeholder.</returns>
        private static string Shorten(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                return "(no key)";
            }

            return licenseKey.Length <= 12 ? licenseKey : licenseKey.Substring(0, 12) + "...";
        }
    }
}
