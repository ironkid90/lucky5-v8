namespace Lucky5.Tests;

public static class WebCabinetRegressionTests
{
    public static async Task RunAsync(List<string> failures)
    {
        string program;
        string indexHtml;
        string gameJs;
        string readme;
        string devScript;

        try
        {
            program = await File.ReadAllTextAsync(ResolveRepoFilePath("server", "src", "Lucky5.Api", "Program.cs"));
            indexHtml = await File.ReadAllTextAsync(ResolveRepoFilePath("server", "src", "Lucky5.Api", "wwwroot", "index.html"));
            gameJs = await File.ReadAllTextAsync(ResolveRepoFilePath("server", "src", "Lucky5.Api", "wwwroot", "js", "game.js"));
            readme = await File.ReadAllTextAsync(ResolveRepoFilePath("README.md"));
            devScript = await File.ReadAllTextAsync(ResolveRepoFilePath("dev.ps1"));
        }
        catch (Exception ex)
        {
            failures.Add($"Web cabinet regression setup failed: {ex.Message}");
            return;
        }

        Assert(
            failures,
            "Lucky5 v8 should serve the built-in cabinet from the ASP.NET host with default files, static files, and index fallback routing.",
            program.Contains("app.UseDefaultFiles();", StringComparison.Ordinal)
                && program.Contains("app.UseStaticFiles();", StringComparison.Ordinal)
                && program.Contains("app.MapFallbackToFile(\"index.html\");", StringComparison.Ordinal));

        Assert(
            failures,
            "The primary web cabinet should keep the auth screen, paytable, machine info, and physical control deck in the shipped shell.",
            indexHtml.Contains("id=\"auth-screen\"", StringComparison.Ordinal)
                && indexHtml.Contains("id=\"paytable\"", StringComparison.Ordinal)
                && indexHtml.Contains("id=\"machine-info-block\"", StringComparison.Ordinal)
                && indexHtml.Contains("id=\"controls\"", StringComparison.Ordinal)
                && indexHtml.Contains("id=\"btn-deal\"", StringComparison.Ordinal)
                && indexHtml.Contains("id=\"btn-bet\"", StringComparison.Ordinal)
                && indexHtml.Contains("id=\"btn-take-score\"", StringComparison.Ordinal));

        Assert(
            failures,
            "The shipped cabinet shell should load the vnext presentation modules that carry the current web-native stage, audio, and orchestration behavior.",
            indexHtml.Contains("/js/cabinet-stage-vnext.js", StringComparison.Ordinal)
                && indexHtml.Contains("/js/cabinet-audio-vnext.js", StringComparison.Ordinal)
                && indexHtml.Contains("/js/cabinet-orchestrator-vnext.js", StringComparison.Ordinal)
                && indexHtml.Contains("/js/cabinet-v8-effects.js", StringComparison.Ordinal));

        Assert(
            failures,
            "The browser cabinet runtime should keep login, OTP bootstrap, lobby fallback, and menu/logout wiring in the main client script.",
            gameJs.Contains("/api/Auth/login", StringComparison.Ordinal)
                && gameJs.Contains("/api/Auth/verify-otp", StringComparison.Ordinal)
                && gameJs.Contains("allowLobbyFallback: true", StringComparison.Ordinal)
                && gameJs.Contains("btn-logout-menu", StringComparison.Ordinal));

        Assert(
            failures,
            "Repository docs and launcher should describe Lucky5 v8 as a web-native cabinet instead of a Godot-first product.",
            readme.Contains("web-native", StringComparison.OrdinalIgnoreCase)
                && readme.Contains("wwwroot", StringComparison.Ordinal)
                && devScript.Contains("Lucky5 v8 - Web Cabinet", StringComparison.Ordinal)
                && devScript.Contains("Opening web cabinet", StringComparison.Ordinal)
                && !devScript.Contains("Godot", StringComparison.Ordinal));
    }

    private static string ResolveRepoFilePath(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(segments)}' from base directory '{AppContext.BaseDirectory}'");
    }

    private static void Assert(List<string> failures, string message, bool condition)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }
}
