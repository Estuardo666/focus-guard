using System.Security.Cryptography;
using System.Text.Json;

namespace FocusGuard.Domain;

public sealed record PolicyAcknowledgement(Guid PolicyId, string PolicyHash);

public sealed record PolicyDelivery(
    int SchemaVersion,
    Guid PolicyId,
    string PolicyHash,
    EnforcementPolicy Policy)
{
    public const int CurrentSchemaVersion = 1;

    public static PolicyDelivery Create(EnforcementPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var policyId = Guid.NewGuid();
        return new PolicyDelivery(
            CurrentSchemaVersion,
            policyId,
            ComputeHash(CurrentSchemaVersion, policyId, policy),
            policy);
    }

    public bool HasValidIntegrity()
    {
        try
        {
            return SchemaVersion == CurrentSchemaVersion
                && PolicyId != Guid.Empty
                && Policy.SessionId != Guid.Empty
                && Policy.EndUtc > Policy.StartUtc
                && PolicyHash.Length == 64
                && CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(PolicyHash),
                    Convert.FromHexString(ComputeHash(SchemaVersion, PolicyId, Policy)));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ComputeHash(int schemaVersion, Guid policyId, EnforcementPolicy policy)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", schemaVersion);
            writer.WriteString("policyId", policyId);
            writer.WriteString("sessionId", policy.SessionId);
            writer.WriteString("profileId", policy.ProfileId.Value);
            writer.WriteString("startUtc", policy.StartUtc.ToUniversalTime());
            writer.WriteString("endUtc", policy.EndUtc.ToUniversalTime());
            writer.WriteBoolean("isStrict", policy.IsStrict);
            writer.WriteBoolean("enforcementEnabled", policy.EnforcementEnabled);
            writer.WriteStartArray("applicationRules");
            foreach (var rule in policy.ApplicationRules)
            {
                writer.WriteStartObject();
                writer.WriteString("id", rule.Id);
                writer.WriteString("action", rule.Action.ToString());
                writer.WriteString("matchKind", rule.MatchKind.ToString());
                writer.WriteString("value", rule.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("websiteRules");
            foreach (var rule in policy.WebsiteRules)
            {
                writer.WriteStartObject();
                writer.WriteString("id", rule.Id);
                writer.WriteString("action", rule.Action.ToString());
                writer.WriteString("pattern", rule.Pattern);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}

public interface IPolicyDistributionTarget
{
    ValueTask<PolicyAcknowledgement> ReceiveAsync(
        PolicyDelivery delivery,
        CancellationToken cancellationToken);

    ValueTask ClearAsync(Guid policyId, CancellationToken cancellationToken);
}

public interface IConfirmedPolicyDistributionTarget : IPolicyDistributionTarget
{
    ValueTask SetConfirmedAsync(
        Guid policyId,
        bool confirmed,
        CancellationToken cancellationToken);
}

public interface IExternallyAcknowledgedPolicyDistributionTarget :
    IConfirmedPolicyDistributionTarget
{
}
