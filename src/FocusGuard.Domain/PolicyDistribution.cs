using System.Globalization;
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
            WritePlan(writer, policy.CyclePlan);
            writer.WriteStartArray("breakApplicationRules");
            foreach (var rule in policy.BreakApplicationRules ?? [])
            {
                WriteApplicationRule(writer, rule);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("breakWebsiteRules");
            foreach (var rule in policy.BreakWebsiteRules ?? [])
            {
                WriteWebsiteRule(writer, rule);
            }

            writer.WriteEndArray();
            writer.WriteNumber("authorizedPhaseIndex", policy.AuthorizedPhaseIndex);
            if (policy.AuthorizedPhaseEndUtc is { } phaseEndUtc)
            {
                writer.WriteString("authorizedPhaseEndUtc", phaseEndUtc.ToUniversalTime());
            }
            else
            {
                writer.WriteNull("authorizedPhaseEndUtc");
            }
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WritePlan(Utf8JsonWriter writer, FocusCyclePlan? plan)
    {
        if (plan is null)
        {
            writer.WriteNull("cyclePlan");
            return;
        }

        writer.WriteStartObject("cyclePlan");
        writer.WriteStartObject("configuration");
        writer.WriteString("focusDuration", plan.Configuration.FocusDuration.ToString("c", CultureInfo.InvariantCulture));
        writer.WriteString("shortBreakDuration", plan.Configuration.ShortBreakDuration.ToString("c", CultureInfo.InvariantCulture));
        writer.WriteString("longBreakDuration", plan.Configuration.LongBreakDuration.ToString("c", CultureInfo.InvariantCulture));
        writer.WriteNumber("cyclesBeforeLongBreak", plan.Configuration.CyclesBeforeLongBreak);
        writer.WriteString("terminationMode", plan.Configuration.TerminationMode.ToString());
        if (plan.Configuration.CycleCount is { } cycleCount)
        {
            writer.WriteNumber("cycleCount", cycleCount);
        }
        else
        {
            writer.WriteNull("cycleCount");
        }

        if (plan.Configuration.TotalDuration is { } totalDuration)
        {
            writer.WriteString("totalDuration", totalDuration.ToString("c", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNull("totalDuration");
        }

        writer.WriteEndObject();
        writer.WriteStartArray("phases");
        foreach (var phase in plan.Phases)
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", phase.Index);
            writer.WriteString("kind", phase.Kind.ToString());
            writer.WriteString("startUtc", phase.StartUtc.ToUniversalTime());
            writer.WriteString("endUtc", phase.EndUtc.ToUniversalTime());
            writer.WriteNumber("focusCycleNumber", phase.FocusCycleNumber);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("endUtc", plan.EndUtc.ToUniversalTime());
        writer.WriteEndObject();
    }

    private static void WriteApplicationRule(Utf8JsonWriter writer, ApplicationRule rule)
    {
        writer.WriteStartObject();
        writer.WriteString("id", rule.Id);
        writer.WriteString("action", rule.Action.ToString());
        writer.WriteString("matchKind", rule.MatchKind.ToString());
        writer.WriteString("value", rule.Value);
        writer.WriteEndObject();
    }

    private static void WriteWebsiteRule(Utf8JsonWriter writer, WebsiteRule rule)
    {
        writer.WriteStartObject();
        writer.WriteString("id", rule.Id);
        writer.WriteString("action", rule.Action.ToString());
        writer.WriteString("pattern", rule.Pattern);
        writer.WriteEndObject();
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
