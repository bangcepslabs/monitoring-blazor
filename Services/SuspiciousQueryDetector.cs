using System.Net;

namespace Monitoring.Blazor.Services;

public sealed record SuspiciousQueryMatch(string QueryString, IReadOnlyList<string> Reasons);

public static class SuspiciousQueryDetector
{
    public static SuspiciousQueryMatch? Detect(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        var questionIndex = uri.IndexOf('?');
        if (questionIndex < 0 || questionIndex == uri.Length - 1) return null;
        var query = uri[(questionIndex + 1)..];
        var hashIndex = query.IndexOf('#');
        if (hashIndex >= 0) query = query[..hashIndex];
        query = query.Trim();
        if (string.IsNullOrWhiteSpace(query)) return null;

        var reasons = new List<string>();
        var lowerVariants = ExpandQueryVariants(query)
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (query.Length >= 120) reasons.Add("long_query");
        if (lowerVariants.Any(lower => lower.Contains("%2e%2e") || lower.Contains("../") || lower.Contains("..\\") ||
            lower.Contains("%5c") || lower.Contains("%2f") || lower.Contains("%252e%252e") || lower.Contains("..%2f") || lower.Contains("..%5c")))
            reasons.Add("traversal_or_encoded_path");
        if (lowerVariants.Any(lower => lower.Contains("<script") || lower.Contains("%3cscript") || lower.Contains("onerror=") ||
            lower.Contains("onload=") || lower.Contains("alert(") || lower.Contains("javascript:") || lower.Contains("document.cookie") ||
            lower.Contains("svg/onload") || lower.Contains("img src=x")))
            reasons.Add("xss_pattern");
        if (lowerVariants.Any(lower => lower.Contains("union select") || lower.Contains("information_schema") || lower.Contains(" or 1=1") ||
            lower.Contains("' or '1'='1") || lower.Contains("\" or \"1\"=\"1") || lower.Contains("sleep(") || lower.Contains("benchmark(") ||
            lower.Contains("waitfor delay") || lower.Contains("xp_cmdshell") || lower.Contains("select@@version") || lower.Contains("cast(") || lower.Contains("convert(")))
            reasons.Add("sqli_pattern");
        if (lowerVariants.Any(lower => lower.Contains("cmd=") || lower.Contains("exec=") || lower.Contains("powershell") || lower.Contains("wget ") ||
            lower.Contains("curl ") || lower.Contains("bash ") || lower.Contains("cmd.exe") || lower.Contains("/bin/sh") || lower.Contains("/bin/bash") ||
            lower.Contains("nc -e") || lower.Contains("certutil") || lower.Contains("tftp ")))
            reasons.Add("command_injection_pattern");
        if (lowerVariants.Any(lower => lower.Contains("/etc/passwd") || lower.Contains("web.config") || lower.Contains("win.ini") ||
            lower.Contains("administrator") || lower.Contains("phpmyadmin") || lower.Contains("wp-") || lower.Contains(".git/config") ||
            lower.Contains(".svn/entries") || lower.Contains("id_rsa") || lower.Contains("docker-compose") || lower.Contains("application.properties")))
            reasons.Add("probing_pattern");
        if (lowerVariants.Any(lower => lower.Contains("file=") || lower.Contains("path=") || lower.Contains("page=") || lower.Contains("include=") ||
            lower.Contains("template=") || lower.Contains("php://") || lower.Contains("file://") || lower.Contains("data://") || lower.Contains("expect://")))
            reasons.Add("lfi_or_rfi_pattern");
        if (lowerVariants.Any(lower => lower.Contains("${jndi:") || lower.Contains("${::-j}") || lower.Contains("#{") ||
            lower.Contains("class.module.classloader") || lower.Contains("_method=delete") || lower.Contains("_method=put") ||
            lower.Contains("deserialize") || lower.Contains("ysoserial")))
            reasons.Add("rce_or_framework_probe_pattern");
        if (query.Count(character => character == '&') >= 6) reasons.Add("many_parameters");

        return reasons.Count == 0 ? null : new SuspiciousQueryMatch(query, reasons);
    }

    private static IReadOnlyList<string> ExpandQueryVariants(string query)
    {
        var values = new List<string> { query };
        var current = query;
        for (var i = 0; i < 2; i++)
        {
            string decoded;
            try { decoded = WebUtility.UrlDecode(current); }
            catch { break; }
            if (string.IsNullOrWhiteSpace(decoded) || values.Contains(decoded, StringComparer.OrdinalIgnoreCase)) break;
            values.Add(decoded);
            current = decoded;
        }
        return values;
    }
}
