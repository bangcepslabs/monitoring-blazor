namespace Monitoring.Blazor.Services;

public sealed record IpRiskEvidence(
    int Score,
    int RequestCount,
    double RefererMissingRatio,
    int ErrorCount,
    int SuspiciousQueryCount,
    bool IsBot,
    int AdminProbeCount,
    int AdminProbeErrorCount,
    int BackupProbeCount,
    int ConfigProbeCount,
    int ScriptProbeCount);

public static class IpRecommendedActionService
{
    public static string Recommend(IpRiskEvidence evidence)
    {
        var unnecessaryAutomatedAccess = evidence.RequestCount >= 50 &&
            evidence.RefererMissingRatio >= 0.9 && (evidence.IsBot || evidence.SuspiciousQueryCount > 0);
        var abnormalUrlAccess = evidence.ConfigProbeCount > 0 || evidence.BackupProbeCount > 0 ||
            evidence.ScriptProbeCount > 0 || (evidence.AdminProbeCount > 0 && evidence.AdminProbeErrorCount > 0);
        var adminOrAbnormalUrlAccess = evidence.AdminProbeCount > 0 || abnormalUrlAccess;

        if (unnecessaryAutomatedAccess) return "Block";
        if (abnormalUrlAccess) return "Block";
        if (adminOrAbnormalUrlAccess) return evidence.AdminProbeErrorCount > 0 ? "Block" : "RateLimit";
        if (evidence.IsBot && evidence.Score < 30) return "Allow";

        var errorRate = evidence.RequestCount == 0 ? 0 : evidence.ErrorCount / (double)evidence.RequestCount;
        if (evidence.Score >= 80 || evidence.SuspiciousQueryCount >= 10 || errorRate >= 0.6) return "Block";
        if (evidence.Score >= 50 || evidence.SuspiciousQueryCount >= 5 || errorRate >= 0.3 || evidence.RefererMissingRatio >= 0.8) return "RateLimit";
        if (evidence.Score >= 20 || evidence.RefererMissingRatio >= 0.5 || errorRate >= 0.15) return "Monitor";
        return "Allow";
    }
}
