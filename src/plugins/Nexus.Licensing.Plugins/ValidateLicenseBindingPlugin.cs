using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Nexus.Licensing.Plugins
{
    /// <summary>
    /// Enforces the license binding invariants when an <c>nx_orgrequest</c> is approved:
    /// the key must be issued, unexpired, not already bound, and the requested org alias
    /// must be free.
    /// </summary>
    /// <remarks>
    /// <para><b>Registration:</b> Message <c>Update</c> · Table <c>nx_orgrequest</c> ·
    /// Stage <b>PreValidation</b> (10) · Mode <b>Synchronous</b> ·
    /// Filtering attributes: <c>nx_provisionstate</c> ·
    /// Pre Image <c>PreImage</c> with <c>nx_licensekey</c>, <c>nx_orgalias</c>.</para>
    /// <para>PreValidation runs <i>outside</i> the database transaction, so rejecting here costs
    /// no rollback. That is the right stage for validation that can fail.</para>
    /// <para>This is the counterpart of the unique index on <c>Organization.LicenseKey</c> in
    /// DASHBOARD: one key binds to at most one organization. Dataverse alternate keys cannot
    /// express it because <c>nx_boundorgalias</c> is empty while a key is still Draft/Issued,
    /// and an alternate key does not allow multiple empty values.</para>
    /// </remarks>
    public sealed class ValidateLicenseBindingPlugin : PluginBase
    {
        /// <summary>Initializes the plug-in and passes the class name to <see cref="PluginBase"/> tracing.</summary>
        public ValidateLicenseBindingPlugin() : base(typeof(ValidateLicenseBindingPlugin))
        {
        }

        /// <summary>Business entry point; <see cref="PluginBase"/> already wraps tracing and error handling.</summary>
        /// <param name="localPluginContext">Context unpacked from <c>IServiceProvider</c>.</param>
        /// <exception cref="InvalidPluginExecutionException">When any binding invariant is violated.</exception>
        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target) ||
                target.LogicalName != Tables.OrgRequest)
            {
                return;
            }

            // --- 1. Only validate the Draft -> Approved transition -------------------------
            // On Update, Target holds ONLY the columns that changed. If nx_provisionstate is
            // absent the user edited something else, so there is nothing to validate.
            if (!target.Contains(OrgRequestFields.ProvisionState) ||
                !(target[OrgRequestFields.ProvisionState] is OptionSetValue newState) ||
                newState.Value != ProvisionState.Approved)
            {
                return;
            }

            // --- 2. Merge Target with the pre-image ---------------------------------------
            // The license key and alias usually are not part of this update, so read them from
            // the pre-image — the record as it looked before the update.
            var preImage = context.PreEntityImages.Contains("PreImage")
                ? context.PreEntityImages["PreImage"]
                : null;

            var keyRef = GetValue<EntityReference>(target, preImage, OrgRequestFields.LicenseKey);
            var orgAlias = GetValue<string>(target, preImage, OrgRequestFields.OrgAlias);

            if (keyRef == null)
            {
                throw new InvalidPluginExecutionException(
                    "This request has no License Key. Link one before approving it.");
            }

            if (string.IsNullOrWhiteSpace(orgAlias))
            {
                throw new InvalidPluginExecutionException(
                    "Org Alias is required before approving the request.");
            }

            orgAlias = orgAlias.Trim().ToLowerInvariant();
            localPluginContext.Trace($"Validating key {keyRef.Id} against alias '{orgAlias}'");

            // PluginUserService: the validation must see every license key in the system,
            // including ones the approver has no read access to.
            var service = localPluginContext.PluginUserService;

            // --- 3. Invariants on the license key itself ----------------------------------
            var key = service.Retrieve(
                Tables.LicenseKey,
                keyRef.Id,
                new ColumnSet(
                    LicenseKeyFields.Key,
                    LicenseKeyFields.Status,
                    LicenseKeyFields.ExpireDate,
                    LicenseKeyFields.BoundOrgAlias));

            var status = key.GetAttributeValue<OptionSetValue>(LicenseKeyFields.Status);
            if (status == null || status.Value != LicenseStatus.Issued)
            {
                throw new InvalidPluginExecutionException(
                    $"Only an Issued license key can be approved. Current status: {DescribeStatus(status)}.");
            }

            // nx_expiredate is Date Only, so compare against UtcNow.Date to avoid an off-by-one
            // day caused by the time part.
            var expireDate = key.GetAttributeValue<DateTime?>(LicenseKeyFields.ExpireDate);
            if (expireDate.HasValue && expireDate.Value.Date < DateTime.UtcNow.Date)
            {
                throw new InvalidPluginExecutionException(
                    $"This license key expired on {expireDate.Value:yyyy-MM-dd} and cannot be used.");
            }

            var boundAlias = key.GetAttributeValue<string>(LicenseKeyFields.BoundOrgAlias);
            if (!string.IsNullOrWhiteSpace(boundAlias))
            {
                throw new InvalidPluginExecutionException(
                    $"This license key is already bound to organization '{boundAlias}'. " +
                    "One key can only ever bind to one organization.");
            }

            // --- 4. The alias must be free across every other key -------------------------
            EnsureAliasIsFree(service, orgAlias, keyRef.Id, localPluginContext);

            localPluginContext.Trace("All binding invariants satisfied.");
        }

        /// <summary>
        /// Rejects the approval when another license key has already claimed the same org alias.
        /// </summary>
        /// <param name="service">Organization service used to query.</param>
        /// <param name="orgAlias">Normalised (lower-case, trimmed) alias being requested.</param>
        /// <param name="currentKeyId">Id of the key being approved, excluded from the search.</param>
        /// <param name="localPluginContext">Context, used for tracing.</param>
        /// <exception cref="InvalidPluginExecutionException">When the alias is taken.</exception>
        private static void EnsureAliasIsFree(
            IOrganizationService service,
            string orgAlias,
            Guid currentKeyId,
            ILocalPluginContext localPluginContext)
        {
            var query = new QueryExpression(Tables.LicenseKey)
            {
                ColumnSet = new ColumnSet(LicenseKeyFields.Key),
                // One hit is enough to reject; do not pay for a full scan.
                TopCount = 1,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            LicenseKeyFields.BoundOrgAlias, ConditionOperator.Equal, orgAlias),
                        new ConditionExpression(
                            LicenseKeyFields.LicenseKeyId, ConditionOperator.NotEqual, currentKeyId)
                    }
                }
            };

            var clash = service.RetrieveMultiple(query);
            if (clash.Entities.Count > 0)
            {
                var other = clash.Entities[0].GetAttributeValue<string>(LicenseKeyFields.Key);
                localPluginContext.Trace($"Alias '{orgAlias}' already taken by key {other}");

                throw new InvalidPluginExecutionException(
                    $"Organization alias '{orgAlias}' is already bound to another license key. " +
                    "Aliases are unique across all of DASHBOARD — pick a different one.");
            }
        }

        /// <summary>
        /// Reads a column from the update payload, falling back to the pre-image when the update
        /// did not touch it.
        /// </summary>
        /// <typeparam name="T">Expected attribute type.</typeparam>
        /// <param name="target">The Target entity, holding only the changed columns.</param>
        /// <param name="preImage">The pre-image, or null when no image was registered.</param>
        /// <param name="attribute">Logical name of the column.</param>
        /// <returns>The value, or <c>default</c> when neither source has it.</returns>
        private static T GetValue<T>(Entity target, Entity preImage, string attribute)
        {
            if (target.Contains(attribute))
            {
                return target.GetAttributeValue<T>(attribute);
            }

            return preImage != null ? preImage.GetAttributeValue<T>(attribute) : default(T);
        }

        /// <summary>Maps a status option set value to its label for use in error messages.</summary>
        /// <param name="status">Status value, may be null.</param>
        /// <returns>A human-readable status name.</returns>
        private static string DescribeStatus(OptionSetValue status)
        {
            if (status == null)
            {
                return "(not set)";
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
    }
}
